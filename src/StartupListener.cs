using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MuMuClean
{
    // Standalone asInvoker executable. Uses window creation events, never a process polling loop.
    static class StartupListener
    {
        internal const string StopEvent = "Local\\MuMuClean.StartupListener.Stop";
        delegate void WindowEvent(IntPtr hook, uint evt, IntPtr window, int objectId, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WindowEvent callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        static readonly ConcurrentDictionary<int, byte> seen = new ConcurrentDictionary<int, byte>();
        static readonly CancellationTokenSource stopping = new CancellationTokenSource();
        static WindowEvent callback;
        static string adb, manager, instance, created, stateDirectory;

        [STAThread]
        static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, "Local\\MuMuClean.StartupListener", out first))
            {
                if (!first) return;
                using (RegistryKey settings = Registry.CurrentUser.OpenSubKey("Software\\MuMuClean"))
                {
                    adb = settings == null ? null : settings.GetValue("StartupAdb") as string;
                    instance = settings == null ? null : settings.GetValue("StartupInstance") as string;
                    created = settings == null ? null : settings.GetValue("StartupCreated") as string;
                    stateDirectory = settings == null ? null : settings.GetValue("StartupStateDirectory") as string;
                }
                string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MuMuAdCleaner");
                if (stateDirectory == null) stateDirectory = common;
                stateDirectory = Path.GetFullPath(stateDirectory).TrimEnd('\\');
                if (!String.Equals(stateDirectory, common, StringComparison.OrdinalIgnoreCase) && !stateDirectory.StartsWith(common + "\\", StringComparison.OrdinalIgnoreCase)) return;
                if (String.IsNullOrEmpty(adb) || !File.Exists(adb) || String.IsNullOrEmpty(instance) || String.IsNullOrEmpty(created)) return;
                manager = Path.Combine(Path.GetDirectoryName(adb), "MuMuManager.exe");
                if (!File.Exists(manager)) return;
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEvent))
                using (var context = new ApplicationContext())
                using (var dispatcher = new Control())
                {
                    IntPtr dispatcherHandle = dispatcher.Handle;
                    RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(stop, (s, expired) => {
                        stopping.Cancel();
                        try { dispatcher.BeginInvoke(new Action(context.ExitThread)); }
                        catch (InvalidOperationException) { }
                    }, null, Timeout.Infinite, true);
                    callback = OnWindowCreated;
                    IntPtr hook = SetWinEventHook(0x8000, 0x8000, IntPtr.Zero, callback, 0, 0, 2);
                    if (hook == IntPtr.Zero) { wait.Unregister(null); return; }
                    foreach (Process process in Process.GetProcessesByName("MuMuNxDevice"))
                        using (process) Check(process.Id);
                    try { Application.Run(context); }
                    finally { stopping.Cancel(); UnhookWinEvent(hook); wait.Unregister(null); }
                }
            }
        }

        static void OnWindowCreated(IntPtr hook, uint evt, IntPtr window, int objectId, int child, uint thread, uint time)
        {
            if (window == IntPtr.Zero || objectId != 0 || child != 0 || stopping.IsCancellationRequested) return;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            Check((int)pid);
        }

        static void Check(int pid)
        {
            try { using (Process process = Process.GetProcessById(pid)) if (process.ProcessName != "MuMuNxDevice") return; }
            catch { return; }
            if (!seen.TryAdd(pid, 0)) return;
            Task.Run(() => ResumeAtStartup(pid));
        }

        static void ResumeAtStartup(int pid)
        {
            var deadline = Stopwatch.StartNew();
            bool matched = false;
            long lastConnect = -3000;
            int target;
            if (!Int32.TryParse(instance, out target)) return;
            string serial = "127.0.0.1:" + (16384 + target * 32);
            while (!stopping.IsCancellationRequested && deadline.ElapsedMilliseconds < 120000)
            {
                try
                {
                    using (Process process = Process.GetProcessById(pid)) if (process.HasExited) return;
                    if (!matched)
                    {
                        int match = MatchesInstance(pid);
                        if (match < 0) return;
                        matched = match > 0;
                    }
                    bool online = matched && Execute(adb, "-s " + serial + " get-state").Trim() == "device";
                    if (matched && !online && deadline.ElapsedMilliseconds - lastConnect >= 3000)
                    {
                        Execute(adb, "connect " + serial);
                        lastConnect = deadline.ElapsedMilliseconds;
                    }
                    if (online)
                    {
                        var policy = new GuestAdPolicy(manager, target);
                        policy.Apply();
                        // netd may still initialize its rules during boot. Recheck after Android is ready.
                        CompleteStartup(policy, serial, pid, deadline);
                        return;
                    }
                }
                catch { /* The management channel may precede mounted Android data. Retry only during startup. */ }
                stopping.Token.WaitHandle.WaitOne(750);
            }
        }

        static void CompleteStartup(GuestAdPolicy policy, string serial, int pid, Stopwatch deadline)
        {
            while (!stopping.IsCancellationRequested && deadline.ElapsedMilliseconds < 120000)
            {
                try { using (Process process = Process.GetProcessById(pid)) if (process.HasExited) return; }
                catch { return; }
                if (Execute(adb, "-s " + serial + " shell getprop sys.boot_completed").Trim() == "1") break;
                stopping.Token.WaitHandle.WaitOne(750);
            }
            if (stopping.IsCancellationRequested || deadline.ElapsedMilliseconds >= 120000) return;
            if (!policy.Active()) policy.Apply();
            // A legacy ROM can provision the store during its first startup.
            new AdComponentControl(adb, serial, stateDirectory).Apply(new QuietProgress(), false);
        }

        sealed class QuietProgress : IProgress<string> { public void Report(string message) { } }

        static int MatchesInstance(int pid)
        {
            try
            {
                var record = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Execute(manager, "info -v " + instance));
                if (record != null)
                {
                    object process, timestamp;
                    if (record.TryGetValue("created_timestamp", out timestamp) && Convert.ToString(timestamp, System.Globalization.CultureInfo.InvariantCulture) != created) return -1;
                    if (record.TryGetValue("pid", out process))
                    {
                        int running = Convert.ToInt32(process);
                        if (running == pid) return 1;
                        if (running > 0)
                        {
                            try { using (Process other = Process.GetProcessById(running)) if (!other.HasExited) return -1; }
                            catch (ArgumentException) { }
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        static string Execute(string file, string arguments)
        {
            NativeCommand.Result result = NativeCommand.Run(file, arguments);
            return result.Code == 0 ? result.Output : "";
        }
    }
}

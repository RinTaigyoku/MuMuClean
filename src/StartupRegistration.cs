using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace MuMuClean
{
    static class StartupRegistration
    {
        const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string RunName = "MuMuCleanStartup";
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MuMuAdCleaner", "MuMuStartup.exe"); } }
        static string Command { get { return "\"" + FilePath + "\""; } }

        internal static bool Enabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && String.Equals(key.GetValue(RunName) as string, Command, StringComparison.OrdinalIgnoreCase) && File.Exists(FilePath);
        }

        internal static void Enable(string adb, string serial = "127.0.0.1:16384")
        {
            var match = System.Text.RegularExpressions.Regex.Match(serial, @"^127\.0\.0\.1:(\d+)$");
            int port;
            if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out port) || port < 16384 || port > 65535 || (port - 16384) % 32 != 0)
                throw new InvalidOperationException("未确认实例身份，未注册启动助手。");
            int instance = (port - 16384) / 32;
            string manager = Path.Combine(Path.GetDirectoryName(adb), "MuMuManager.exe");
            var record = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(Cleaner.Run(manager, "info -v " + instance).Output);
            if (record == null || !record.ContainsKey("created_timestamp") || !record.ContainsKey("adb_port") || Convert.ToInt32(record["adb_port"]) != port)
                throw new InvalidOperationException("无法确认实例创建记录，未注册启动助手。");
            string created = Convert.ToString(record["created_timestamp"], System.Globalization.CultureInfo.InvariantCulture);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                string previous = key.GetValue(RunName) as string;
                if (previous != null && !String.Equals(previous, Command, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("启动项名称被其他程序占用，未覆盖原启动项。");
                Stop();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("MuMuClean.Startup.exe"))
                {
                    if (input == null) throw new InvalidOperationException("程序没有包含启动助手。");
                    using (FileStream output = File.Create(FilePath)) input.CopyTo(output);
                }
                using (RegistryKey settings = Registry.CurrentUser.CreateSubKey("Software\\MuMuClean"))
                {
                    settings.SetValue("StartupAdb", adb);
                    settings.SetValue("StartupInstance", instance.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    settings.SetValue("StartupCreated", created);
                }
                key.SetValue(RunName, Command);
            }
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = false, CreateNoWindow = true });
        }

        internal static void Remove()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (key != null && String.Equals(key.GetValue(RunName) as string, Command, StringComparison.OrdinalIgnoreCase)) key.DeleteValue(RunName, false);
            Stop();
            if (File.Exists(FilePath)) File.Delete(FilePath);
            using (RegistryKey settings = Registry.CurrentUser.OpenSubKey("Software\\MuMuClean", true))
                if (settings != null) { settings.DeleteValue("StartupAdb", false); settings.DeleteValue("StartupInstance", false); settings.DeleteValue("StartupCreated", false); }
        }

        static void Stop()
        {
            try { using (EventWaitHandle stop = EventWaitHandle.OpenExisting("Local\\MuMuClean.StartupListener.Stop")) stop.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            foreach (Process process in Process.GetProcessesByName("MuMuStartup"))
                using (process)
                {
                    if (process.WaitForExit(2500)) continue;
                    try
                    {
                        if (String.Equals(process.MainModule.FileName, FilePath, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("启动助手仍在退出，未覆盖或删除运行中的文件。请稍后重试。");
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        if (!process.HasExited) throw new InvalidOperationException("无法确认启动助手是否已经退出，已保留文件。请稍后重试。");
                    }
                }
        }
    }
}

using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

namespace MuMuClean
{
    static class NativeCommand
    {
        internal sealed class Result { public int Code; public string Output; }
        internal static Result Run(string file, string arguments)
        {
            Mutex gate = null;
            bool held = false;
            bool legacy = false;
            Stopwatch duration = Stopwatch.StartNew();
            try
            {
                if (String.Equals(Path.GetFileName(file), "MuMuManager.exe", StringComparison.OrdinalIgnoreCase))
                {
                    gate = new Mutex(false, "Local\\MuMuClean.MuMuManager");
                    try { held = gate.WaitOne(20000); }
                    catch (AbandonedMutexException) { held = true; }
                    if (!held) return new Result { Code = -1, Output = "MuMu 管理通道正在处理其他操作。" };
                    legacy = (FileVersionInfo.GetVersionInfo(file).FileVersion ?? "").StartsWith("5.30.2", StringComparison.Ordinal);
                    duration.Restart();
                }
                using (var process = Process.Start(new ProcessStartInfo(file, arguments) {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                }))
                {
                    Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(15000)) { process.Kill(); return new Result { Code = -1, Output = "命令超时，已结束本次命令进程。" }; }
                    if (!Task.WaitAll(new Task[] { output, error }, 2000))
                        return new Result { Code = -1, Output = "命令已退出，但输出通道没有及时关闭。" };
                    return new Result { Code = process.ExitCode, Output = output.Result + error.Result };
                }
            }
            catch (Exception error) { return new Result { Code = -1, Output = error.Message }; }
            finally
            {
                if (held)
                {
                    // The legacy RPC bridge can still be clearing its result after the CLI exits.
                    if (legacy && duration.ElapsedMilliseconds < 200) Thread.Sleep((int)(200 - duration.ElapsedMilliseconds));
                    gate.ReleaseMutex();
                }
                if (gate != null) gate.Dispose();
            }
        }
    }
}

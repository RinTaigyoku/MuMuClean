using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace MuMuClean
{
    sealed class EmulatorSession
    {
        internal readonly string Root, Adb, Manager, Index, Serial, Created, AndroidId;
        internal EmulatorSession(string root, int index, bool allowOffline = false)
        {
            if (String.IsNullOrEmpty(root) || index < 0 || index > 1535) throw new InvalidOperationException("模拟器实例无效。");
            Root = Path.GetFullPath(root).TrimEnd('\\');
            Adb = Cleaner.FindAdb(Root); Manager = Path.Combine(Root, "nx_main", "MuMuManager.exe");
            if (Adb == null || !File.Exists(Manager)) throw new InvalidOperationException("缺少 MuMu 管理工具。");
            Index = index.ToString(CultureInfo.InvariantCulture); Serial = "127.0.0.1:" + (16384 + index * 32);
            Dictionary<string, object> record = Info();
            object port, created;
            bool knownPort = record.TryGetValue("adb_port", out port) && Convert.ToInt32(port) == 16384 + index * 32;
            if ((!allowOffline && !knownPort) || !record.TryGetValue("created_timestamp", out created))
                throw new InvalidOperationException("请先启动要配置的 Android 实例。");
            Created = Convert.ToString(created, CultureInfo.InvariantCulture);
            if (!allowOffline)
            {
                AndroidId = AdbShell("settings get secure android_id").Trim();
                if (!Regex.IsMatch(AndroidId, "^[a-fA-F0-9]{8,32}$")) throw new InvalidOperationException("Android 尚未就绪，未修改设置。");
            }
        }
        internal string Run(string command)
        {
            NativeCommand.Result result = NativeCommand.Run(Manager, command);
            if (result.Code != 0) throw new InvalidOperationException(result.Output);
            return result.Output;
        }
        Dictionary<string, object> Info()
        {
            string output = Run("info -v " + Index).Trim();
            if (!output.StartsWith("{", StringComparison.Ordinal)) throw new InvalidOperationException("MuMu 管理接口暂未就绪，请保持管理器打开后重试。");
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(output);
        }
        internal void Verify()
        {
            object created;
            if (!Info().TryGetValue("created_timestamp", out created) || Convert.ToString(created, CultureInfo.InvariantCulture) != Created)
                throw new InvalidOperationException("实例创建标记已变化，已停止修改。");
        }
        internal string Shell(string command)
        {
            string marker = "MUMUCLEAN_RC_" + Guid.NewGuid().ToString("N");
            string wrapped = "( " + command + " ) 2>&1; echo " + marker + ":$?";
            string result = Run("sh -v " + Index + " --cmd \"" + wrapped.Replace("\"", "\\\"") + "\"");
            Match end = Regex.Match(result.TrimEnd(), @"(?:^|\r?\n)" + marker + @":(\d+)$");
            if (!end.Success) throw new InvalidOperationException("管理命令没有返回有效完成标记。");
            string body = result.Substring(0, end.Index).TrimEnd();
            if (end.Groups[1].Value != "0") throw new InvalidOperationException("Android 操作未完成：" + body.Substring(0, Math.Min(body.Length, 360)));
            return body;
        }
        internal string AdbRun(string command)
        {
            NativeCommand.Result result = NativeCommand.Run(Adb, "-s " + Serial + " " + command);
            if (result.Code != 0) throw new InvalidOperationException("ADB 操作失败：" + result.Output);
            return result.Output;
        }
        internal string AdbShell(string command) { return AdbRun("shell " + command); }
        internal string Setting(string key)
        {
            var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Run("setting -v " + Index + " --key " + key));
            if (values == null || !values.ContainsKey(key)) throw new InvalidOperationException("没有读到原始模拟器设置。");
            string value = Convert.ToString(values[key], CultureInfo.InvariantCulture).ToLowerInvariant();
            if (value != "true" && value != "false") throw new InvalidOperationException("设置值超出已支持范围。");
            return value;
        }
        internal void Set(string key, string value)
        {
            if ((key != "root_permission" && key != "system_disk_readonly") || (value != "true" && value != "false")) throw new InvalidOperationException("设置项无效。");
            Run("setting -v " + Index + " --key " + key + " --value " + value);
            if (Setting(key) != value) throw new InvalidOperationException("模拟器设置未达到目标值。");
        }
        internal void Restart(IProgress<string> progress)
        {
            Verify(); progress.Report("正在重启当前 Android 实例…");
            string previousBoot = NativeCommand.Run(Adb, "-s " + Serial + " shell cat /proc/sys/kernel/random/boot_id").Output.Trim();
            Run("control -v " + Index + " restart");
            for (int attempt = 0; attempt < 90; attempt++)
            {
                NativeCommand.Run(Adb, "connect " + Serial);
                NativeCommand.Result boot = NativeCommand.Run(Adb, "-s " + Serial + " shell getprop sys.boot_completed");
                if (boot.Code == 0 && boot.Output.Trim() == "1")
                {
                    string currentBoot = NativeCommand.Run(Adb, "-s " + Serial + " shell cat /proc/sys/kernel/random/boot_id").Output.Trim();
                    if (Regex.IsMatch(currentBoot, "^[a-fA-F0-9-]{36}$") && currentBoot != previousBoot) { Verify(); return; }
                }
                Thread.Sleep(1000);
            }
            throw new InvalidOperationException("Android 尚未完成启动，恢复记录已保留。");
        }
        internal void Push(string local, string guest)
        {
            string result = AdbRun("push \"" + local + "\" " + guest);
            if (result.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("文件传输未完成。");
        }
    }
}

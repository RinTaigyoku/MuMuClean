using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MuMuClean
{
    // Independent opt-in switch. Keep original bytes for exact rollback.
    sealed class UpdateControl
    {
        readonly string root, version, executable, backup, record, operationKey;
        internal UpdateControl(string installRoot)
        {
            if (String.IsNullOrEmpty(installRoot)) throw new InvalidOperationException("未找到 MuMu 安装目录。");
            root = Path.GetFullPath(installRoot).TrimEnd('\\'); version = Cleaner.GetVersion(root);
            executable = Path.Combine(root, "nx_main", "MuMuNxUpdater.exe");
            string key = LocalRecovery.Key(root.ToLowerInvariant() + "|" + version);
            operationKey = key;
            backup = executable + ".MuMuClean-" + key + ".disabled";
            record = Path.Combine(Cleaner.CommonStateDir, "updates", key + ".json");
        }
        Dictionary<string, string> Read()
        {
            var state = LocalRecovery.Read(record);
            if (!state.ContainsKey("Root") || !String.Equals(state["Root"], root, StringComparison.OrdinalIgnoreCase) || state["Version"] != version)
                throw new InvalidOperationException("更新控制恢复点与当前安装版本不一致。");
            return state;
        }
        internal string Status()
        {
            if (!File.Exists(record)) return "未关闭 MuMu 更新功能。";
            var state = Read();
            if (!File.Exists(executable) && File.Exists(backup) && LocalRecovery.FileHash(backup) == state["Hash"])
                return "MuMu 更新器已停用；重开管理器后清除本次会话缓存的更新提示。";
            return "更新器状态发生变化，恢复点仍然保留。";
        }
        internal string Disable(IProgress<string> progress)
        {
            return Locked(() => DisableCore(progress));
        }
        string DisableCore(IProgress<string> progress)
        {
            if (File.Exists(record) && !File.Exists(executable)) return Status();
            if (!File.Exists(executable)) throw new InvalidOperationException("当前版本没有找到独立更新器，未修改其他程序。");
            EnsureIdle();
            Dictionary<string, string> state;
            if (File.Exists(record))
            {
                state = Read();
                if (LocalRecovery.FileHash(executable) != state["Hash"]) throw new InvalidOperationException("厂商已替换更新器，未覆盖旧版本恢复点。请先恢复或重新安装对应版本。");
            }
            else
            {
                if (File.Exists(backup)) throw new InvalidOperationException("备份名称已被占用，未覆盖未知文件。");
                state = new Dictionary<string, string> {
                    {"Root",root},{"Version",version},{"Hash",LocalRecovery.FileHash(executable)},
                    {"Attributes",((int)File.GetAttributes(executable)).ToString(CultureInfo.InvariantCulture)},{"Phase","Prepared"}
                };
                LocalRecovery.Save(record, state);
            }
            if (File.Exists(backup))
            {
                if (LocalRecovery.FileHash(backup) != state["Hash"]) throw new InvalidOperationException("更新器备份校验失败。");
                File.Delete(executable); // Only a byte-identical replacement of the already backed-up updater.
            }
            else File.Move(executable, backup);
            if (File.Exists(executable) || LocalRecovery.FileHash(backup) != state["Hash"]) throw new InvalidOperationException("更新控制尚未完整生效，备份已保留。");
            state["Phase"] = "Disabled"; LocalRecovery.Save(record, state);
            progress.Report("已停用独立 MuMu 更新器，原文件按字节保留，没有修改主程序或用户 App 的更新网络。");
            return "更新器已关闭。请重开 MuMu 管理器，使当前会话里缓存的更新提示消失；手动更新前先点恢复。";
        }
        internal string Restore(IProgress<string> progress)
        {
            return Locked(() => RestoreCore(progress));
        }
        string RestoreCore(IProgress<string> progress)
        {
            if (!File.Exists(record)) return "没有本工具的更新控制恢复点。";
            var state = Read(); EnsureIdle();
            if (File.Exists(executable) && LocalRecovery.FileHash(executable) != state["Hash"])
                throw new InvalidOperationException("检测到不同版本的更新器，未覆盖厂商新文件；旧备份仍保留。");
            if (File.Exists(backup))
            {
                if (LocalRecovery.FileHash(backup) != state["Hash"]) throw new InvalidOperationException("更新器备份校验失败，未恢复未知内容。");
                if (File.Exists(executable)) File.Delete(backup); else File.Move(backup, executable);
            }
            if (!File.Exists(executable) || LocalRecovery.FileHash(executable) != state["Hash"]) throw new InvalidOperationException("更新器未完整恢复。");
            int attributes;
            if (!Int32.TryParse(state["Attributes"], out attributes)) throw new InvalidOperationException("原始文件属性无效，恢复点已保留。");
            File.SetAttributes(executable, (FileAttributes)attributes);
            File.Delete(record);
            progress.Report("已恢复原始更新器文件和属性。");
            return "MuMu 更新功能已恢复。";
        }
        string Locked(Func<string> operation)
        {
            using (var mutex = new Mutex(false, "Local\\MuMuClean.Update." + operationKey))
            {
                bool held;
                try { held = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) throw new InvalidOperationException("另一个更新控制操作正在执行。");
                try { return operation(); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        void EnsureIdle()
        {
            foreach (Process process in Process.GetProcessesByName("MuMuNxUpdater"))
                using (process)
                {
                    try
                    {
                        if (!process.HasExited && String.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("更新器正在运行，请退出更新窗口或等待本次检查结束后再操作。");
                    }
                    catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("无法确认更新器状态，暂未修改文件。"); }
                }
        }
    }
}

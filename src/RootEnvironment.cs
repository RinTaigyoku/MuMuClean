using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Net;

namespace MuMuClean
{
    // Optional Android 12 System Mode profile. It is never called by ad removal.
    sealed class RootEnvironment
    {
        const string MagiskHash = "f44cfa11c5de9a0dabf3dc8a516438ac9eec762a3178ca4a5f4fd772dcb516b7";
        const string VectorHash = "d5e39669c02c2c699ab948eb8f3639b348eefb7749553224a9c62fa4a2f2dc18";
        const string MagiskUrl = "https://github.com/Jordan231111/KitsuneMagisk/releases/download/kitsune-canary-5869bd7f9612e1f07e7ec07420afb8f11b868cf6/app-release.apk";
        const string VectorUrl = "https://github.com/JingMatrix/Vector/releases/download/v2.0/Vector-v2.0-3021-Release.zip";
        const string ManagerPackage = "io.github.jordan231111.magisk";
        const string VectorPackage = "org.lsposed.manager";
        const string GuestPayload = "/data/local/tmp/mumuclean-root-payload";
        readonly EmulatorSession session;
        readonly string recordPath, work;

        internal RootEnvironment(string root, int index, bool forRecovery = false)
        {
            session = new EmulatorSession(root, index, forRecovery);
            string sdk = forRecovery ? "32" : session.AdbShell("getprop ro.build.version.sdk").Trim();
            if (!Cleaner.GetVersion(root).StartsWith("5.30.2", StringComparison.Ordinal) || (sdk != "31" && sdk != "32"))
                throw new InvalidOperationException("一键 Root 环境仅适用于 MuMu 国际版 5.30.2 / Android 12。去广告功能不受此限制。");
            string directory = Path.Combine(Cleaner.CommonStateDir, "root", LocalRecovery.Key(session.Root.ToLowerInvariant() + "|" + session.Index + "|" + session.Created));
            recordPath = Path.Combine(directory, "recovery.json");
            work = Path.Combine(directory, "payload");
        }

        Dictionary<string, string> Record()
        {
            var state = LocalRecovery.Read(recordPath);
            if (!state.ContainsKey("Created") || state["Created"] != session.Created || !state.ContainsKey("Root") || !String.Equals(state["Root"], session.Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Root 恢复记录属于其他实例，未修改当前环境。");
            return state;
        }
        internal string Status()
        {
            if (!File.Exists(recordPath)) return "尚未由本工具配置 Root 环境。";
            var record = Record();
            if (record["Phase"] == "RolledBack") return "Root 环境已回退，保留原有模块配置数据。";
            return record["Phase"] == "Ready" ? "已配置 KitsuneMagisk + Zygisk + Vector 2.0。" : "存在未完成的 Root 恢复点，可点击回退。";
        }
        internal string Configure(IProgress<string> progress)
        {
            using (var mutex = OperationLock())
            {
                try
                {
                    if (!mutex.WaitOne(0)) throw new InvalidOperationException("另一个 Root 操作正在执行。");
                    try { return ConfigureCore(progress); }
                    finally { mutex.ReleaseMutex(); }
                }
                catch (AbandonedMutexException) { mutex.ReleaseMutex(); throw new InvalidOperationException("上一次 Root 操作被中断，请先回退已有恢复点。"); }
            }
        }
        Mutex OperationLock() { return new Mutex(false, "Local\\MuMuClean.Root." + LocalRecovery.Key(session.Root + "|" + session.Index)); }

        string ConfigureCore(IProgress<string> progress)
        {
            session.Verify();
            bool ownedPrevious = false;
            if (File.Exists(recordPath))
            {
                var existing = Record();
                if (existing["Phase"] == "Ready" && Healthy()) return "Root 环境已就绪，无需重复安装。";
                if (existing["Phase"] == "RolledBack") ownedPrevious = true;
                else throw new InvalidOperationException("已有 Root 恢复点，请先回退，再重新配置。");
            }
            string test = "[ -e /data/adb/magisk ] || [ -e /data/adb/ksu ] || [ -e /data/adb/ap ]";
            if (!ownedPrevious) test += " || [ -e /data/adb/modules/zygisk_vector ] || [ -e /data/adb/lspd ]";
            string present = session.Shell("if " + test + "; then echo EXISTS; else echo CLEAR; fi").Trim();
            if (present != "CLEAR" || Installed(ManagerPackage) || Installed(VectorPackage))
                throw new InvalidOperationException("检测到已有第三方 Root 或 Vector 配置，未覆盖现有环境。");
            progress.Report("正在校验 Root 配置包；首次使用需从上游下载，失败时不会修改模拟器。");
            PreparePayload();
            var state = new Dictionary<string, string> {
                {"Root",session.Root},{"Created",session.Created},{"AndroidId",session.AndroidId},
                {"RootBefore",session.Setting("root_permission")},{"ReadonlyBefore",session.Setting("system_disk_readonly")},
                {"MagiskSha256",MagiskHash},{"VectorSha256",VectorHash},{"Phase","Prepared"}
            };
            LocalRecovery.Save(recordPath, state);
            try
            {
                progress.Report("已保存原始 root 开关和系统盘设置，开始配置当前实例。");
                session.Set("root_permission", "true"); session.Set("system_disk_readonly", "false");
                session.Restart(progress);
                DeployPayload();
                progress.Report("正在安装可回退的 System Mode Root 环境…");
                InstallAction("install");
                state["Phase"] = "SystemInstalled"; LocalRecovery.Save(recordPath, state);
                InstallApk(Path.Combine(work, "manager.apk"));
                session.Restart(progress);
                string path = session.Shell("/data/adb/magisk/magisk --path").Trim();
                if (!path.StartsWith("/", StringComparison.Ordinal)) throw new InvalidOperationException("Root 服务尚未就绪。");
                session.Shell("/data/adb/magisk/magisk --sqlite \"INSERT OR REPLACE INTO settings (key,value) VALUES('zygisk',1);\"");
                session.Push(Path.Combine(work, "vector.zip"), "/data/local/tmp/mumuclean-vector.zip");
                session.Shell("/data/adb/magisk/magisk --install-module /data/local/tmp/mumuclean-vector.zip > /data/local/tmp/mumuclean-vector-install.log 2>&1");
                InstallApk(Path.Combine(work, "vector-manager.apk"));
                progress.Report("已安装 Vector 2.0；由新框架接管 Root，避免与 MuMu 自带 Root 重复运行。");
                session.Set("root_permission", "false");
                session.Restart(progress);
                bool ready = false;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (Healthy()) { ready = true; break; }
                    Thread.Sleep(1000);
                }
                if (!ready) throw new InvalidOperationException("框架启动核对未通过，恢复点已保留。");
                state["Phase"] = "Ready"; LocalRecovery.Save(recordPath, state);
                progress.Report("Root 和 Vector 已启动。系统盘保持可写，这是该版本 System Mode 的运行条件。");
                return "Root 环境配置完成。没有预装其他 Hook 模块，也没有给普通用户 App 批量授予 Root。";
            }
            catch
            {
                state["Phase"] = "NeedsRecovery"; LocalRecovery.Save(recordPath, state);
                progress.Report("配置未完成，恢复点已保留。请点击“回退 Root 环境”。");
                throw;
            }
        }

        internal string Rollback(IProgress<string> progress)
        {
            using (var mutex = OperationLock())
            {
                bool held;
                try { held = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) throw new InvalidOperationException("另一个 Root 操作正在执行。");
                try
                {
                    if (!File.Exists(recordPath)) return "没有本工具建立的 Root 恢复点，未改动现有环境。";
                    session.Verify(); var state = Record();
                    if (state["Phase"] == "RolledBack") return "Root 环境已经回退，未修改现有环境。";
                    if (state["RootBefore"] != "true" && state["RootBefore"] != "false") throw new InvalidOperationException("原始 Root 状态无效。");
                    if (state["ReadonlyBefore"] != "true" && state["ReadonlyBefore"] != "false") throw new InvalidOperationException("原始系统盘状态无效。");
                    PreparePayload();
                    session.Run("control -v " + session.Index + " launch");
                    if (NativeCommand.Run(session.Adb, "-s " + session.Serial + " get-state").Output.Trim() == "device") DeployPayload();
                    else if (session.Shell("if [ -x " + GuestPayload + "/busybox ]; then echo YES; else echo NO; fi").Trim() != "YES")
                        throw new InvalidOperationException("Android 数据分区尚未可用，恢复记录已保留。");
                    string installed = session.Shell("if [ -e /system/etc/init/magisk/install-manifest.json ]; then echo YES; else echo NO; fi").Trim();
                    if (installed == "YES")
                    {
                        progress.Report("正在按安装事务还原系统文件…");
                        InstallAction("uninstall");
                    }
                    else if (session.Shell("if [ -e /data/adb/kitsune ]; then echo YES; else echo NO; fi").Trim() == "YES")
                        InstallAction("recover");
                    session.Shell("if [ -f /data/adb/modules/zygisk_vector/module.prop ] && grep -qx 'versionCode=3021' /data/adb/modules/zygisk_vector/module.prop; then "
                        + GuestPayload + "/busybox rm -rf /data/adb/modules/zygisk_vector; fi");
                    session.Set("root_permission", state["RootBefore"]);
                    session.Set("system_disk_readonly", state["ReadonlyBefore"]);
                    session.Restart(progress);
                    // These packages were confirmed absent before this tool's installation.
                    if (Installed(VectorPackage)) session.AdbRun("uninstall " + VectorPackage);
                    if (Installed(ManagerPackage)) session.AdbRun("uninstall " + ManagerPackage);
                    if (session.Shell("if [ -e /system/etc/init/magisk.rc ]; then echo EXISTS; else echo CLEAR; fi").Trim() != "CLEAR")
                        throw new InvalidOperationException("Root 启动文件尚未完全恢复，恢复点已保留。");
                    state["Phase"] = "RolledBack"; LocalRecovery.Save(recordPath, state);
                    progress.Report("已恢复配置前的 MuMu Root 开关和系统盘状态。");
                    return "Root 环境已回退。去广告和更新控制保持各自设置。";
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        bool Installed(string package)
        {
            NativeCommand.Result result = NativeCommand.Run(session.Adb, "-s " + session.Serial + " shell pm path " + package);
            return result.Code == 0 && result.Output.TrimStart().StartsWith("package:", StringComparison.Ordinal);
        }
        void InstallApk(string path)
        {
            string output = session.AdbRun("install --no-incremental -r \"" + path + "\"");
            if (!output.Contains("Success")) throw new InvalidOperationException("Root 管理工具 APK 安装未完成。");
        }
        bool Healthy()
        {
            string processes = session.AdbShell("ps -A");
            return System.Text.RegularExpressions.Regex.IsMatch(processes, @"(?m)\bmagiskd\s*$")
                && System.Text.RegularExpressions.Regex.IsMatch(processes, @"(?m)\blspd\s*$")
                && session.Shell("cat /data/adb/modules/zygisk_vector/module.prop").Contains("versionCode=3021");
        }
        void InstallAction(string action)
        {
            if (action != "install" && action != "uninstall" && action != "recover") throw new InvalidOperationException("操作无效。");
            string command = "export ASH_STANDALONE=1; " + GuestPayload + "/busybox unshare -m " + GuestPayload + "/busybox sh "
                + GuestPayload + "/kitsune_system_install.sh " + action + " " + GuestPayload;
            if (action == "install") command += " /data/local/tmp/mumuclean-root-manager.apk";
            session.Shell(command + " > /data/local/tmp/mumuclean-root-operation.log 2>&1");
        }
        void DeployPayload()
        {
            session.Push(Path.Combine(work, "payload"), GuestPayload);
            session.Push(Path.Combine(work, "manager.apk"), "/data/local/tmp/mumuclean-root-manager.apk");
            session.Shell("chmod 755 " + GuestPayload + "/*");
        }
        void PreparePayload()
        {
            Directory.CreateDirectory(work);
            DownloadPackage(MagiskUrl, Path.Combine(work, "manager.apk"), MagiskHash);
            DownloadPackage(VectorUrl, Path.Combine(work, "vector.zip"), VectorHash);
            string payload = Path.Combine(work, "payload"); Directory.CreateDirectory(payload);
            using (ZipArchive archive = ZipFile.OpenRead(Path.Combine(work, "manager.apk")))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = null;
                    if (entry.FullName.StartsWith("assets/", StringComparison.Ordinal) && entry.FullName.Substring(7).IndexOf('/') < 0 && (entry.Name.EndsWith(".sh", StringComparison.Ordinal) || entry.Name == "stub.apk")) name = entry.Name;
                    foreach (string binary in new[] { "busybox", "magisk", "magiskboot", "magiskinit", "magiskpolicy" })
                        if (entry.FullName == "lib/x86_64/lib" + binary + ".so") name = binary;
                    if (name == null) continue;
                    using (Stream input = entry.Open()) using (FileStream output = File.Create(Path.Combine(payload, name))) input.CopyTo(output);
                }
            }
            using (ZipArchive archive = ZipFile.OpenRead(Path.Combine(work, "vector.zip")))
            {
                ZipArchiveEntry manager = archive.GetEntry("manager.apk");
                if (manager == null) throw new InvalidOperationException("Vector 包不含管理界面。");
                using (Stream input = manager.Open()) using (FileStream output = File.Create(Path.Combine(work, "vector-manager.apk"))) input.CopyTo(output);
            }
        }
        static void DownloadPackage(string url, string path, string expected)
        {
            if (File.Exists(path) && LocalRecovery.FileHash(path) == expected) return;
            string temporary = path + ".download";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 120000; request.ReadWriteTimeout = 30000;
                request.UserAgent = "MuMuClean/1.5";
                using (var response = request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (var output = File.Create(temporary))
                {
                    byte[] buffer = new byte[65536]; int count; long total = 0;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > 32 * 1024 * 1024) throw new InvalidOperationException("Root 配置包大小异常。");
                        output.Write(buffer, 0, count);
                    }
                }
                if (LocalRecovery.FileHash(temporary) != expected) throw new InvalidOperationException("Root 配置包校验失败，未修改模拟器。");
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

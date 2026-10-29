using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("MuMu 清爽助手")]
[assembly: System.Reflection.AssemblyDescription("MuMuPlayer 国际版可逆广告清理工具")]
[assembly: System.Reflection.AssemblyCompany("Local Utility")]
[assembly: System.Reflection.AssemblyProduct("MuMu 清爽助手")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.0.1")]

namespace MuMuClean
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                Console.WriteLine(Cleaner.SelfTest());
                return;
            }
            if (args.Length > 0 && args[0] == "--maa-check")
            {
                Console.WriteLine(Cleaner.MaaCheck());
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    sealed class MainForm : Form
    {
        readonly Cleaner cleaner = new Cleaner();
        readonly Label status = new Label();
        readonly Label detail = new Label();
        readonly RichTextBox log = new RichTextBox();
        readonly Button apply = new Button();
        readonly Button restore = new Button();
        readonly Button scan = new Button();
        readonly Button optimize = new Button();
        readonly Button undoOptimize = new Button();
        readonly Button closeMuMu = new Button();

        public MainForm()
        {
            Text = "MuMu 清爽助手";
            ClientSize = new Size(820, 570);
            MinimumSize = new Size(760, 540);
            BackColor = Color.FromArgb(15, 19, 27);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { Icon = SystemIcons.Application; }

            var top = new Panel { Dock = DockStyle.Top, Height = 122, BackColor = Color.FromArgb(20, 27, 39) };
            Controls.Add(top);
            var mark = new Label { Text = "M", AutoSize = false, Size = new Size(56, 56), Location = new Point(30, 28), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(88, 101, 242), ForeColor = Color.White, Font = new Font("Segoe UI", 22F, FontStyle.Bold) };
            top.Controls.Add(mark);
            top.Controls.Add(new Label { Text = "MuMu 清爽助手", AutoSize = true, Location = new Point(104, 25), Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold), ForeColor = Color.White });
            top.Controls.Add(new Label { Text = "可逆 · MAA 安全保护 · 不干扰更新器", AutoSize = true, Location = new Point(106, 70), ForeColor = Color.FromArgb(154, 164, 181) });

            status.Text = "正在检测…";
            status.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            status.AutoSize = true;
            status.Location = new Point(30, 148);
            Controls.Add(status);
            detail.AutoSize = false;
            detail.Size = new Size(750, 46);
            detail.Location = new Point(32, 187);
            detail.ForeColor = Color.FromArgb(174, 183, 199);
            Controls.Add(detail);

            StyleButton(apply, "全面去除广告", new Point(30, 245), Color.FromArgb(88, 101, 242));
            StyleButton(restore, "全面恢复", new Point(240, 245), Color.FromArgb(48, 58, 73));
            StyleButton(scan, "重新检测", new Point(450, 245), Color.FromArgb(48, 58, 73));
            Controls.Add(apply); Controls.Add(restore); Controls.Add(scan);
            StyleButton(optimize, "省占用（30 FPS）", new Point(30, 305), Color.FromArgb(48, 58, 73));
            StyleButton(undoOptimize, "恢复原始帧率", new Point(240, 305), Color.FromArgb(48, 58, 73));
            Controls.Add(optimize); Controls.Add(undoOptimize);
            StyleButton(closeMuMu, "按顺序退出 MuMu", new Point(450, 305), Color.FromArgb(48, 58, 73));
            Controls.Add(closeMuMu);
            closeMuMu.Click += async (s, e) => {
                SetBusy(true);
                try { WriteLog(await Task.Run(() => cleaner.CloseInOrder())); }
                catch (Exception ex) { WriteLog("退出未完成：" + ex.Message); }
                finally { SetBusy(false); }
            };

            log.Location = new Point(30, 375);
            log.Size = new Size(760, 155);
            log.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom | AnchorStyles.Top;
            log.BackColor = Color.FromArgb(10, 14, 21);
            log.ForeColor = Color.FromArgb(202, 211, 225);
            log.BorderStyle = BorderStyle.None;
            log.ReadOnly = true;
            log.Font = new Font("Microsoft YaHei UI", 9F);
            Controls.Add(log);

            apply.Click += async (s, e) => await RunAction(true);
            restore.Click += async (s, e) => await RunAction(false);
            scan.Click += async (s, e) => await RefreshStatus();
            optimize.Click += async (s, e) => await RunOptimize(false);
            undoOptimize.Click += async (s, e) => await RunOptimize(true);
            Shown += async (s, e) => await RefreshStatus();
        }

        void StyleButton(Button b, string text, Point p, Color color)
        {
            b.Text = text; b.Location = p; b.Size = new Size(190, 52);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = color; b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand; b.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        }

        async Task RunAction(bool doApply)
        {
            SetBusy(true);
            log.Clear();
            WriteLog(doApply ? "开始应用清爽规则…" : "开始恢复 MuMu 原始状态…");
            try
            {
                var progress = new Progress<string>(WriteLog);
                string result = await Task.Run(() => doApply ? cleaner.Apply(progress) : cleaner.Restore(progress));
                WriteLog(result);
            }
            catch (Exception ex) { WriteLog("失败：" + ex.Message); }
            SetBusy(false);
            await RefreshStatus(false);
        }

        async Task RefreshStatus(bool clear = true)
        {
            SetBusy(true);
            if (clear) log.Clear();
            var info = await Task.Run(() => cleaner.Scan());
            status.Text = info.Applied ? "已开启清爽模式" : (info.Partial ? "还差一步即可完成" : (info.Found ? "已检测到 MuMuPlayer" : "未检测到 MuMuPlayer"));
            status.ForeColor = info.Applied ? Color.FromArgb(63, 214, 146) : (info.Found ? Color.FromArgb(255, 184, 93) : Color.FromArgb(255, 184, 93));
            detail.Text = info.Message;
            if (clear) WriteLog(info.Details);
            SetBusy(false);
        }

        void SetBusy(bool busy)
        {
            apply.Enabled = restore.Enabled = scan.Enabled = !busy;
            optimize.Enabled = undoOptimize.Enabled = !busy;
            closeMuMu.Enabled = !busy;
            UseWaitCursor = busy;
        }

        async Task RunOptimize(bool undo)
        {
            SetBusy(true);
            try { WriteLog(await Task.Run(() => cleaner.Optimize(undo))); }
            catch (Exception ex) { WriteLog("失败：" + ex.Message); }
            finally { SetBusy(false); }
        }

        void WriteLog(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(WriteLog), text); return; }
            log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
            log.ScrollToCaret();
        }
    }

    sealed class ScanInfo
    {
        public bool Found;
        public bool Applied;
        public bool Partial;
        public bool MaaRunning;
        public string Message;
        public string Details;
    }

    sealed class Cleaner
    {
        const string RulePrefix = "MuMuClean-";
        static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MuMuAdCleaner");
        static readonly string StateFile = Path.Combine(StateDir, "state.ini");
        static readonly string[] Components = {
            "com.mumu.store/.widget.appWidgetProvider.AdBannerWidgetProvider",
            "com.mumu.store/.widget.appWidgetProvider.DailyDiscoveryWidgetProvider",
            "com.mumu.store/.track.AdTrackingReceiver",
            "com.mumu.store/.track.TrackingReceiver"
        };

        public ScanInfo Scan()
        {
            string root = FindInstall();
            bool found = !String.IsNullOrEmpty(root) && Directory.Exists(root);
            bool fw = Run("netsh", "advfirewall firewall show rule name=\"" + RulePrefix + "MainCEF\"").Output.IndexOf(RulePrefix + "MainCEF", StringComparison.OrdinalIgnoreCase) >= 0;
            bool maa = IsMaaRunning();
            string adb = found ? FindAdb(root) : null;
            string serial = adb == null ? null : FindConnectedDevice(adb);
            bool android = serial != null && AreComponentsDisabled(adb, serial);
            bool overlay = serial != null && Run(adb, "-s " + serial + " shell cmd appops get com.mumu.store SYSTEM_ALERT_WINDOW").Output.Contains("SYSTEM_ALERT_WINDOW: ignore");
            bool startup = File.Exists(StartupIndex()) && File.ReadAllText(StartupIndex()).Replace(" ", "").Contains("\"info\":[]");
            return new ScanInfo {
                Found = found,
                Applied = fw && android && overlay && startup,
                Partial = fw || android,
                MaaRunning = maa,
                Message = found ? ("版本 " + GetVersion(root) + "    安装位置：" + root) : "请先安装或启动 MuMuPlayer 国际版。",
                Details = "Windows 广告联网规则：" + (fw ? "已应用" : "未应用") + "\nAndroid 广告组件：" + (serial == null ? "模拟器未连接" : (android ? "已关闭" : "仍在启用")) + "\n商店悬浮窗限制：" + overlay + "；启动缓存索引已清空：" + startup + "\nMAA 安全保护：" + (maa ? "已启用（检测到 MAA 正在运行）" : "待命") + "\n管理员权限：" + (IsAdmin() ? "正常" : "不足")
            };
        }

        public string Apply(IProgress<string> p)
        {
            RequireAdmin();
            string root = FindInstall();
            if (String.IsNullOrEmpty(root)) throw new InvalidOperationException("没有找到 E:\\MuMuPlayerGlobal 或注册表安装项。");
            Directory.CreateDirectory(StateDir);
            var state = File.Exists(StateFile) ? new List<string>(File.ReadAllLines(StateFile)) : new List<string>();
            if (!state.Any(x => x.StartsWith("InstallPath=", StringComparison.OrdinalIgnoreCase))) state.Add("InstallPath=" + root);
            if (!state.Any(x => x.StartsWith("AppliedAt=", StringComparison.OrdinalIgnoreCase))) state.Add("AppliedAt=" + DateTime.Now.ToString("o"));

            string adb = FindAdb(root);
            string serial = null;
            if (adb != null)
            {
                serial = ConnectAdb(adb, p);
                if (serial != null)
                {
                    if (!state.Any(x => x.StartsWith("StoreWasDisabled=", StringComparison.OrdinalIgnoreCase)))
                    {
                        bool wasDisabled = Run(adb, "-s " + serial + " shell pm list packages -d").Output.Contains("package:com.mumu.store");
                        state.Add("StoreWasDisabled=" + wasDisabled.ToString());
                    }
                }
            }
            File.WriteAllLines(StateFile, state.ToArray(), new UTF8Encoding(false));
            p.Report("已保存恢复点：" + StateFile);

            // 1.0 曾阻止整个主程序联网。发布版只处理广告 WebView，避免影响账号、工具箱和启动流程。
            Run("netsh", "advfirewall firewall delete rule name=\"" + RulePrefix + "Main\"");
            AddFirewall(root, "MainCEF", Path.Combine(root, "nx_main", "CefView", "CefViewWing.exe"), p);
            AddFirewall(root, "DeviceCEF", Path.Combine(root, "nx_device", "15.0", "shell", "CefView", "CefViewWing.exe"), p);
            ApplyStartupCache(p);

            if (adb != null && serial != null)
            {
                // 旧版曾停用整个 MuMu 商店包，导致 APK/XAPK 安装链路不可用。
                // 新版先恢复原本启用的商店包，再仅处理广告组件。
                // 清爽模式必须保留安装能力。无论执行前是什么状态，应用期间都启用商店；
                // 原始状态已经写入恢复点，点击“全面恢复”时仍能照原样还原。
                Run(adb, "-s " + serial + " shell pm default-state --user 0 com.mumu.store");
                p.Report("已确认 MuMu 商店和软件安装能力保持启用。");
                ApplyOverlay(adb, serial, p);

                bool componentsDone = AreComponentsDisabled(adb, serial);
                bool maaRunning = IsMaaRunning();
                if (componentsDone)
                {
                    p.Report("Android 广告组件已经关闭，无需重启 ADB。");
                }
                else if (maaRunning)
                {
                    p.Report("检测到 MAA 正在运行：为避免 ADB 瞬断，本次不切换 root。");
                    p.Report("Windows 端已经完成；退出 MAA 后再点一次即可补齐 Android 广告组件。");
                }
                else if (RestartAdbAsRoot(adb, serial, p))
                {
                    foreach (string c in Components)
                    {
                        var result = Run(adb, "-s " + serial + " shell pm disable --user 0 " + c);
                        if (result.Code == 0 && result.Output.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) < 0)
                            p.Report("已关闭广告组件：" + c.Substring(c.IndexOf('/') + 1));
                        else
                            p.Report("组件处理失败，已跳过：" + c.Substring(c.IndexOf('/') + 1));
                    }
                    RestoreAdbShell(adb, serial);
                    p.Report("桌面推荐会在下次进入桌面或重启模拟器后消失。");
                }
                else p.Report("当前实例不允许 ADB root，已保留安装能力并跳过 Android 组件修改。");
            }
            else p.Report("模拟器未运行：Windows 端已完成；启动模拟器后请再次点击“全面去除广告”完成桌面处理。");

            p.Report("更新器、虚拟机网络、游戏数据和性能配置均未修改。");
            return IsMaaRunning() && serial != null && !AreComponentsDisabled(adb, serial)
                ? "MAA 保护已生效。退出 MAA 后再点一次“全面去除广告”即可完成。"
                : "完成。建议重启 MuMuPlayer 后检查界面。";
        }

        public string Restore(IProgress<string> p)
        {
            RequireAdmin();
            string[] ruleNames = { "Main", "MainCEF", "DeviceCEF" };
            for (int i = 0; i < ruleNames.Length; i++)
            {
                Run("netsh", "advfirewall firewall delete rule name=\"" + RulePrefix + ruleNames[i] + "\"");
            }
            p.Report("已删除本程序创建的 Windows 防火墙规则。");

            string root = ReadState("InstallPath");
            if (String.IsNullOrEmpty(root)) root = FindInstall();
            string adb = FindAdb(root);
            string serial = adb == null ? null : ConnectAdb(adb, p);
            RestoreStartupCache(p);
            if (serial != null) RestoreOverlay(adb, serial, p);
            bool restored = true;
            if (adb != null && serial != null)
            {
                if (!AreComponentsDisabled(adb, serial, false))
                {
                    p.Report("Android 广告组件已是默认状态，无需重启 ADB。");
                }
                else if (IsMaaRunning())
                {
                    restored = false;
                    p.Report("检测到 MAA 正在运行：为避免 ADB 瞬断，Android 组件恢复已延期。");
                    p.Report("退出 MAA 后再点一次“全面恢复”即可补齐。");
                }
                else if (RestartAdbAsRoot(adb, serial, p))
                {
                    foreach (string c in Components) Run(adb, "-s " + serial + " shell pm default-state --user 0 " + c);
                    RestoreAdbShell(adb, serial);
                }
                else restored = false;
                bool wasDisabled = String.Equals(ReadState("StoreWasDisabled"), "True", StringComparison.OrdinalIgnoreCase);
                Run(adb, "-s " + serial + " shell pm " + (wasDisabled ? "disable-user" : "default-state") + " --user 0 com.mumu.store");
                if (restored) p.Report("已恢复 Android 推荐组件和 MuMu 商店的原始启用状态。");
            }
            else p.Report("模拟器未运行：Windows 端已恢复；启动模拟器后再点一次“全面恢复”。");

            if (File.Exists(StateFile) && serial != null && restored) File.Delete(StateFile);
            return restored ? "恢复完成。建议重启 MuMuPlayer。" : "Windows 端已恢复；退出 MAA 后再点一次补齐 Android 恢复。";
        }

        static string StartupIndex()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Netease", "MuMuPlayerGlobal", "data", "startupImage", "imageManager.json");
        }

        static string StartupBackup() { return Path.Combine(StateDir, "startup-" + Environment.UserName + ".bak"); }

        void ApplyStartupCache(IProgress<string> p)
        {
            string file = StartupIndex();
            if (!File.Exists(file)) { p.Report("尚无启动广告索引，启动缓存处理已跳过。"); return; }
            string backup = StartupBackup();
            if (!File.Exists(backup))
            {
                File.Copy(file, backup);
                File.WriteAllText(backup + ".attributes", ((int)File.GetAttributes(file)).ToString());
            }
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            File.WriteAllText(file, "{\"info\":[]}", new UTF8Encoding(false));
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            p.Report("已备份并清空当前用户的启动推广索引；普通写入已限制。更新后仍需重新检测。");
        }

        void RestoreStartupCache(IProgress<string> p)
        {
            string file = StartupIndex(), backup = StartupBackup();
            if (!File.Exists(backup)) return;
            if (File.Exists(file)) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.Copy(backup, file, true);
            int attrs;
            if (File.Exists(backup + ".attributes") && Int32.TryParse(File.ReadAllText(backup + ".attributes"), out attrs))
                File.SetAttributes(file, (FileAttributes)attrs);
            File.Delete(backup);
            if (File.Exists(backup + ".attributes")) File.Delete(backup + ".attributes");
            p.Report("已恢复启动推广索引和原始文件属性。");
        }

        static string OverlayBackup(string adb, string serial)
        {
            string id = Run(adb, "-s " + serial + " shell settings get secure android_id").Output.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-fA-F0-9]{8,32}$"))
                throw new InvalidOperationException("无法确认 Android 实例身份，已停止保存恢复点。");
            return Path.Combine(StateDir, "overlay-" + id + ".ini");
        }

        void ApplyOverlay(string adb, string serial, IProgress<string> p)
        {
            string backup = OverlayBackup(adb, serial);
            if (!File.Exists(backup))
            {
                Result before = Run(adb, "-s " + serial + " shell cmd appops get com.mumu.store SYSTEM_ALERT_WINDOW");
                if (before.Code != 0) throw new InvalidOperationException("无法读取商店悬浮窗原始状态。");
                var m = System.Text.RegularExpressions.Regex.Match(before.Output, @"SYSTEM_ALERT_WINDOW:\s*(allow|ignore|deny|default|foreground)");
                if (!m.Success && before.Output.IndexOf("No operations", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("无法识别商店悬浮窗原始状态。");
                File.WriteAllText(backup, m.Success ? m.Groups[1].Value : "default");
            }
            Result set = Run(adb, "-s " + serial + " shell cmd appops set com.mumu.store SYSTEM_ALERT_WINDOW ignore");
            if (set.Code != 0) throw new InvalidOperationException("设置商店悬浮窗失败：" + set.Output);
            string current = Run(adb, "-s " + serial + " shell cmd appops get com.mumu.store SYSTEM_ALERT_WINDOW").Output;
            if (!current.Contains("SYSTEM_ALERT_WINDOW: ignore")) throw new InvalidOperationException("商店悬浮窗状态校验失败。");
            p.Report("已限制商店悬浮窗弹广告权限，安装服务保持启用；没有重启 ADB。");
        }

        void RestoreOverlay(string adb, string serial, IProgress<string> p)
        {
            string backup = OverlayBackup(adb, serial);
            if (!File.Exists(backup)) return;
            string mode = File.ReadAllText(backup).Trim();
            if (!new[] { "allow", "ignore", "deny", "default", "foreground" }.Contains(mode))
                throw new InvalidOperationException("恢复记录格式有误。");
            Result set = Run(adb, "-s " + serial + " shell cmd appops set com.mumu.store SYSTEM_ALERT_WINDOW " + mode);
            if (set.Code != 0) throw new InvalidOperationException("商店悬浮窗恢复失败。");
            File.Delete(backup);
            p.Report("已恢复商店悬浮窗原始权限。");
        }

        public string CloseInOrder()
        {
            if (IsMaaRunning()) return "请先结束任务并退出 MAA，再按顺序退出 MuMu，避免中断自动操作。";
            string root = FindInstall();
            if (root == null) throw new InvalidOperationException("没有找到 MuMu。");
            string manager = Path.Combine(root, "nx_main", "MuMuManager.exe");
            if (!File.Exists(manager)) throw new InvalidOperationException("找不到官方管理接口。");
            // 仅处理本安装目录的主实例；多开情况下要求用户先自行关闭其他实例。
            Result all = Run(manager, "info -v all");
            if (all.Code != 0) throw new InvalidOperationException("无法读取实例状态，停止退出。");
            var started = System.Text.RegularExpressions.Regex.Matches(all.Output, "\\\"is_process_started\\\"\\s*:\\s*true");
            if (started.Count > 1) return "检测到多个运行实例，请先自行关闭其他实例。";
            Result primary = Run(manager, "info -v 0");
            if (primary.Code != 0 || !primary.Output.Contains("is_process_started")) throw new InvalidOperationException("无法确认主实例状态。");
            if (started.Count == 1 && !System.Text.RegularExpressions.Regex.IsMatch(primary.Output, "\\\"is_process_started\\\"\\s*:\\s*true"))
                return "正在运行的是其他多开实例，请先自行关闭该实例。";
            var main = Process.GetProcessesByName("MuMuNxMain").Where(x => {
                try { return String.Equals(x.MainModule.FileName, Path.Combine(root, "nx_main", "MuMuNxMain.exe"), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }).ToArray();
            Result shutdown = Run(manager, "control -v 0 shutdown");
            if (shutdown.Code != 0) throw new InvalidOperationException("官方实例关闭命令失败。");
            bool stopped = false;
            for (int i = 0; i < 30; i++)
            {
                Result info = Run(manager, "info -v 0");
                if (info.Code == 0 && System.Text.RegularExpressions.Regex.IsMatch(info.Output, "\\\"is_process_started\\\"\\s*:\\s*false")) { stopped = true; break; }
                System.Threading.Thread.Sleep(500);
            }
            if (!stopped) throw new InvalidOperationException("Android 未确认停止，已保留管理器，未强杀进程。");
            Result close = Run(manager, "main close");
            if (close.Code != 0) throw new InvalidOperationException("官方管理器退出命令失败。");
            foreach (Process pr in main)
                using (pr) { if (!pr.WaitForExit(15000)) return "Android 已停止，但管理器仍未退出；没有强制结束进程。"; }
            return "Android 已停止，管理器已退出。未使用强制结束。";
        }

        public string Optimize(bool undo)
        {
            RequireAdmin();
            string manager = Path.Combine(FindInstall() ?? "", "nx_main", "MuMuManager.exe");
            if (!File.Exists(manager)) throw new InvalidOperationException("找不到 MuMu 官方配置工具。");
            // 当前仅支持主实例，避免把多开的其他实例一并修改。
            string backup = Path.Combine(StateDir, "fps-main.ini");
            string before = Run(manager, "setting -v 0 --key max_frame_rate").Output;
            var match = System.Text.RegularExpressions.Regex.Match(before, "\\\"max_frame_rate\\\"\\s*:\\s*\\\"?(\\d+)");
            int oldFps;
            if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out oldFps) || oldFps < 1 || oldFps > 480)
                throw new InvalidOperationException("无法读取主实例帧率，未修改设置。");
            Directory.CreateDirectory(StateDir);
            int target = Math.Min(oldFps, 30);
            if (undo)
            {
                if (!File.Exists(backup)) return "没有需要恢复的帧率记录。";
                if (!Int32.TryParse(File.ReadAllText(backup), out target) || target < 1 || target > 480)
                    throw new InvalidOperationException("帧率恢复记录有误。");
            }
            else if (!File.Exists(backup)) File.WriteAllText(backup, oldFps.ToString());
            Result set = Run(manager, "setting -v 0 --key max_frame_rate --value " + target);
            string verify = Run(manager, "setting -v 0 --key max_frame_rate").Output;
            var checkedValue = System.Text.RegularExpressions.Regex.Match(verify, "\\\"max_frame_rate\\\"\\s*:\\s*\\\"?(\\d+)");
            if (set.Code != 0 || !checkedValue.Success || checkedValue.Groups[1].Value != target.ToString())
                throw new InvalidOperationException("官方配置接口未确认修改成功，恢复记录已保留。");
            if (undo) File.Delete(backup);
            return undo ? "主实例帧率已恢复为 " + target + " FPS。" : "主实例帧率上限设为 " + target + " FPS。流畅度会降低，实际占用收益取决于游戏；可随时恢复。";
        }

        void AddFirewall(string root, string suffix, string exe, IProgress<string> p)
        {
            if (!File.Exists(exe)) { p.Report("跳过不存在的组件：" + exe); return; }
            string name = RulePrefix + suffix;
            Run("netsh", "advfirewall firewall delete rule name=\"" + name + "\"");
            var r = Run("netsh", "advfirewall firewall add rule name=\"" + name + "\" dir=out action=block program=\"" + exe + "\" enable=yes profile=any");
            if (r.Code != 0) throw new InvalidOperationException("创建规则失败：" + r.Output);
            p.Report("已隔离广告联网组件：" + Path.GetFileName(exe));
        }

        string ConnectAdb(string adb, IProgress<string> p)
        {
            string connected = FindConnectedDevice(adb);
            if (connected != null)
            {
                p.Report("已复用现有 Android 连接：" + connected);
                return connected;
            }
            string[] ports = { "127.0.0.1:16384", "127.0.0.1:16416", "127.0.0.1:16448", "127.0.0.1:16480", "127.0.0.1:16512", "127.0.0.1:16544", "127.0.0.1:16576", "127.0.0.1:7555", "127.0.0.1:5555" };
            foreach (string s in ports)
            {
                Run(adb, "connect " + s);
                string o = Run(adb, "-s " + s + " get-state").Output.Trim();
                if (o.Contains("device")) { p.Report("已连接 Android 实例：" + s); return s; }
            }
            return null;
        }

        static string FindConnectedDevice(string adb)
        {
            Result result = Run(adb, "devices");
            var devices = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(x => x.Length >= 2 && x[1] == "device")
                .Select(x => x[0]).ToList();
            string official = devices.FirstOrDefault(x => IsMaaPort(x));
            if (official != null) return official;
            string emulator = devices.FirstOrDefault(x => x.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase));
            return emulator;
        }

        static bool IsMaaPort(string serial)
        {
            if (!serial.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase)) return false;
            int port;
            if (!Int32.TryParse(serial.Substring(serial.IndexOf(':') + 1), out port)) return false;
            return port >= 16384 && port <= 16576 && (port - 16384) % 32 == 0;
        }

        static bool AreComponentsDisabled(string adb, string serial, bool requireAll = true)
        {
            Result result = Run(adb, "-s " + serial + " shell dumpsys package com.mumu.store");
            int marker = result.Output.IndexOf("disabledComponents:", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return false;
            string tail = result.Output.Substring(marker, Math.Min(2500, result.Output.Length - marker));
            int matched = 0;
            foreach (string component in Components)
            {
                string name = component.Substring(component.IndexOf('/') + 1).TrimStart('.');
                if (tail.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) matched++;
            }
            return requireAll ? matched == Components.Length : matched > 0;
        }

        static bool IsMaaRunning()
        {
            string[] names = { "MAA", "MaaWpfGui", "MFAAvalonia" };
            foreach (string name in names)
            {
                try { if (Process.GetProcessesByName(name).Length > 0) return true; }
                catch { }
            }
            return false;
        }

        bool RestartAdbAsRoot(string adb, string serial, IProgress<string> p)
        {
            Run(adb, "-s " + serial + " root");
            Run(adb, "-s " + serial + " wait-for-device");
            string identity = Run(adb, "-s " + serial + " shell id").Output;
            bool rooted = identity.IndexOf("uid=0(root)", StringComparison.OrdinalIgnoreCase) >= 0;
            if (rooted) p.Report("已临时取得组件管理权限。");
            return rooted;
        }

        void RestoreAdbShell(string adb, string serial)
        {
            Run(adb, "-s " + serial + " unroot");
            Run(adb, "-s " + serial + " wait-for-device");
        }

        static string FindInstall()
        {
            string[] keys = {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MuMuPlayerGlobal",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\MuMuPlayerGlobal"
            };
            foreach (string k in keys)
            {
                object v = Registry.GetValue(k, "InstallLocation", null);
                if (v != null && Directory.Exists(v.ToString())) return v.ToString().TrimEnd('\\');
            }
            string fixedPath = @"E:\MuMuPlayerGlobal";
            if (Directory.Exists(fixedPath)) return fixedPath;
            return null;
        }

        static string FindAdb(string root)
        {
            if (String.IsNullOrEmpty(root)) return null;
            // MAA 默认使用 nx_main\adb.exe。优先复用同一版本和同一个 ADB server，避免互相抢占。
            string[] candidates = { Path.Combine(root, "nx_main", "adb.exe"), Path.Combine(root, "nx_device", "15.0", "shell", "adb.exe") };
            string found = candidates.FirstOrDefault(File.Exists);
            if (found != null) return found;
            try
            {
                string deviceRoot = Path.Combine(root, "nx_device");
                if (Directory.Exists(deviceRoot)) return Directory.GetFiles(deviceRoot, "adb.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch { }
            return null;
        }

        static string GetVersion(string root)
        {
            try
            {
                object reg = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MuMuPlayerGlobal", "DisplayVersion", null);
                if (reg != null && !String.IsNullOrWhiteSpace(reg.ToString())) return reg.ToString();
                string value = FileVersionInfo.GetVersionInfo(Path.Combine(root, "nx_main", "MuMuNxMain.exe")).FileVersion;
                return String.IsNullOrWhiteSpace(value) ? "未知" : value;
            }
            catch { return "未知"; }
        }

        static bool IsAdmin()
        {
            var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void RequireAdmin()
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("请以管理员身份运行本程序。");
        }

        static string ReadState(string key)
        {
            if (!File.Exists(StateFile)) return null;
            string line = File.ReadAllLines(StateFile).FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            return line == null ? null : line.Substring(key.Length + 1);
        }

        public static string SelfTest()
        {
            string root = FindInstall();
            var lines = new List<string>();
            lines.Add("SELF-TEST OK");
            lines.Add("Install=" + (root ?? "NOT_FOUND"));
            lines.Add("Version=" + (root == null ? "N/A" : GetVersion(root)));
            lines.Add("ADB=" + (FindAdb(root) ?? "NOT_FOUND"));
            lines.Add("Admin=" + IsAdmin());
            lines.Add("State=" + StateFile);
            return String.Join(Environment.NewLine, lines.ToArray());
        }

        public static string MaaCheck()
        {
            string root = FindInstall();
            string adb = FindAdb(root);
            string serial = adb == null ? null : FindConnectedDevice(adb);
            var lines = new List<string>();
            lines.Add("MAA-CHECK " + (serial == null ? "INCOMPLETE" : "OK"));
            lines.Add("MAA=" + (IsMaaRunning() ? "RUNNING (PROTECTED)" : "NOT_RUNNING"));
            lines.Add("ADB=" + (adb ?? "NOT_FOUND"));
            lines.Add("Device=" + (serial ?? "NOT_CONNECTED"));
            if (adb != null && serial != null)
            {
                lines.Add("Identity=" + Run(adb, "-s " + serial + " shell id").Output.Trim());
                bool storeEnabled = Run(adb, "-s " + serial + " shell pm list packages -e com.mumu.store").Output.Contains("package:com.mumu.store");
                bool installerEnabled = Run(adb, "-s " + serial + " shell pm list packages -e com.android.packageinstaller").Output.Contains("package:com.android.packageinstaller");
                lines.Add("StoreEnabled=" + storeEnabled);
                lines.Add("InstallerEnabled=" + installerEnabled);
                lines.Add("AdComponentsDisabled=" + AreComponentsDisabled(adb, serial));
            }
            return String.Join(Environment.NewLine, lines.ToArray());
        }

        sealed class Result { public int Code; public string Output; }

        static Result Run(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var pr = Process.Start(psi))
                {
                    Task<string> output = pr.StandardOutput.ReadToEndAsync();
                    Task<string> error = pr.StandardError.ReadToEndAsync();
                    if (!pr.WaitForExit(15000))
                    {
                        pr.Kill();
                        return new Result { Code = -1, Output = "命令超时，已结束本次命令进程。" };
                    }
                    Task.WaitAll(output, error);
                    return new Result { Code = pr.ExitCode, Output = output.Result + error.Result };
                }
            }
            catch (Exception ex) { return new Result { Code = -1, Output = ex.Message }; }
        }
    }
}

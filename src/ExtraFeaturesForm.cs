using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MuMuClean
{
    sealed class ExtraFeaturesForm : Form
    {
        readonly RichTextBox log = new RichTextBox();
        readonly Button configure = new Button(), rollback = new Button(), disable = new Button(), restore = new Button();
        readonly string root;
        internal ExtraFeaturesForm(string installRoot)
        {
            root = installRoot;
            Text = "Root 与更新控制"; ClientSize = new Size(720, 550);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(15, 19, 27); ForeColor = Color.White; Font = new Font("Microsoft YaHei UI", 10F);
            AddLabel("Android 12 Root 环境", 24, 22, 16F);
            AddLabel("仅适配 5.30.2。配置和回退会重启主实例；Root 模式需保持系统盘可写。", 24, 62, 9F);
            AddLabel("配置 KitsuneMagisk 31024 / Vector 2.0，首次下载并校验。部分 App 检测 Root，可回退。", 24, 88, 9F);
            Button(configure, "一键配置 Root", 24, 123); Button(rollback, "回退 Root 环境", 246, 123);
            bool supported = root != null && Cleaner.GetVersion(root).StartsWith("5.30.2", StringComparison.Ordinal);
            configure.Enabled = rollback.Enabled = supported;
            AddLabel("MuMu 更新控制", 24, 198, 16F);
            AddLabel("关闭后不再运行 MuMu 更新器，含检查、提示和下载；手动更新前先恢复。", 24, 238, 9F);
            AddLabel("用户 App 和游戏的更新不在此开关范围内。当前会话的提示缓存需重开管理器清除。", 24, 264, 9F);
            Button(disable, "关闭 MuMu 更新", 24, 299); Button(restore, "恢复 MuMu 更新", 246, 299);
            log.Location = new Point(24, 375); log.Size = new Size(672, 150); log.ReadOnly = true; log.BorderStyle = BorderStyle.None;
            log.BackColor = Color.FromArgb(10, 14, 21); log.ForeColor = Color.FromArgb(202, 211, 225); Controls.Add(log);
            configure.Click += async (s, e) => await Run(p => new RootEnvironment(root, 0).Configure(p));
            rollback.Click += async (s, e) => await Run(p => new RootEnvironment(root, 0, true).Rollback(p));
            disable.Click += async (s, e) => await Run(p => new UpdateControl(root).Disable(p));
            restore.Click += async (s, e) => await Run(p => new UpdateControl(root).Restore(p));
            Shown += (s, e) => { try { Write(new UpdateControl(root).Status()); } catch (Exception error) { Write(error.Message); } };
        }
        void AddLabel(string text, int x, int y, float size)
        {
            Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y), Font = new Font(Font.FontFamily, size), ForeColor = size > 10 ? Color.White : Color.FromArgb(174, 183, 199) });
        }
        void Button(Button button, string text, int x, int y)
        {
            button.Text = text; button.Location = new Point(x, y); button.Size = new Size(200, 50);
            button.BackColor = Color.FromArgb(48, 58, 73); button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; Controls.Add(button);
        }
        void Write(string text) { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
        async Task Run(Func<IProgress<string>, string> action)
        {
            configure.Enabled = rollback.Enabled = disable.Enabled = restore.Enabled = false; ControlBox = false;
            try { var progress = new Progress<string>(Write); Write(await Task.Run(() => action(progress))); }
            catch (Exception error) { Write("未完成：" + error.Message); }
            finally
            {
                configure.Enabled = rollback.Enabled = root != null && Cleaner.GetVersion(root).StartsWith("5.30.2", StringComparison.Ordinal);
                disable.Enabled = restore.Enabled = true; ControlBox = true;
            }
        }
    }
}

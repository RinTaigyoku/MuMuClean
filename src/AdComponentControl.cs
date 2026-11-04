using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MuMuClean
{
    // Verified on supported MuMu builds: the native management channel remains
    // available with guest root switched off. No root switch or ADB restart.
    sealed class AdComponentControl
    {
        internal static readonly string[] Components = {
            "com.mumu.store/.widget.appWidgetProvider.AdBannerWidgetProvider",
            "com.mumu.store/.widget.appWidgetProvider.DailyDiscoveryWidgetProvider",
            "com.mumu.store/.track.AdTrackingReceiver",
            "com.mumu.store/.track.TrackingReceiver"
        };
        readonly string manager, index, adb, serial, backup;

        internal AdComponentControl(string adbPath, string device, string stateDirectory)
        {
            adb = adbPath; serial = device;
            manager = Path.Combine(Path.GetDirectoryName(adb), "MuMuManager.exe");
            if (!File.Exists(manager)) throw new InvalidOperationException("缺少 MuMu 官方管理工具，组件规则未修改。");
            Match port = Regex.Match(serial, @"^127\.0\.0\.1:(\d+)$");
            int number;
            if (!port.Success || !Int32.TryParse(port.Groups[1].Value, out number) || number < 16384 || (number - 16384) % 32 != 0)
                throw new InvalidOperationException("尚未确认官方实例端口，组件规则未修改。");
            index = ((number - 16384) / 32).ToString(CultureInfo.InvariantCulture);
            Cleaner.Result info = Cleaner.Run(manager, "info -v " + index);
            var fields = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(info.Output);
            if (info.Code != 0 || fields == null || !fields.ContainsKey("adb_port") || Convert.ToInt32(fields["adb_port"]) != number)
                throw new InvalidOperationException("管理工具与 ADB 实例不一致，组件规则未修改。");
            Cleaner.Result identity = Shell("id");
            if (identity.Code != 0 || !identity.Output.Contains("uid=0(root)") || !identity.Output.Contains("nemuinit"))
                throw new InvalidOperationException("当前 MuMu 管理通道不支持组件维护，已跳过。");
            string id = Cleaner.Run(adb, "-s " + serial + " shell settings get secure android_id").Output.Trim();
            if (!Regex.IsMatch(id, "^[a-fA-F0-9]{8,32}$")) throw new InvalidOperationException("无法确认实例身份，未建立组件恢复点。");
            backup = Path.Combine(stateDirectory, "components-" + id + ".ini");
        }

        internal void Apply(IProgress<string> progress, bool migrateLegacy)
        {
            Dictionary<string, int> current = ReadStates();
            if (!File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                using (var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                    foreach (string component in Components)
                        writer.WriteLine(component + "=" + ((migrateLegacy && index == "0" && current[component] == 2) ? 0 : current[component]));
            }
            ReadBackup(); // Never continue with an incomplete recovery record.
            foreach (string component in Components)
            {
                if (current[component] == 2) continue;
                SetState(component, 2);
            }
            if (ReadStates().Values.Any(state => state != 2)) throw new InvalidOperationException("广告组件未全部关闭，恢复点已保留。");
            progress.Report("已通过 MuMu 官方管理通道关闭四个广告组件；没有切换 root 开关或重启 ADB。");
        }

        internal bool Restore(IProgress<string> progress, bool legacy)
        {
            if (!File.Exists(backup) && !legacy) return true;
            Dictionary<string, int> original = File.Exists(backup) ? ReadBackup() : Components.ToDictionary(component => component, component => 0);
            Dictionary<string, int> current = ReadStates();
            foreach (string component in Components)
                if (current[component] != original[component]) SetState(component, original[component]);
            current = ReadStates();
            if (Components.Any(component => current[component] != original[component])) return false;
            if (File.Exists(backup)) File.Delete(backup);
            progress.Report("已恢复广告组件原始状态，ADB 连接保持不变。");
            return true;
        }

        Dictionary<string, int> ReadBackup()
        {
            var states = new Dictionary<string, int>();
            foreach (string line in File.ReadAllLines(backup))
            {
                int equal = line.LastIndexOf('='), state;
                if (equal < 1 || !Int32.TryParse(line.Substring(equal + 1), out state) || state < 0 || state > 2) throw new InvalidOperationException("组件恢复记录不完整，已停止修改。");
                string name = line.Substring(0, equal);
                if (!Components.Contains(name) || states.ContainsKey(name)) throw new InvalidOperationException("组件恢复记录不匹配，已停止修改。");
                states.Add(name, state);
            }
            if (states.Count != Components.Length) throw new InvalidOperationException("组件恢复记录缺少项目，已停止修改。");
            return states;
        }

        Dictionary<string, int> ReadStates()
        {
            string dump = Cleaner.Run(adb, "-s " + serial + " shell dumpsys package com.mumu.store").Output;
            if (!dump.Contains("User 0:")) throw new InvalidOperationException("没有读到用户 0 的商店组件状态。");
            var states = Components.ToDictionary(component => component, component => 0);
            bool user = false; int section = 0;
            foreach (string line in dump.Replace("\r", "").Split('\n'))
            {
                string value = line.Trim();
                if (Regex.IsMatch(value, @"^User \d+:")) { user = value.StartsWith("User 0:", StringComparison.Ordinal); section = 0; }
                if (!user) continue;
                if (value == "disabledComponents:") { section = 2; continue; }
                if (value == "enabledComponents:") { section = 1; continue; }
                if (value.EndsWith(":", StringComparison.Ordinal)) { section = 0; continue; }
                if (section == 0) continue;
                foreach (string component in Components)
                {
                    string name = component.Substring(component.IndexOf('/') + 1);
                    if (name.StartsWith(".", StringComparison.Ordinal)) name = "com.mumu.store" + name;
                    if (value == name || value == component) states[component] = section;
                }
            }
            return states;
        }

        void SetState(string component, int state)
        {
            string verb = state == 0 ? "default-state" : (state == 1 ? "enable" : "disable");
            Cleaner.Result result = Shell("pm " + verb + " --user 0 " + component);
            if (ReadStates()[component] == state) return;
            string reason = result.Output.Trim();
            if (reason.Length > 300) reason = reason.Substring(0, 300);
            throw new InvalidOperationException("组件未达到目标状态，恢复记录已保留：" + component + "；" + reason);
        }

        Cleaner.Result Shell(string command)
        {
            return Cleaner.Run(manager, "sh -v " + index + " --cmd \"" + command + "\"");
        }
    }
}

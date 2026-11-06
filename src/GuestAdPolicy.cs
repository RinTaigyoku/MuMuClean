using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace MuMuClean
{
    // Guest root settings and Android HOME are deliberately left untouched.
    // This uses the official local management channel, not an ADB root daemon.
    sealed class GuestAdPolicy
    {
        const string Chain = "MUMUCLEAN_ADS_V1";
        const string Marker = "MuMuClean-Ads-v1";
        const string Host = "api.mumuglobal.com";
        const string Sni = "0000126170692e6d756d75676c6f62616c2e636f6d";
        readonly string manager, index;
        readonly Func<string, string> shell;
        bool? legacyAndroid12;

        internal bool LegacyAndroid12
        {
            get
            {
                if (!legacyAndroid12.HasValue)
                {
                    string sdk = shell("getprop ro.build.version.sdk").Trim();
                    int number;
                    if (!Int32.TryParse(sdk, out number)) throw new InvalidOperationException("Android 版本尚未确认，未修改规则。");
                    legacyAndroid12 = sdk == "31" || sdk == "32";
                }
                return legacyAndroid12.Value;
            }
        }

        internal GuestAdPolicy(string managerPath, int instance)
        {
            if (instance < 0 || instance > 4095) throw new ArgumentOutOfRangeException("instance");
            manager = managerPath;
            index = instance.ToString(CultureInfo.InvariantCulture);
            shell = Execute;
        }

        internal static GuestAdPolicy ForDevice(string adb, string serial)
        {
            Match match = Regex.Match(serial ?? "", @"^127\.0\.0\.1:(\d+)$");
            int port;
            if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out port) || port < 16384 || port > 65535 || (port - 16384) % 32 != 0)
                throw new InvalidOperationException("未确认 MuMu 实例端口，未修改网络规则。");
            return new GuestAdPolicy(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(adb), "MuMuManager.exe"), (port - 16384) / 32);
        }

        internal bool Active()
        {
            try
            {
                using (var gate = new ManagementLock(index))
                {
                    int[] owners = ReadOwners();
                    if (LegacyAndroid12 && !owners.All(uid => GuestUidFilter.Check(uid, shell))) return false;
                    return new[] { "iptables", "ip6tables" }.All(tool => IsApplied(Read(tool), owners));
                }
            }
            catch { return false; }
        }

        internal void Apply()
        {
            using (var gate = new ManagementLock(index)) ApplyCore();
        }

        void ApplyCore()
        {
            string identity = shell("id");
            bool knownContext = identity.Contains("nemuinit") || (LegacyAndroid12 && identity.Contains("context=u:r:init:s0"));
            if (!identity.Contains("uid=0(root)") || !knownContext)
                throw new InvalidOperationException("当前管理通道不支持广告网络规则，未修改 root 设置。");
            int[] owners = ReadOwners();
            if (LegacyAndroid12)
            {
                string adb = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(manager), "adb.exe");
                string serial = "127.0.0.1:" + (16384 + Int32.Parse(index) * 32);
                foreach (int uid in owners) GuestUidFilter.Load(uid, adb, serial, shell, RunProcess);
            }
            var before = new Dictionary<string, string>();
            foreach (string tool in new[] { "iptables", "ip6tables" })
            {
                string rules = Read(tool);
                ValidateOwnership(rules);
                before.Add(tool, rules);
            }
            try
            {
                foreach (var item in before)
                {
                    if (IsApplied(item.Value, owners)) continue;
                    string script = "*filter\n";
                    if (!HasChain(item.Value)) script += ":" + Chain + " - [0:0]\n";
                    else script += "-F " + Chain + "\n";
                    foreach (string line in Jumps(item.Value)) script += "-D" + line.Substring(2) + "\n";
                    script += Rule443() + "\n" + Rule80() + "\n";
                    foreach (int uid in owners) script += Jump(uid) + "\n";
                    script += "COMMIT\n";
                    RestoreTable(item.Key, script);
                    if (!IsApplied(Read(item.Key), owners)) throw new InvalidOperationException("广告网络规则写入后校验失败。");
                }
            }
            catch
            {
                // The caller retains the recovery record if rollback itself fails.
                foreach (var item in before)
                {
                    try { RestoreOwned(item.Key, item.Value); }
                    catch { }
                }
                throw;
            }
        }

        internal void Remove()
        {
            using (var gate = new ManagementLock(index))
            {
                var pins = new HashSet<int>();
                foreach (string tool in new[] { "iptables", "ip6tables" })
                {
                    foreach (string jump in Jumps(Read(tool)))
                    {
                        Match pin = Regex.Match(jump, @"/sys/fs/bpf/mumuclean_uid_v1_(1000|1\d{4}) ");
                        if (pin.Success) pins.Add(Int32.Parse(pin.Groups[1].Value));
                    }
                    RestoreOwned(tool, "");
                }
                if (LegacyAndroid12)
                {
                    foreach (int uid in ReadOwners()) pins.Add(uid);
                    foreach (int uid in pins) GuestUidFilter.Remove(uid, shell);
                    GuestUidFilter.RemoveBinary(shell);
                }
            }
        }

        sealed class ManagementLock : IDisposable
        {
            readonly Mutex mutex;
            bool held;
            internal ManagementLock(string instance)
            {
                mutex = new Mutex(false, "Local\\MuMuClean.GuestPolicy." + instance);
                try { held = mutex.WaitOne(15000); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) { mutex.Dispose(); throw new TimeoutException("广告规则正在处理中，请稍后重试。"); }
            }
            public void Dispose() { if (held) { mutex.ReleaseMutex(); held = false; } mutex.Dispose(); }
        }

        void RestoreOwned(string tool, string original)
        {
            string current = Read(tool);
            ValidateOwnership(current);
            if (!HasChain(current)) return;
            string script = "*filter\n";
            foreach (string line in Jumps(current)) script += "-D" + line.Substring(2) + "\n";
            script += "-F " + Chain + "\n";
            if (HasChain(original))
            {
                foreach (string line in Lines(original).Where(line => line.StartsWith("-A " + Chain + " ", StringComparison.Ordinal))) script += line + "\n";
                foreach (string line in Jumps(original)) script += line + "\n";
            }
            else script += "-X " + Chain + "\n";
            RestoreTable(tool, script + "COMMIT\n");
            string after = Read(tool);
            if (HasChain(original) ? !OwnedLines(after).SequenceEqual(OwnedLines(original)) : HasChain(after) || Jumps(after).Any())
                throw new InvalidOperationException("广告网络规则未完整恢复。");
        }

        int[] ReadOwners()
        {
            // Only these two installed package UIDs are eligible. Never wildcard user applications.
            string result = shell("sed -n -e 's/^app\\.lawnchair \\([0-9][0-9]*\\) .*/native=\\1/p' -e 's/^com\\.mumu\\.store \\([0-9][0-9]*\\) .*/store=\\1/p' /data/system/packages.list");
            var native = Regex.Match(result, @"(?m)^native=(\d+)\r?$");
            var store = Regex.Match(result, @"(?m)^store=(\d+)\r?$");
            if (native.Success && !store.Success && LegacyAndroid12)
            {
                int launcher = Int32.Parse(native.Groups[1].Value);
                if (launcher < 10000 || launcher > 19999) throw new InvalidOperationException("原桌面应用身份超出已验证范围。");
                return new[] { launcher };
            }
            if (!native.Success || !store.Success) throw new InvalidOperationException("广告组件的应用身份尚未就绪。");
            int first = Int32.Parse(native.Groups[1].Value), second = Int32.Parse(store.Groups[1].Value);
            if (first < 10000 || first > 19999 || second < 1000 || second > 19999)
                throw new InvalidOperationException("应用身份超出已验证范围，未添加网络规则。");
            return new[] { first, second }.Distinct().OrderBy(value => value).ToArray();
        }

        static string Rule443() { return "-A " + Chain + " -p tcp -m tcp --dport 443 -m string --hex-string \"|" + Sni + "|\" --algo kmp -j REJECT --reject-with tcp-reset"; }
        static string Rule80() { return "-A " + Chain + " -p tcp -m tcp --dport 80 -m string --string \"Host: " + Host + "\" --algo kmp -j REJECT --reject-with tcp-reset"; }
        string Jump(int uid)
        {
            string selector = LegacyAndroid12 ? "bpf --object-pinned " + GuestUidFilter.Pin(uid) : "owner --uid-owner " + uid;
            return "-A OUTPUT -m " + selector + " -m comment --comment " + Marker + " -j " + Chain;
        }
        static string Normalize(string line)
        {
            // Android 12's iptables prints the default full-packet string range explicitly.
            return line.Replace("\"" + Marker + "\"", Marker).Replace(" --algo kmp --to 65535 ", " --algo kmp ");
        }
        static string[] Lines(string rules) { return rules.Replace("\r", "").Split('\n').Select(line => Normalize(line.Trim())).Where(line => line.Length > 0).ToArray(); }
        static bool HasChain(string rules) { return Lines(rules).Contains("-N " + Chain); }
        static IEnumerable<string> Jumps(string rules) { return Lines(rules).Where(line => line.EndsWith("-j " + Chain, StringComparison.Ordinal)); }
        static IEnumerable<string> OwnedLines(string rules) { return Lines(rules).Where(line => line.StartsWith("-A " + Chain + " ", StringComparison.Ordinal) || line.EndsWith("-j " + Chain, StringComparison.Ordinal)).OrderBy(line => line); }
        bool IsApplied(string rules, int[] owners)
        {
            ValidateOwnership(rules);
            var expected = owners.Select(Jump).Concat(new[] { Rule443(), Rule80() }).OrderBy(line => line);
            return HasChain(rules) && OwnedLines(rules).SequenceEqual(expected);
        }
        static void ValidateOwnership(string rules)
        {
            if (!rules.Contains("-P OUTPUT ")) throw new InvalidOperationException("没有读到 Android 网络规则。");
            string[] own = OwnedLines(rules).ToArray();
            if (!HasChain(rules) && own.Length == 0) return;
            foreach (string line in own)
            {
                if (line == Rule443() || line == Rule80()) continue;
                if (Regex.IsMatch(line, @"^-A OUTPUT -m owner --uid-owner \d+ -m comment --comment " + Marker + " -j " + Chain + "$")) continue;
                if (Regex.IsMatch(line, @"^-A OUTPUT -m bpf --object-pinned /sys/fs/bpf/mumuclean_uid_v1_(1000|1\d{4}) -m comment --comment " + Marker + " -j " + Chain + "$")) continue;
                throw new InvalidOperationException("同名网络规则不属于本程序，已停止修改。");
            }
            if (!own.Contains(Rule443()) || !own.Contains(Rule80()))
                throw new InvalidOperationException("同名规则链不完整，已停止修改。");
        }

        string Read(string tool) { return shell(tool + " -w 2 -t filter -S"); }
        void RestoreTable(string tool, string script)
        {
            if (LegacyAndroid12)
            {
                // Legacy MuMu's CLI does not reliably transport literal multiline arguments.
                string payload = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(script));
                string result = shell("echo " + payload + " | base64 -d | " + tool + "-restore -w 2 --noflush 2>&1; echo MUMUCLEAN_RULE_STATUS:$?");
                if (!result.TrimEnd().EndsWith("MUMUCLEAN_RULE_STATUS:0", StringComparison.Ordinal))
                    throw new InvalidOperationException("Android 12 网络规则未提交：" + result.Substring(0, Math.Min(result.Length, 240)));
                return;
            }
            // All script fields are fixed strings or validated numeric UIDs.
            // --noflush preserves netd, firewall and third-party chains; one family commits atomically.
            string quoted = script.Replace("'", "'\\''");
            shell("printf '%s' '" + quoted + "' | " + tool + "-restore -w 2 --noflush");
        }
        string Execute(string command)
        {
            const string end = "MUMUCLEAN_COMMAND_END";
            bool legacy = legacyAndroid12 == true;
            if (legacy) command += "; echo " + end;
            string arguments = "sh -v " + index + " --cmd \"" + command.Replace("\"", "\\\"") + "\"";
            string output = RunProcess(manager, arguments);
            if (legacy)
            {
                int marker = output.LastIndexOf(end, StringComparison.Ordinal);
                if (marker < 0) throw new InvalidOperationException("Android 12 管理命令没有返回完成标记。");
                output = output.Substring(0, marker).TrimEnd();
            }
            return output;
        }
        static string RunProcess(string file, string arguments)
        {
            NativeCommand.Result result = NativeCommand.Run(file, arguments);
            if (result.Code != 0) throw new InvalidOperationException("MuMu 管理命令未完成：" + result.Output);
            return result.Output;
        }
    }
}

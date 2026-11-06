using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace MuMuClean
{
    // A loader runs only while configuring a rule. The kernel holds the UID selector.
    static class GuestUidFilter
    {
        static readonly Lazy<byte[]> binary = new Lazy<byte[]>(() => {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("MuMuClean.UidFilter"))
            {
                if (input == null) throw new InvalidOperationException("缺少 Android 12 应用身份过滤组件。");
                using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
            }
        });
        static readonly Lazy<string> hash = new Lazy<string>(() => {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(binary.Value)).Replace("-", "").ToLowerInvariant();
        });
        static string GuestPath { get { return "/data/local/tmp/mumuclean-uid-" + hash.Value.Substring(0, 16); } }
        internal static string Pin(int uid) { Validate(uid); return "/sys/fs/bpf/mumuclean_uid_v1_" + uid; }
        static void Validate(int uid) { if (uid != 1000 && (uid < 10000 || uid > 19999)) throw new InvalidOperationException("应用身份不在允许范围。"); }

        internal static void Load(int uid, string adb, string serial, Func<string, string> shell, Func<string, string, string> run)
        {
            Validate(uid);
            if (!ValidBinary(shell))
            {
                string temporary = Path.GetTempFileName();
                try
                {
                    File.WriteAllBytes(temporary, binary.Value);
                    string result = run(adb, "-s " + serial + " push \"" + temporary + "\" " + GuestPath);
                    if (result.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0 || !ValidBinary(shell))
                        throw new InvalidOperationException("Android 12 身份组件传输校验失败。");
                }
                finally { File.Delete(temporary); }
            }
            shell("chmod 755 " + GuestPath);
            if (!Invoke("load", uid, shell)) throw new InvalidOperationException("Android 12 应用身份规则未能加载。");
        }
        internal static bool Check(int uid, Func<string, string> shell)
        {
            return ValidBinary(shell) && Invoke("check", uid, shell);
        }
        internal static void Remove(int uid, Func<string, string> shell)
        {
            if (!ValidBinary(shell))
            {
                if (shell("if [ -e " + Pin(uid) + " ]; then echo EXISTS; else echo MISSING; fi").Trim() == "MISSING") return;
                throw new InvalidOperationException("缺少校验身份过滤组件的程序，已保留恢复记录。");
            }
            if (!Invoke("remove", uid, shell))
                throw new InvalidOperationException("Android 12 身份过滤组件未完整恢复。");
        }
        internal static void RemoveBinary(Func<string, string> shell)
        {
            if (ValidBinary(shell)) shell("rm " + GuestPath);
        }
        static bool ValidBinary(Func<string, string> shell)
        {
            return shell("sha256sum " + GuestPath + " 2>/dev/null").TrimStart().StartsWith(hash.Value + " ", StringComparison.Ordinal);
        }
        static bool Invoke(string mode, int uid, Func<string, string> shell)
        {
            Validate(uid);
            return shell(GuestPath + " " + mode + " " + uid + " 2>&1").Trim() == "MUMUCLEAN_UID_OK " + uid;
        }
    }
}

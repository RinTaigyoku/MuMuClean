using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MuMuClean
{
    static class LocalRecovery
    {
        internal static string Key(string value)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant().Substring(0, 20);
        }
        internal static string FileHash(string path)
        {
            using (var file = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        internal static Dictionary<string, string> Read(string path)
        {
            var state = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (state == null) throw new InvalidOperationException("恢复记录无法读取。");
            return state;
        }
        internal static void Save(string path, Dictionary<string, string> state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".new";
            byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(state));
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}

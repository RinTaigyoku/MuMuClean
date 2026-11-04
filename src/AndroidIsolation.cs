using System;

namespace MuMuClean
{
    // Migration cleanup for the retired Android VPN experiment. Never installs an app.
    static class AndroidIsolation
    {
        internal const string Package = "me.mumuclean.guard";

        internal static bool Active(string adb, string serial)
        {
            Cleaner.Result result = Call(adb, serial, "shell dumpsys connectivity");
            return result.Code == 0 && result.Output.Contains("VPN CONNECTED extra: VPN:" + Package)
                && result.Output.Contains("10.253.71.2/32");
        }

        internal static bool Installed(string adb, string serial)
        {
            Cleaner.Result result = Call(adb, serial, "shell pm path " + Package);
            return result.Code == 0 && result.Output.TrimStart().StartsWith("package:", StringComparison.Ordinal);
        }

        internal static bool Remove(string adb, string serial, IProgress<string> progress)
        {
            if (!Installed(adb, serial)) return true;
            Cleaner.Result result = Call(adb, serial, "uninstall " + Package);
            bool removed = result.Code == 0 && result.Output.Contains("Success") && !Installed(adb, serial) && !Active(adb, serial);
            progress.Report(removed ? "已卸载本程序的隔离组件并恢复 Android 网络路由。" : "隔离组件未完整移除，请保持模拟器在线后重试恢复。");
            return removed;
        }

        static Cleaner.Result Call(string adb, string serial, string args)
        {
            return Cleaner.Run(adb, "-s \"" + serial + "\" " + args);
        }
    }
}

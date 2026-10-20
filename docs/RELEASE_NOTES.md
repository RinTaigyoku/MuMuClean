# MuMu 清爽助手 1.2.0

这次专门给 MAA 做了适配，同时继续保留 APK/XAPK 安装能力。

我在 MAA 和 MuMu 都开着的情况下检查了完整连接流程。新版会优先复用 `nx_main\adb.exe` 和 MuMu 官方实例端口，不再盲目添加 `7555/5555`，也不会在 MAA 运行时执行 `adb root/unroot`。

如果 Android 广告组件已经处理过，新版只核对状态。如果还没处理，会先完成 Windows 端规则，再提示退出 MAA 后点一次补齐，不会把正在工作的 MAA 连接掐断。

程序仍然不会停用整个 MuMu 商店。系统安装器、XAPK 入口、本地 APK 安装服务和安装广播全部保留。

另外修正了一个状态判断问题：现在读取 Android 的真实组件状态，MuMu 更新后组件被恢复也能准确发现。

## 本机验证

- MuMuPlayer 国际版 6.8.5.0 / Android 15
- MAA 连接和增强截图在只读检查前后保持正常
- `com.mumu.store`：启用
- `com.android.packageinstaller`：启用
- MuMu XAPK 与本地 APK 安装组件：保留
- ADB 最终身份：普通 Shell

下载后直接覆盖旧版即可。MAA 正在跑任务时也可以打开工具查看状态；如果提示“还差一步即可完成”，等 MAA 任务结束并退出 MAA，再点一次“全面去除广告”。

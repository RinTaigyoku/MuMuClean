# MAA 兼容说明

这版专门处理了 MuMu 清爽规则和 MAA 同时使用时最容易踩到的几个问题。

## 适配范围

- MuMuPlayer 国际版 6.8.x / Android 15
- MAA 的 `MuMuEmulator12` 连接配置
- MuMu 官方实例端口 `16384 + 32 × 实例序号`
- MuMu 增强截图与增强触控

程序没有写死 MAA 的安装位置，也不会读取、复制或上传 MAA 的配置和日志。MuMu 安装位置仍按注册表和目录结构自动寻找。

## 保护规则

1. MAA 正在运行时不执行 `adb root`、`adb unroot`、`adb kill-server` 或 `adb disconnect`。
2. 优先复用已经在线的 `127.0.0.1:16384` 等 MuMu 官方实例端口，不重复连接同一台虚拟设备。
3. 优先使用 `nx_main\adb.exe`，与 MAA 保持同一个 ADB 版本和 server。
4. MAA 正在运行时不强制停止 Lawnchair，也不抢前台应用。
5. 如果广告组件已经关闭，只核对状态，不重启 ADB。
6. 如果广告组件还没关闭，Windows 端照常完成，Android 端明确延期；退出 MAA 后再点一次即可补齐。

## 安装能力

程序不会停用整个 `com.mumu.store`。下面这些安装链路都会保留：

- `com.android.packageinstaller`
- `com.mumu.store/.install.XApkInstallerActivity`
- `com.mumu.store/.install.InstallLocalApkService`
- `com.mumu.store/.InstallationReceiver`

四个被处理的对象只负责广告横幅、每日推荐和广告追踪，不属于 APK/XAPK 安装链路。

## 推荐的 MAA 设置

在 MAA 的“设置 - 连接设置”中使用：

- ADB 路径：选择当前 MuMu 安装目录里的 `nx_main\adb.exe`
- 地址：`127.0.0.1:16384`（多开实例按 MAA 检测结果填写）
- 连接配置：MuMuEmulator12
- MuMu 安装路径：填写自己的 MuMuPlayer 国际版安装目录
- 开启 MuMu 截图增强

如果启用了 MuMu 的“应用保活”，触控增强也需要能正常生效；否则按 MAA 提示关闭应用保活。不要把 MuMu 显存策略设成“资源占用更小”，这会影响 MAA 的识别和截图稳定性。

## 自检

命令行运行：

```powershell
.\MuMu清爽助手.exe --maa-check
```

会输出 MAA 保护状态、实际 ADB、设备地址、Shell 身份、商店状态、系统安装器状态和广告组件状态。这个检查是只读的，不会重启 ADB。

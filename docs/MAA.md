# MAA 兼容说明

这次保留 MuMu 原桌面，不额外开一个 Android 页面。去广告不需要改 MAA 的配置。

## 连接方式

优先复用 MuMu 自带的 `nx_main\adb.exe` 和官方实例端口 `16384 + 32 × 实例序号`，连接配置使用 `MuMuEmulator12`。具体模拟器设置以 [MAA 官方文档](https://docs.maa.plus/zh-cn/manual/device/windows.html) 为准；项目地址：[MaaAssistantArknights](https://github.com/MaaAssistantArknights/MaaAssistantArknights)。

新版通过 MuMu 官方管理通道处理广告组件和网络规则，不执行 `adb root/unroot`、`kill-server` 或 `disconnect`，也不强制停止原桌面、切换 HOME 或修改 MAA 设置。root 开关保持用户原来的状态。

广告网络规则只针对原桌面和商店的应用身份。明日方舟和普通用户 App 不在名单内；增强截图和触控没有被替换或代理。

## 实际复查

MuMuPlayer 国际版 6.8.9.0 / Android 15，MAA 6.21.3：

- root 开启实例与 root 关闭测试实例均完成连接。
- 增强截图连续五次返回有效 PNG。
- 触控接口返回任务编号；此前对游戏弹窗的实际触控已验证响应。
- 恢复和重新应用广告规则后，连接仍可用。

完整作战、基建等任务没有逐项跑完，不能把接口检查写成所有 MAA 任务都已测试。去广告规则没有覆盖用户游戏的网络，也没有调整分辨率、显存策略、CPU、内存或默认帧率。

## 安装能力

MuMu 商店包、系统安装器、`XApkInstallerActivity`、`InstallLocalApkService` 和安装广播保持启用。只关闭四个广告推荐/追踪组件，不停用整个商店包。

`MuMu清爽助手.exe --maa-check` 可读取当前 ADB、设备、商店、安装器和广告组件状态；`--ad-audit` 可检查广告规则。命令输出只在本机生成，反馈问题前请自行隐去路径和设备信息。
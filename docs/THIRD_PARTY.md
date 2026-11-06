# 第三方组件

项目本身为 MIT 许可。Root 配置包不打包进 EXE，也不在本仓库重新分发；仅在选择可选 Root 功能时，从下列上游发布地址下载原文件，校验后使用。第三方项目各自遵守原许可。

## KitsuneMagisk

- 项目和源码：[Jordan231111/KitsuneMagisk](https://github.com/Jordan231111/KitsuneMagisk)
- 固定源码提交：[`5869bd7f9612e1f07e7ec07420afb8f11b868cf6`](https://github.com/Jordan231111/KitsuneMagisk/tree/5869bd7f9612e1f07e7ec07420afb8f11b868cf6)
- [固定发布](https://github.com/Jordan231111/KitsuneMagisk/releases/tag/kitsune-canary-5869bd7f9612e1f07e7ec07420afb8f11b868cf6)，使用 `app-release.apk`，版本码 31024。
- SHA-256：`f44cfa11c5de9a0dabf3dc8a516438ac9eec762a3178ca4a5f4fd772dcb516b7`
- 许可：[上游 GPL-3.0 许可与声明](https://github.com/Jordan231111/KitsuneMagisk/blob/5869bd7f9612e1f07e7ec07420afb8f11b868cf6/LICENSE)。基于 Magisk 与 KitsuneMagisk 的维护分支，感谢原作者和维护者。

选用其 System Mode 安装事务、原文件清单和回退接口，不复制第三方一键脚本。上游发布元数据将这条分支标记为 `next-system-experimental`，不宣传成所有设备通用的稳定方案。

## Vector

- 项目和源码：[JingMatrix/Vector](https://github.com/JingMatrix/Vector)
- [v2.0 发布与源码](https://github.com/JingMatrix/Vector/releases/tag/v2.0)，使用 `Vector-v2.0-3021-Release.zip`。
- SHA-256：`d5e39669c02c2c699ab948eb8f3639b348eefb7749553224a9c62fa4a2f2dc18`
- 许可：[上游 GPL-3.0 许可](https://github.com/JingMatrix/Vector/blob/v2.0/LICENSE)。感谢 Vector 与 LSPosed 的贡献者。

本轮 Android 12 样本中，Vector 2.2 出现系统广播接口不匹配，因此没有选用。2.0 已检查守护进程启动、管理器安装与回退；这不代表每个 Xposed 模块都兼容。

## Linux / Zig

身份过滤加载器源码在 `native/UidFilter.c`，使用 Linux UAPI 和系统调用接口，自有代码采用 MIT 许可。Zig 不随 EXE 分发。eBPF 程序的 `GPL` 标志用于声明内核 helper 兼容性，不把第三方 Root 二进制转为本项目 MIT 许可。

# Android 12 应用身份过滤加载器

`UidFilter.c` 是 MIT 许可的短时运行程序。它只为固定 UID 加载、核对或移除 6 条指令的 eBPF socket filter；配置工具退出后，过滤逻辑留在内核，不保留 Android 用户态进程。

5.30.2 的 Android 12 内核没有启用 `xt_owner`，因此用已有的 `xt_bpf --object-pinned` 选择 socket UID，再进入相同的推广域名规则链。Android 15 继续使用原来的 `xt_owner`。

使用 [Zig](https://ziglang.org/download/) 的 musl 工具链交叉编译：

```powershell
zig cc -target x86_64-linux-musl -static -Os -s native/UidFilter.c -o assets/UidFilter.x86_64
```

当前产物使用 Zig 0.17.1。不要按模拟器伪装的手机架构选择目标；这个版本的实际系统 ELF 为 x86_64。

允许的 UID 为系统商店使用的 1000 或 10000–19999 中明确读取到的桌面/商店 UID。加载器不自行枚举 App，传入 UID 由 `GuestAdPolicy` 选择。只使用本工具命名的 BPF pin，已有 pin 必须匹配预期程序类型、名称和字节码标签；不修改其他 BPF 对象或全局网络链。

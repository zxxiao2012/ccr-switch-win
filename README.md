# CCR Switch (Windows 版)

[macOS 版](../ccr-switch) 的 Windows 移植：C# WinForms 系统托盘应用，双栈管理 **ccr (claude-code-router v1.0.73)** 供应商与 **Claude Code 直连**配置。核心逻辑（`CCRSwitch.Core`）跨平台，可在 macOS/Linux 上运行全部单元测试。

## 功能（与 mac 版对齐）

- **托盘常驻**：⚡ 图标，左键弹出管理面板，右键菜单快速切换（default 子菜单按供应商→模型分组、直连子菜单、重启/停止/Web UI）
- **CCR 栈**：`%USERPROFILE%\.claude-code-router\config.json` 的 Providers 增删改、Router 四槽位（default/think/longContext/background）切换、/health 状态监控、改配置自动 `ccr restart`
- **直连栈**：`%USERPROFILE%\.claude\settings.json` 的 env 覆写（其余键保真）、首次启动自动导入当前配置、编辑"当前生效"项时从活文件回填
- **安全网**：写前自动备份（`config.json.bak-app-*` / `settings.json.bak-ccrswitch-*` 各留 10 份）、原子写
- ccr 调用自动解析 `%APPDATA%\npm\ccr.cmd`（npm 全局安装），GUI 进程 PATH 自动补全

## 产物

| 文件 | 大小 | 说明 |
|---|---|---|
| `publish/CCR-Switch-1.0.0-win64.exe` | ~110MB | 自包含单文件，Windows 10/11 x64 **免装运行时** |
| `publish/CCR-Switch-1.0.0-win64-small.exe` | ~300KB | 框架依赖版，目标机需 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

## 构建（在 mac 上交叉编译）

```bash
# 一次性：安装 .NET SDK（免 sudo）
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel LTS

export DOTNET="$HOME/.dotnet/dotnet"
./scripts/build-win.sh          # 生成 publish/ 下两个 exe（含图标）
swift scripts/make-icon.swift CCRSwitch/app.ico   # 需要时重新生成图标
```

## 测试（在 mac 上跑 Core 全套逻辑）

```bash
~/.dotnet/dotnet run --project CCRSwitch.Tests             # 10 项单元测试
~/.dotnet/dotnet run --project CCRSwitch.Tests -- --selftest       # 真实环境只读自检
~/.dotnet/dotnet run --project CCRSwitch.Tests -- --selftest-live  # 真实往返切换 + ccr restart + /health
```

## Windows 首次运行

1. 拷贝 exe 到 Windows 机器（自包含版直接可用）
2. 若 SmartScreen 拦截：右键 → 属性 → 勾选"解除锁定"（无签名证书的正常现象）
3. 前置：`npm install -g @musistudio/claude-code-router` 并完成 `~/.claude-code-router/config.json` 配置
4. 验证 CLI 自检：`CCR-Switch.exe --selftest`（只读）→ 托盘出图标
5. CLI 也可直接执行真实切换自检：`CCR-Switch.exe --selftest-live`（切走再切回，最终状态不变）

## 已知限制

- ccr 的 config.json 允许 JSON5 注释；本版写入标准 JSON（与 ccr 自身行为一致，**注释会丢**）。读取端有尽力而为的 JSON5 清洗兜底（注释/尾逗号/裸键名/单引号），极端写法不支持
- GUI 无法在 mac 上运行实测，已在 mac 上完成：Core 全部逻辑测试 + 真机 live 切换链路 + WinForms 壳交叉编译验证。Windows 上的 UI 渲染请按上面"首次运行"冒烟
- 未做代码签名与开机自启（后续可加 `HKCU\...\Run` 注册表项或计划任务）

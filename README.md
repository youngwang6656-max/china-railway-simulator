# RailSim

中国铁路建设与调度模拟器。当前仓库只包含第一个基础子项目：
Godot Windows 启动工程、纯 C# 确定性仿真时钟、tick 边界命令总线、
有序仿真宿主和只读快照。

## Prerequisites

- Windows 11 64 位
- PowerShell 5.1 或更高
- 网络仅用于首次下载官方 .NET 8 SDK 与 Godot 4.6.3 .NET

## Bootstrap

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap-toolchain.ps1
```

工具安装在仓库的 `.tools/` 中，不写入 Git。

## Verify

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
```

成功标志为 `VERIFY_OK`，并且 Godot 输出包含 `RAILSIM_SMOKE tick=4`。

## Architecture

- `src/RailSim.Core`：不依赖 Godot 的确定性仿真核心。
- `game`：Godot 4.6.3 .NET 显示与输入适配层。
- `tests/RailSim.Core.Tests`：核心行为的 xUnit 测试。
- `docs/superpowers/specs`：已批准的产品设计规格。
- `docs/superpowers/plans`：按子项目拆分的实施计划。

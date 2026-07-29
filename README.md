# RailSim 国铁模拟器

一个面向中国铁路建设与调度的虚拟模拟器。当前里程碑已经提供可玩的
Godot 2D 宏观地图编辑器，以及与界面解耦的纯 C# 确定性领域核心。

## 当前可用能力

- 由种子确定性生成约 `1000 × 800 km` 的大区级世界。
- 显示量化地形、主要河流、64 个四级城市与 64 个独立车站锚点。
- 地图平移、缩放与区域/线路/编辑三级 LOD。
- 一笔绘制上下行两股独立物理轨道，控制点自动平滑并可拖动。
- 将联络线吸附到既有节点，并循环选择明确的双向互通方案。
- 示例 A—C—D—B 双线、线路二、D 附近双向联络线。
- 普通几何交叉默认不互通；只有显式连接才改变行车拓扑。
- 原子编辑、撤销/重做、带版本和内容哈希的保存/读取。
- 64 位毫米世界坐标、地理与铁路分块空间索引、城市—车站一对多关系和
  大陆级配置端口。
- 可替换的地形来源/生成/编辑、城市布置、命名、宏观投影、分块存储/
  流式加载和编辑历史接口。
- 可直接使用的原子文件区块存储；远距区块能独立卸载、读取并保持稳定 ID。

详细站场、信号、列车动力学、时刻表和调度台属于后续里程碑；现有稳定
ID、物理长度、方向连接、详细区引用与扩展接口会作为其接入基础。

## 环境

- Windows 11 64 位
- PowerShell 5.1 或更高
- .NET SDK 8.x
- Godot 4.6.3 stable .NET（Mono）

初始化脚本会优先复用 `%USERPROFILE%\.dotnet` 中已有的 .NET 8，并只在
缺少工具时下载到仓库忽略的 `.tools/`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap-toolchain.ps1
```

## 启动

推荐从 PowerShell 启动。脚本会使用仓库内的 Godot 4.6.3 .NET，并把日志写入
可访问的绝对路径，从而避免 Windows 上的日志目录启动错误：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1
```

也可以在 Godot 4.6.3 .NET 中打开 `game/project.godot` 后按 F6/F5。

进入地图后会自动创建完整示例。工具栏可新建世界、绘制双线或联络线、
切换明确互通方案、追加示例线路、撤销/重做及保存/读取。

操作方式：

- 鼠标滚轮：缩放。
- 鼠标中键或右键拖动：平移。
- “绘制双线”模式下左键：添加控制点；拖动已有橙色点可调整。
- “绘制联络线”模式：两端吸附既有节点；“切换互通方案”选择要开放的
  四个明确行车方向。
- “查看”模式左键：选择城市、车站或铁路节点。
- Enter 或双击：提交线路；Esc：取消草案。
- 出现半径、坡度、河流穿越或线间距警告时，再次 Enter/双击才会确认。
- “保存/读取”：使用 Godot 用户目录中的
  `regional-world.crsim-world.json`。

## 验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
```

脚本离线友好地执行格式、Release/Debug 构建、115 项以上测试、75% 覆盖率门，
并真正实例化地图 UI 完成三帧 Godot 无界面冒烟。成功结尾应包含：

```text
GODOT_SMOKE_OK tick=4
GODOT_WORLD_SMOKE_OK cities=64 stations=64 tracks=12
GODOT_HISTORY_SMOKE_OK rebuild=True
GODOT_SNAPSHOT_SMOKE_OK published=True
GODOT_PERSISTENCE_SMOKE_OK roundtrip=True
GODOT_HOST_COMMAND_SMOKE_OK dispatched=True
GODOT_REPLACEMENT_SMOKE_OK current=True
GODOT_MANUAL_WARNING_SMOKE_OK confirmed=True
GODOT_MANUAL_CONNECTOR_SMOKE_OK built=True
VERIFY_OK
```

## 架构

- `src/RailSim.Core`：不依赖 Godot 的世界、铁路拓扑、编辑、存储与投影核心。
- `game`：Godot 4.6.3 .NET 显示、输入与应用控制器。
- `tests/RailSim.Core.Tests`：确定性和领域契约的 xUnit 测试。
- `docs/superpowers/specs`：已批准的产品与里程碑设计规格。
- `docs/superpowers/plans`：可复核的分步实施计划。

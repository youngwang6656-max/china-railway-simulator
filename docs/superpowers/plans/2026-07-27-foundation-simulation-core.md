# Foundation and Deterministic Simulation Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可启动的 Godot Windows 工程，以及不依赖 Godot 场景树的纯 C# 确定性时钟、命令总线、仿真宿主、只读快照和自动化测试基础。

**Architecture:** `RailSim.Core` 是无 Godot 依赖的 `net8.0` 类库，所有时间推进、命令排序和状态查询都在该层完成。`RailSim.App` 是 Godot 4.6.3 .NET 适配层，只把真实帧时间交给 `SimulationHost`，读取 `ISimulationQuery` 快照并显示；后续铁路拓扑、联锁和调度系统通过 `ISimulationSystem`、`ICommandHandler<T>` 与 `ISnapshotContributor` 接入。

**Tech Stack:** Godot 4.6.3 .NET、C# 12、.NET 8、xUnit 2.9.2、Microsoft.NET.Test.Sdk 17.11.1、coverlet.collector 6.0.2、PowerShell 5.1+

## Global Constraints

- 首发平台固定为 Windows 桌面版；本计划不增加 Web 导出。
- Godot 固定在 4.6.x 系列并以 4.6.3 开始；不得自动升级到 4.7。
- 所有核心项目目标框架固定为 `net8.0`；启用 nullable 和 warnings-as-errors。
- `RailSim.Core` 不得引用 Godot、渲染、UI 或操作系统 API。
- 固定仿真步长为 0.25 秒；支持暂停、0.25×、0.5×、1×、2×、4×、8×、16×、32×和 64×。
- 相同真实时间片、倍率变化和命令序列必须得到相同 tick 与命令执行顺序。
- 命令只在 tick 边界执行；显示层不得直接修改核心状态。
- 本子项目不实现铁路拓扑、列车、信号、时刻表或专业 CTC，只提供它们后续接入的稳定端口。
- 开发机当前只有 .NET 8 Runtime，没有 .NET SDK 和 Godot；首次执行需在获得网络下载许可后运行项目内工具链脚本。
- 所有新行为先写失败测试，再写最小实现；每个任务结束后单独提交。

---

## File Structure

```text
.
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── RailSim.sln
├── README.md
├── game/
│   ├── App/
│   │   └── Bootstrap.cs
│   ├── Main.tscn
│   ├── RailSim.App.csproj
│   └── project.godot
├── scripts/
│   ├── bootstrap-toolchain.ps1
│   └── verify.ps1
├── src/
│   └── RailSim.Core/
│       ├── Commands/
│       │   ├── CommandContracts.cs
│       │   └── SimulationCommandBus.cs
│       ├── Hosting/
│       │   ├── ISimulationSystem.cs
│       │   ├── SimulationHost.cs
│       │   └── SimulationStepContext.cs
│       ├── Query/
│       │   ├── SimulationSnapshot.cs
│       │   └── SnapshotStore.cs
│       ├── Time/
│       │   ├── SimulationClock.cs
│       │   ├── SimulationRate.cs
│       │   └── SimulationTick.cs
│       └── RailSim.Core.csproj
└── tests/
    └── RailSim.Core.Tests/
        ├── Commands/
        │   └── SimulationCommandBusTests.cs
        ├── Hosting/
        │   └── SimulationHostTests.cs
        ├── Query/
        │   └── SnapshotStoreTests.cs
        ├── Time/
        │   └── SimulationClockTests.cs
        └── RailSim.Core.Tests.csproj
```

`.tools/`、`bin/`、`obj/`、`.godot/` 和测试输出只存在于本地并由 `.gitignore` 排除。

---

### Task 1: Reproducible Toolchain and Bootable Solution

**Files:**
- Create: `.gitignore`
- Create: `.editorconfig`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `scripts/bootstrap-toolchain.ps1`
- Create: `RailSim.sln`
- Create: `src/RailSim.Core/RailSim.Core.csproj`
- Create: `game/RailSim.App.csproj`
- Create: `game/project.godot`
- Create: `game/Main.tscn`
- Create: `game/App/Bootstrap.cs`
- Create: `tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj`

**Interfaces:**
- Consumes: official .NET 8 installer endpoint and official Godot 4.6.3 .NET release archive after user approves network access.
- Produces: local `.tools/dotnet/dotnet.exe`, local Godot .NET executable, buildable `RailSim.sln`, bootable Godot project, and an initially empty xUnit suite.

- [ ] **Step 1: Run the preflight and record the expected missing tools**

Run:

```powershell
dotnet --list-sdks
Get-Command godot,godot4 -ErrorAction SilentlyContinue
```

Expected before setup: no .NET SDK rows and no Godot command. Existing .NET Runtime rows do not satisfy this task.

- [ ] **Step 2: Create the project-local toolchain bootstrap**

Create `scripts/bootstrap-toolchain.ps1` with:

```powershell
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$dotnetRoot = Join-Path $toolsRoot 'dotnet'
$godotRoot = Join-Path $toolsRoot 'godot'
$downloadRoot = Join-Path $toolsRoot 'downloads'

New-Item -ItemType Directory -Force -Path $dotnetRoot, $godotRoot, $downloadRoot | Out-Null

$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
if (-not (Test-Path $dotnetExe)) {
    $installer = Join-Path $downloadRoot 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel '8.0' `
        -Quality 'GA' `
        -InstallDir $dotnetRoot `
        -NoPath
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK installation failed with exit code $LASTEXITCODE"
    }
}

$godotExe = Get-ChildItem -Path $godotRoot -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
    Select-Object -First 1
if ($null -eq $godotExe) {
    $archive = Join-Path $downloadRoot 'godot-4.6.3-dotnet-win64.zip'
    Invoke-WebRequest `
        'https://github.com/godotengine/godot/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_win64.zip' `
        -OutFile $archive
    Expand-Archive -Path $archive -DestinationPath $godotRoot -Force
    $godotExe = Get-ChildItem -Path $godotRoot -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
        Select-Object -First 1
}

if (-not (Test-Path $dotnetExe)) {
    throw "Missing local .NET SDK at $dotnetExe"
}
if ($null -eq $godotExe) {
    throw "Missing Godot 4.6.3 .NET executable under $godotRoot"
}

& $dotnetExe --info
if ($LASTEXITCODE -ne 0) {
    throw "Local dotnet --info failed"
}

& $godotExe.FullName --version
if ($LASTEXITCODE -ne 0) {
    throw "Godot --version failed"
}
```

Run after obtaining download approval:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap-toolchain.ps1
```

Expected: `.tools/dotnet/dotnet.exe --list-sdks` prints one .NET 8 SDK and the Godot executable prints a `4.6.3` version.

- [ ] **Step 3: Add repository-wide build configuration**

Create `.gitignore`:

```gitignore
.tools/
.godot/
.vs/
.idea/
bin/
obj/
TestResults/
artifacts/
*.user
*.suo
```

Create `global.json`:

```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
</Project>
```

Create `.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4
dotnet_sort_system_directives_first = true
csharp_new_line_before_open_brace = all
dotnet_diagnostic.IDE0005.severity = warning
```

- [ ] **Step 4: Scaffold the solution and project references**

Run with the local SDK:

```powershell
$dotnet = '.\.tools\dotnet\dotnet.exe'
& $dotnet new sln --name RailSim
& $dotnet new classlib --name RailSim.Core --output src/RailSim.Core --framework net8.0
& $dotnet new xunit --name RailSim.Core.Tests --output tests/RailSim.Core.Tests --framework net8.0
& $dotnet sln RailSim.sln add src/RailSim.Core/RailSim.Core.csproj
& $dotnet sln RailSim.sln add tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj
& $dotnet add tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj reference src/RailSim.Core/RailSim.Core.csproj
```

Delete the generated `src/RailSim.Core/Class1.cs` and `tests/RailSim.Core.Tests/UnitTest1.cs` with an explicit patch.

Replace `src/RailSim.Core/RailSim.Core.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>RailSim.Core</RootNamespace>
    <AssemblyName>RailSim.Core</AssemblyName>
  </PropertyGroup>
</Project>
```

Replace `tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>RailSim.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RailSim.Core\RailSim.Core.csproj" />
  </ItemGroup>
</Project>
```

Create `game/RailSim.App.csproj`:

```xml
<Project Sdk="Godot.NET.Sdk/4.6.3">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>RailSim.App</RootNamespace>
    <AssemblyName>RailSim.App</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\RailSim.Core\RailSim.Core.csproj" />
  </ItemGroup>
</Project>
```

Then add it to the solution:

```powershell
.\.tools\dotnet\dotnet.exe sln RailSim.sln add game/RailSim.App.csproj
```

- [ ] **Step 5: Create the minimum Godot boot scene**

Create `game/project.godot`:

```ini
; Engine configuration file.
; Edit through reviewed source changes only.

config_version=5

[application]

config/name="RailSim"
run/main_scene="res://Main.tscn"

[display]

window/size/viewport_width=1280
window/size/viewport_height=720
window/size/window_width_override=1280
window/size/window_height_override=720
window/stretch/mode="canvas_items"

[dotnet]

project/assembly_name="RailSim.App"

[rendering]

renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
environment/defaults/default_clear_color=Color(0.035, 0.043, 0.055, 1)
```

Create `game/Main.tscn`:

```ini
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://App/Bootstrap.cs" id="1_boot"]

[node name="RailSim" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_boot")

[node name="Status" type="Label" parent="."]
layout_mode = 0
offset_left = 24.0
offset_top = 24.0
offset_right = 500.0
offset_bottom = 56.0
text = "RailSim foundation booted"
```

Create `game/App/Bootstrap.cs`:

```csharp
using Godot;

namespace RailSim.App;

public partial class Bootstrap : Control
{
    public override void _Ready()
    {
        GD.Print("RAILSIM_BOOT");

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            GetTree().Quit(0);
        }
    }
}
```

- [ ] **Step 6: Verify the empty solution and Godot boot**

Run:

```powershell
$dotnet = '.\.tools\dotnet\dotnet.exe'
$godot = Get-ChildItem '.\.tools\godot' -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
    Select-Object -First 1 -ExpandProperty FullName
& $dotnet restore RailSim.sln
& $dotnet build RailSim.sln --configuration Release --no-restore
& $dotnet test RailSim.sln --configuration Release --no-build
& $godot --headless --path game -- --smoke-test
```

Expected: restore/build/test exit 0; test output reports zero failed tests; Godot output contains `RAILSIM_BOOT` and exits 0.

- [ ] **Step 7: Commit the toolchain and bootable skeleton**

```powershell
git add .gitignore .editorconfig global.json Directory.Build.props Directory.Packages.props RailSim.sln scripts game src tests
git commit -m "build: scaffold Godot and .NET solution"
```

---

### Task 2: Deterministic Fixed-Step Clock

**Files:**
- Create: `src/RailSim.Core/Time/SimulationTick.cs`
- Create: `src/RailSim.Core/Time/SimulationRate.cs`
- Create: `src/RailSim.Core/Time/SimulationClock.cs`
- Create: `tests/RailSim.Core.Tests/Time/SimulationClockTests.cs`

**Interfaces:**
- Consumes: `net8.0` base class library only.
- Produces: `SimulationTick`, `SimulationSpeedPreset`, `SimulationRate.FromPreset(...)`, `SimulationClock.Accumulate(...)`, `SimulationClock.AdvanceOne()`.

- [ ] **Step 1: Write failing tests for every rate and frame partition**

Create `tests/RailSim.Core.Tests/Time/SimulationClockTests.cs`:

```csharp
using RailSim.Core.Time;

namespace RailSim.Core.Tests.Time;

public sealed class SimulationClockTests
{
    [Theory]
    [InlineData(SimulationSpeedPreset.Paused, 0)]
    [InlineData(SimulationSpeedPreset.Quarter, 1)]
    [InlineData(SimulationSpeedPreset.Half, 2)]
    [InlineData(SimulationSpeedPreset.Normal, 4)]
    [InlineData(SimulationSpeedPreset.X2, 8)]
    [InlineData(SimulationSpeedPreset.X4, 16)]
    [InlineData(SimulationSpeedPreset.X8, 32)]
    [InlineData(SimulationSpeedPreset.X16, 64)]
    [InlineData(SimulationSpeedPreset.X32, 128)]
    [InlineData(SimulationSpeedPreset.X64, 256)]
    public void OneRealSecondProducesExpectedSteps(
        SimulationSpeedPreset preset,
        int expectedSteps)
    {
        var clock = new SimulationClock();

        var steps = clock.Accumulate(
            TimeSpan.FromSeconds(1),
            SimulationRate.FromPreset(preset));

        Assert.Equal(expectedSteps, steps);
    }

    [Fact]
    public void TenSmallFramesEqualOneLargeFrame()
    {
        var splitClock = new SimulationClock();
        var wholeClock = new SimulationClock();
        var rate = SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

        var splitSteps = 0;
        for (var index = 0; index < 10; index++)
        {
            splitSteps += splitClock.Accumulate(TimeSpan.FromMilliseconds(100), rate);
        }

        var wholeSteps = wholeClock.Accumulate(TimeSpan.FromSeconds(1), rate);

        Assert.Equal(wholeSteps, splitSteps);
        Assert.Equal(4, splitSteps);
    }

    [Fact]
    public void ChangingRatePreservesSubStepRemainder()
    {
        var clock = new SimulationClock();

        var beforeChange = clock.Accumulate(
            TimeSpan.FromMilliseconds(500),
            SimulationRate.FromPreset(SimulationSpeedPreset.Quarter));
        var afterChange = clock.Accumulate(
            TimeSpan.FromMilliseconds(125),
            SimulationRate.FromPreset(SimulationSpeedPreset.Normal));

        Assert.Equal(0, beforeChange);
        Assert.Equal(1, afterChange);
    }

    [Fact]
    public void AdvanceOneIncrementsTickExactlyOnce()
    {
        var clock = new SimulationClock();

        var first = clock.AdvanceOne();
        var second = clock.AdvanceOne();

        Assert.Equal(new SimulationTick(1), first);
        Assert.Equal(new SimulationTick(2), second);
        Assert.Equal(second, clock.Current);
    }

    [Fact]
    public void NegativeElapsedTimeIsRejected()
    {
        var clock = new SimulationClock();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.Accumulate(
                TimeSpan.FromMilliseconds(-1),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));

        Assert.Equal("realElapsed", exception.ParamName);
    }
}
```

- [ ] **Step 2: Run the clock tests and verify the expected compile failure**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SimulationClockTests
```

Expected: FAIL because namespace `RailSim.Core.Time` and clock types do not exist.

- [ ] **Step 3: Implement the time value types and fixed-step accumulator**

Create `src/RailSim.Core/Time/SimulationTick.cs`:

```csharp
namespace RailSim.Core.Time;

public readonly record struct SimulationTick(long Value)
{
    public static SimulationTick Zero { get; } = new(0);

    public SimulationTick Next()
    {
        return new SimulationTick(checked(Value + 1));
    }
}
```

Create `src/RailSim.Core/Time/SimulationRate.cs`:

```csharp
namespace RailSim.Core.Time;

public enum SimulationSpeedPreset
{
    Paused,
    Quarter,
    Half,
    Normal,
    X2,
    X4,
    X8,
    X16,
    X32,
    X64
}

public readonly record struct SimulationRate
{
    private SimulationRate(SimulationSpeedPreset preset, int quarterUnits)
    {
        Preset = preset;
        QuarterUnits = quarterUnits;
    }

    public SimulationSpeedPreset Preset { get; }

    public int QuarterUnits { get; }

    public bool IsPaused => QuarterUnits == 0;

    public static SimulationRate FromPreset(SimulationSpeedPreset preset)
    {
        return preset switch
        {
            SimulationSpeedPreset.Paused => new(preset, 0),
            SimulationSpeedPreset.Quarter => new(preset, 1),
            SimulationSpeedPreset.Half => new(preset, 2),
            SimulationSpeedPreset.Normal => new(preset, 4),
            SimulationSpeedPreset.X2 => new(preset, 8),
            SimulationSpeedPreset.X4 => new(preset, 16),
            SimulationSpeedPreset.X8 => new(preset, 32),
            SimulationSpeedPreset.X16 => new(preset, 64),
            SimulationSpeedPreset.X32 => new(preset, 128),
            SimulationSpeedPreset.X64 => new(preset, 256),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };
    }
}
```

Create `src/RailSim.Core/Time/SimulationClock.cs`:

```csharp
namespace RailSim.Core.Time;

public sealed class SimulationClock
{
    private const int QuarterUnitsPerNormalRate = 4;
    private long _quarterScaledRemainder;

    public static TimeSpan StepDuration { get; } =
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 4);

    public SimulationTick Current { get; private set; } = SimulationTick.Zero;

    public int Accumulate(TimeSpan realElapsed, SimulationRate rate)
    {
        if (realElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(realElapsed),
                realElapsed,
                "Elapsed time cannot be negative.");
        }

        var scaledElapsed = checked(realElapsed.Ticks * rate.QuarterUnits);
        var available = checked(_quarterScaledRemainder + scaledElapsed);
        var scaledStep = checked(StepDuration.Ticks * QuarterUnitsPerNormalRate);
        var dueSteps = available / scaledStep;

        if (dueSteps > int.MaxValue)
        {
            throw new InvalidOperationException(
                "A single frame produced more simulation steps than can be processed.");
        }

        _quarterScaledRemainder = available % scaledStep;
        return (int)dueSteps;
    }

    public SimulationTick AdvanceOne()
    {
        Current = Current.Next();
        return Current;
    }
}
```

- [ ] **Step 4: Run the focused and full tests**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SimulationClockTests
.\.tools\dotnet\dotnet.exe test RailSim.sln --configuration Release
```

Expected: all clock tests pass; the full suite reports zero failures.

- [ ] **Step 5: Commit the deterministic clock**

```powershell
git add src/RailSim.Core/Time tests/RailSim.Core.Tests/Time
git commit -m "feat: add deterministic simulation clock"
```

---

### Task 3: Tick-Boundary Command Bus

**Files:**
- Create: `src/RailSim.Core/Commands/CommandContracts.cs`
- Create: `src/RailSim.Core/Commands/SimulationCommandBus.cs`
- Create: `tests/RailSim.Core.Tests/Commands/SimulationCommandBusTests.cs`

**Interfaces:**
- Consumes: `SimulationTick`.
- Produces: `ISimulationCommand`, `CommandEnvelope`, `CommandResult`, `CommandDecision`, `ICommandHandler`, `CommandHandler<TCommand>`, and `SimulationCommandBus`.

- [ ] **Step 1: Write failing tests for order, future scheduling, and rejection**

Create `tests/RailSim.Core.Tests/Commands/SimulationCommandBusTests.cs`:

```csharp
using RailSim.Core.Commands;
using RailSim.Core.Time;

namespace RailSim.Core.Tests.Commands;

public sealed class SimulationCommandBusTests
{
    [Fact]
    public void CommandsAtSameTickRunInEnqueueOrder()
    {
        var values = new List<string>();
        var bus = new SimulationCommandBus();
        bus.Register(new AppendHandler(values));

        bus.Enqueue(new AppendCommand("A"), new SimulationTick(2));
        bus.Enqueue(new AppendCommand("B"), new SimulationTick(2));
        bus.Enqueue(new AppendCommand("C"), new SimulationTick(2));

        var results = bus.DispatchDue(new SimulationTick(2));

        Assert.Equal(new[] { "A", "B", "C" }, values);
        Assert.All(results, result => Assert.Equal(CommandStatus.Accepted, result.Status));
        Assert.Equal(new long[] { 1, 2, 3 }, results.Select(result => result.Sequence));
    }

    [Fact]
    public void FutureCommandRemainsQueued()
    {
        var values = new List<string>();
        var bus = new SimulationCommandBus();
        bus.Register(new AppendHandler(values));
        bus.Enqueue(new AppendCommand("future"), new SimulationTick(4));

        var early = bus.DispatchDue(new SimulationTick(3));
        var onTime = bus.DispatchDue(new SimulationTick(4));

        Assert.Empty(early);
        Assert.Single(onTime);
        Assert.Equal(new[] { "future" }, values);
    }

    [Fact]
    public void MissingHandlerReturnsStableRejection()
    {
        var bus = new SimulationCommandBus();
        var envelope = bus.Enqueue(new UnknownCommand(), new SimulationTick(1));

        var result = Assert.Single(bus.DispatchDue(new SimulationTick(1)));

        Assert.Equal(envelope.Sequence, result.Sequence);
        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal("handler.missing", result.Code);
    }

    [Fact]
    public void DuplicateHandlerRegistrationIsRejected()
    {
        var bus = new SimulationCommandBus();
        bus.Register(new AppendHandler(new List<string>()));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            bus.Register(new AppendHandler(new List<string>())));

        Assert.Contains(typeof(AppendCommand).FullName!, exception.Message);
    }

    [Fact]
    public void ExportLogReturnsIndependentSequenceOrderedCopy()
    {
        var bus = new SimulationCommandBus();
        bus.Enqueue(new UnknownCommand(), new SimulationTick(3));
        bus.Enqueue(new UnknownCommand(), new SimulationTick(1));

        var log = bus.ExportLog();

        Assert.Equal(new long[] { 1, 2 }, log.Select(item => item.Sequence));
        Assert.NotSame(log, bus.ExportLog());
    }

    private sealed record AppendCommand(string Value) : ISimulationCommand;

    private sealed record UnknownCommand : ISimulationCommand;

    private sealed class AppendHandler(List<string> values)
        : CommandHandler<AppendCommand>
    {
        protected override CommandDecision Handle(
            AppendCommand command,
            SimulationTick tick)
        {
            values.Add(command.Value);
            return CommandDecision.Accept();
        }
    }
}
```

- [ ] **Step 2: Run the command tests and verify the expected compile failure**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SimulationCommandBusTests
```

Expected: FAIL because `RailSim.Core.Commands` does not exist.

- [ ] **Step 3: Implement command contracts**

Create `src/RailSim.Core/Commands/CommandContracts.cs`:

```csharp
using RailSim.Core.Time;

namespace RailSim.Core.Commands;

public interface ISimulationCommand;

public enum CommandStatus
{
    Accepted,
    Rejected
}

public sealed record CommandDecision(
    CommandStatus Status,
    string Code,
    string Message)
{
    public static CommandDecision Accept()
    {
        return new(CommandStatus.Accepted, "accepted", string.Empty);
    }

    public static CommandDecision Reject(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(CommandStatus.Rejected, code, message);
    }
}

public sealed record CommandEnvelope(
    long Sequence,
    SimulationTick ExecuteAt,
    ISimulationCommand Payload);

public sealed record CommandResult(
    long Sequence,
    SimulationTick ExecutedAt,
    CommandStatus Status,
    string Code,
    string Message);

public interface ICommandHandler
{
    Type CommandType { get; }

    CommandDecision Handle(
        ISimulationCommand command,
        SimulationTick tick);
}

public abstract class CommandHandler<TCommand> : ICommandHandler
    where TCommand : ISimulationCommand
{
    public Type CommandType => typeof(TCommand);

    public CommandDecision Handle(
        ISimulationCommand command,
        SimulationTick tick)
    {
        if (command is not TCommand typedCommand)
        {
            return CommandDecision.Reject(
                "handler.type_mismatch",
                $"Handler for {typeof(TCommand).FullName} received {command.GetType().FullName}.");
        }

        return Handle(typedCommand, tick);
    }

    protected abstract CommandDecision Handle(
        TCommand command,
        SimulationTick tick);
}
```

- [ ] **Step 4: Implement the deterministic command queue**

Create `src/RailSim.Core/Commands/SimulationCommandBus.cs`:

```csharp
using RailSim.Core.Time;

namespace RailSim.Core.Commands;

public sealed class SimulationCommandBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, ICommandHandler> _handlers = new();
    private readonly List<CommandEnvelope> _scheduled = new();
    private readonly List<CommandEnvelope> _log = new();
    private long _nextSequence;

    public void Register(ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryAdd(handler.CommandType, handler))
            {
                throw new InvalidOperationException(
                    $"A handler is already registered for {handler.CommandType.FullName}.");
            }
        }
    }

    public CommandEnvelope Enqueue(
        ISimulationCommand command,
        SimulationTick executeAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (executeAt.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executeAt),
                executeAt,
                "Commands must execute at tick 1 or later.");
        }

        lock (_gate)
        {
            var envelope = new CommandEnvelope(
                checked(++_nextSequence),
                executeAt,
                command);
            _scheduled.Add(envelope);
            _log.Add(envelope);
            return envelope;
        }
    }

    public IReadOnlyList<CommandResult> DispatchDue(SimulationTick tick)
    {
        CommandEnvelope[] due;

        lock (_gate)
        {
            due = _scheduled
                .Where(item => item.ExecuteAt.Value <= tick.Value)
                .OrderBy(item => item.ExecuteAt.Value)
                .ThenBy(item => item.Sequence)
                .ToArray();

            var dueIds = due
                .Select(item => item.Sequence)
                .ToHashSet();
            _scheduled.RemoveAll(item => dueIds.Contains(item.Sequence));
        }

        var results = new List<CommandResult>(due.Length);
        foreach (var envelope in due)
        {
            ICommandHandler? handler;
            lock (_gate)
            {
                _handlers.TryGetValue(envelope.Payload.GetType(), out handler);
            }

            var decision = handler is null
                ? CommandDecision.Reject(
                    "handler.missing",
                    $"No handler is registered for {envelope.Payload.GetType().FullName}.")
                : handler.Handle(envelope.Payload, tick);

            results.Add(new CommandResult(
                envelope.Sequence,
                tick,
                decision.Status,
                decision.Code,
                decision.Message));
        }

        return results;
    }

    public IReadOnlyList<CommandEnvelope> ExportLog()
    {
        lock (_gate)
        {
            return _log
                .OrderBy(item => item.Sequence)
                .ToArray();
        }
    }
}
```

- [ ] **Step 5: Run focused tests and the full suite**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SimulationCommandBusTests
.\.tools\dotnet\dotnet.exe test RailSim.sln --configuration Release
```

Expected: all command tests and all existing clock tests pass.

- [ ] **Step 6: Commit the command bus**

```powershell
git add src/RailSim.Core/Commands tests/RailSim.Core.Tests/Commands
git commit -m "feat: add tick-boundary command bus"
```

---

### Task 4: Ordered Simulation Host

**Files:**
- Create: `src/RailSim.Core/Hosting/ISimulationSystem.cs`
- Create: `src/RailSim.Core/Hosting/SimulationStepContext.cs`
- Create: `src/RailSim.Core/Hosting/SimulationHost.cs`
- Create: `tests/RailSim.Core.Tests/Hosting/SimulationHostTests.cs`

**Interfaces:**
- Consumes: `SimulationClock`, `SimulationRate`, `SimulationCommandBus`, `CommandResult`.
- Produces: `ISimulationSystem`, `SimulationStepContext`, `FrameAdvanceResult`, `SimulationHost.Submit(...)`, `SimulationHost.AdvanceFrame(...)`.

- [ ] **Step 1: Write failing host order and determinism tests**

Create `tests/RailSim.Core.Tests/Hosting/SimulationHostTests.cs`:

```csharp
using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Time;

namespace RailSim.Core.Tests.Hosting;

public sealed class SimulationHostTests
{
    [Fact]
    public void CommandRunsBeforeOrderedSystemsAtNextTick()
    {
        var events = new List<string>();
        var clock = new SimulationClock();
        var bus = new SimulationCommandBus();
        bus.Register(new RecordHandler(events));
        var host = new SimulationHost(
            clock,
            bus,
            new ISimulationSystem[]
            {
                new RecordingSystem(20, "second", events),
                new RecordingSystem(10, "first", events)
            });

        host.Submit(new RecordCommand("command"));
        var result = host.AdvanceFrame(
            TimeSpan.FromMilliseconds(250),
            SimulationRate.FromPreset(SimulationSpeedPreset.Normal));

        Assert.Equal(1, result.StepsProcessed);
        Assert.Equal(new SimulationTick(1), result.CurrentTick);
        Assert.Equal(
            new[] { "command@1", "first@1", "second@1" },
            events);
    }

    [Fact]
    public void FramePartitionDoesNotChangeTickTrace()
    {
        var splitTrace = RunTrace(Enumerable.Repeat(
            TimeSpan.FromMilliseconds(100),
            10));
        var wholeTrace = RunTrace(new[] { TimeSpan.FromSeconds(1) });

        Assert.Equal(wholeTrace, splitTrace);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, wholeTrace);
    }

    [Fact]
    public void DuplicateSystemOrderIsRejected()
    {
        var events = new List<string>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SimulationHost(
                new SimulationClock(),
                new SimulationCommandBus(),
                new ISimulationSystem[]
                {
                    new RecordingSystem(10, "A", events),
                    new RecordingSystem(10, "B", events)
                }));

        Assert.Contains("10", exception.Message);
    }

    [Fact]
    public void PausedFrameDoesNotRunCommandsOrSystems()
    {
        var events = new List<string>();
        var bus = new SimulationCommandBus();
        bus.Register(new RecordHandler(events));
        var host = new SimulationHost(
            new SimulationClock(),
            bus,
            new[] { new RecordingSystem(10, "system", events) });
        host.Submit(new RecordCommand("command"));

        var result = host.AdvanceFrame(
            TimeSpan.FromSeconds(10),
            SimulationRate.FromPreset(SimulationSpeedPreset.Paused));

        Assert.Equal(0, result.StepsProcessed);
        Assert.Empty(result.CommandResults);
        Assert.Empty(events);
    }

    private static long[] RunTrace(IEnumerable<TimeSpan> frames)
    {
        var trace = new List<long>();
        var host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            new[] { new TickTraceSystem(trace) });
        var rate = SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

        foreach (var frame in frames)
        {
            host.AdvanceFrame(frame, rate);
        }

        return trace.ToArray();
    }

    private sealed record RecordCommand(string Value) : ISimulationCommand;

    private sealed class RecordHandler(List<string> events)
        : CommandHandler<RecordCommand>
    {
        protected override CommandDecision Handle(
            RecordCommand command,
            SimulationTick tick)
        {
            events.Add($"{command.Value}@{tick.Value}");
            return CommandDecision.Accept();
        }
    }

    private sealed class RecordingSystem(
        int order,
        string name,
        List<string> events) : ISimulationSystem
    {
        public int Order => order;

        public void Step(SimulationStepContext context)
        {
            events.Add($"{name}@{context.Tick.Value}");
        }
    }

    private sealed class TickTraceSystem(List<long> trace) : ISimulationSystem
    {
        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            trace.Add(context.Tick.Value);
        }
    }
}
```

- [ ] **Step 2: Run the host tests and verify the expected compile failure**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SimulationHostTests
```

Expected: FAIL because `RailSim.Core.Hosting` does not exist.

- [ ] **Step 3: Implement the system contract and step context**

Create `src/RailSim.Core/Hosting/ISimulationSystem.cs`:

```csharp
namespace RailSim.Core.Hosting;

public interface ISimulationSystem
{
    int Order { get; }

    void Step(SimulationStepContext context);
}
```

Create `src/RailSim.Core/Hosting/SimulationStepContext.cs`:

```csharp
using RailSim.Core.Time;

namespace RailSim.Core.Hosting;

public sealed record SimulationStepContext(
    SimulationTick Tick,
    TimeSpan StepDuration);
```

- [ ] **Step 4: Implement the host and tick-boundary command execution**

Create `src/RailSim.Core/Hosting/SimulationHost.cs`:

```csharp
using RailSim.Core.Commands;
using RailSim.Core.Time;

namespace RailSim.Core.Hosting;

public sealed record FrameAdvanceResult(
    int StepsProcessed,
    SimulationTick CurrentTick,
    IReadOnlyList<CommandResult> CommandResults);

public sealed class SimulationHost
{
    private readonly SimulationClock _clock;
    private readonly SimulationCommandBus _commandBus;
    private readonly ISimulationSystem[] _systems;

    public SimulationHost(
        SimulationClock clock,
        SimulationCommandBus commandBus,
        IEnumerable<ISimulationSystem> systems)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        ArgumentNullException.ThrowIfNull(systems);

        _systems = systems
            .OrderBy(system => system.Order)
            .ToArray();

        var duplicateOrder = _systems
            .GroupBy(system => system.Order)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOrder is not null)
        {
            throw new InvalidOperationException(
                $"Simulation system order {duplicateOrder.Key} is registered more than once.");
        }
    }

    public SimulationTick CurrentTick => _clock.Current;

    public CommandEnvelope Submit(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _commandBus.Enqueue(command, _clock.Current.Next());
    }

    public FrameAdvanceResult AdvanceFrame(
        TimeSpan realElapsed,
        SimulationRate rate)
    {
        var dueSteps = _clock.Accumulate(realElapsed, rate);
        var commandResults = new List<CommandResult>();

        for (var index = 0; index < dueSteps; index++)
        {
            var tick = _clock.AdvanceOne();
            commandResults.AddRange(_commandBus.DispatchDue(tick));

            var context = new SimulationStepContext(
                tick,
                SimulationClock.StepDuration);
            foreach (var system in _systems)
            {
                system.Step(context);
            }
        }

        return new FrameAdvanceResult(
            dueSteps,
            _clock.Current,
            commandResults);
    }
}
```

- [ ] **Step 5: Run focused tests, full tests, and a Release build**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SimulationHostTests
.\.tools\dotnet\dotnet.exe test RailSim.sln --configuration Release
.\.tools\dotnet\dotnet.exe build RailSim.sln --configuration Release
```

Expected: all tests pass and the Release build exits 0 with no warnings.

- [ ] **Step 6: Commit the simulation host**

```powershell
git add src/RailSim.Core/Hosting tests/RailSim.Core.Tests/Hosting
git commit -m "feat: add ordered simulation host"
```

---

### Task 5: Immutable Query Snapshots and Notices

**Files:**
- Create: `src/RailSim.Core/Query/SimulationSnapshot.cs`
- Create: `src/RailSim.Core/Query/SnapshotStore.cs`
- Modify: `src/RailSim.Core/Hosting/SimulationStepContext.cs`
- Modify: `src/RailSim.Core/Hosting/SimulationHost.cs`
- Create: `tests/RailSim.Core.Tests/Query/SnapshotStoreTests.cs`

**Interfaces:**
- Consumes: `SimulationHost`, `CommandResult`, `SimulationTick`.
- Produces: `ISimulationSnapshotSection`, `ISnapshotContributor`, `SimulationNotice`, `SimulationNoticeSink`, `SimulationSnapshot`, `ISimulationQuery`, and `SnapshotStore`.

- [ ] **Step 1: Write failing tests for immutable latest-state queries**

Create `tests/RailSim.Core.Tests/Query/SnapshotStoreTests.cs`:

```csharp
using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
using RailSim.Core.Time;

namespace RailSim.Core.Tests.Query;

public sealed class SnapshotStoreTests
{
    [Fact]
    public void HostPublishesTickCommandsNoticesAndSections()
    {
        var state = new CounterState();
        var bus = new SimulationCommandBus();
        bus.Register(new IncrementHandler(state));
        var store = new SnapshotStore();
        var host = new SimulationHost(
            new SimulationClock(),
            bus,
            new[] { new CounterSystem(state) },
            store,
            new[] { new CounterContributor(state) });
        host.Submit(new IncrementCommand(3));

        host.AdvanceFrame(
            TimeSpan.FromMilliseconds(250),
            SimulationRate.FromPreset(SimulationSpeedPreset.Normal));
        var snapshot = store.GetLatest();

        Assert.Equal(new SimulationTick(1), snapshot.Tick);
        Assert.Equal(1, snapshot.LastCommandSequence);
        Assert.Equal("counter.stepped", Assert.Single(snapshot.Notices).Code);
        var section = Assert.IsType<CounterSection>(snapshot.Sections["counter"]);
        Assert.Equal(4, section.Value);
    }

    [Fact]
    public void PublishedCollectionsCannotBeMutatedThroughSourceCollections()
    {
        var notices = new List<SimulationNotice>
        {
            new("first", "First notice")
        };
        var sections = new Dictionary<string, ISimulationSnapshotSection>
        {
            ["counter"] = new CounterSection(1)
        };
        var store = new SnapshotStore();
        store.Publish(new SimulationSnapshot(
            new SimulationTick(1),
            0,
            notices,
            sections));

        notices.Add(new SimulationNotice("second", "Second notice"));
        sections["counter"] = new CounterSection(99);
        var snapshot = store.GetLatest();

        Assert.Single(snapshot.Notices);
        Assert.Equal(1, Assert.IsType<CounterSection>(snapshot.Sections["counter"]).Value);
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed record IncrementCommand(int Amount) : ISimulationCommand;

    private sealed class IncrementHandler(CounterState state)
        : CommandHandler<IncrementCommand>
    {
        protected override CommandDecision Handle(
            IncrementCommand command,
            SimulationTick tick)
        {
            state.Value += command.Amount;
            return CommandDecision.Accept();
        }
    }

    private sealed class CounterSystem(CounterState state) : ISimulationSystem
    {
        public int Order => 10;

        public void Step(SimulationStepContext context)
        {
            state.Value++;
            context.Notices.Add("counter.stepped", "Counter advanced.");
        }
    }

    private sealed record CounterSection(int Value) : ISimulationSnapshotSection
    {
        public string Key => "counter";
    }

    private sealed class CounterContributor(CounterState state)
        : ISnapshotContributor
    {
        public string Key => "counter";

        public ISimulationSnapshotSection Capture()
        {
            return new CounterSection(state.Value);
        }
    }
}
```

- [ ] **Step 2: Run query and host tests to verify the expected failures**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~SnapshotStoreTests|FullyQualifiedName~SimulationHostTests"
```

Expected: FAIL because query contracts, notice sink, and the extended host constructor do not exist.

- [ ] **Step 3: Implement snapshot and notice contracts**

Create `src/RailSim.Core/Query/SimulationSnapshot.cs`:

```csharp
using System.Collections.ObjectModel;
using RailSim.Core.Time;

namespace RailSim.Core.Query;

public interface ISimulationSnapshotSection
{
    string Key { get; }
}

public interface ISnapshotContributor
{
    string Key { get; }

    ISimulationSnapshotSection Capture();
}

public sealed record SimulationNotice(string Code, string Message);

public sealed class SimulationNoticeSink
{
    private readonly List<SimulationNotice> _notices = new();

    public void Add(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _notices.Add(new SimulationNotice(code, message));
    }

    public IReadOnlyList<SimulationNotice> Snapshot()
    {
        return _notices.ToArray();
    }
}

public sealed record SimulationSnapshot
{
    public SimulationSnapshot(
        SimulationTick tick,
        long lastCommandSequence,
        IEnumerable<SimulationNotice> notices,
        IReadOnlyDictionary<string, ISimulationSnapshotSection> sections)
    {
        Tick = tick;
        LastCommandSequence = lastCommandSequence;
        Notices = notices.ToArray();
        Sections = new ReadOnlyDictionary<string, ISimulationSnapshotSection>(
            sections.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    public SimulationTick Tick { get; }

    public long LastCommandSequence { get; }

    public IReadOnlyList<SimulationNotice> Notices { get; }

    public IReadOnlyDictionary<string, ISimulationSnapshotSection> Sections { get; }

    public static SimulationSnapshot Empty { get; } = new(
        SimulationTick.Zero,
        0,
        Array.Empty<SimulationNotice>(),
        new Dictionary<string, ISimulationSnapshotSection>());
}

public interface ISimulationQuery
{
    SimulationSnapshot GetLatest();
}
```

Create `src/RailSim.Core/Query/SnapshotStore.cs`:

```csharp
namespace RailSim.Core.Query;

public sealed class SnapshotStore : ISimulationQuery
{
    private SimulationSnapshot _latest = SimulationSnapshot.Empty;

    public SimulationSnapshot GetLatest()
    {
        return Volatile.Read(ref _latest);
    }

    public void Publish(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var independentCopy = new SimulationSnapshot(
            snapshot.Tick,
            snapshot.LastCommandSequence,
            snapshot.Notices,
            snapshot.Sections);
        Volatile.Write(ref _latest, independentCopy);
    }
}
```

- [ ] **Step 4: Add notices and snapshot publication to the host**

Replace `src/RailSim.Core/Hosting/SimulationStepContext.cs` with:

```csharp
using RailSim.Core.Query;
using RailSim.Core.Time;

namespace RailSim.Core.Hosting;

public sealed record SimulationStepContext(
    SimulationTick Tick,
    TimeSpan StepDuration,
    SimulationNoticeSink Notices);
```

Add the query namespace to `src/RailSim.Core/Hosting/SimulationHost.cs`:

```csharp
using RailSim.Core.Query;
```

Change `SimulationHost` fields and constructor to include:

```csharp
private readonly SnapshotStore _snapshotStore;
private readonly ISnapshotContributor[] _contributors;
private long _lastCommandSequence;

public SimulationHost(
    SimulationClock clock,
    SimulationCommandBus commandBus,
    IEnumerable<ISimulationSystem> systems)
    : this(
        clock,
        commandBus,
        systems,
        new SnapshotStore(),
        Array.Empty<ISnapshotContributor>())
{
}

public SimulationHost(
    SimulationClock clock,
    SimulationCommandBus commandBus,
    IEnumerable<ISimulationSystem> systems,
    SnapshotStore snapshotStore,
    IEnumerable<ISnapshotContributor> contributors)
{
    _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
    _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    ArgumentNullException.ThrowIfNull(systems);
    ArgumentNullException.ThrowIfNull(contributors);

    _systems = systems.OrderBy(system => system.Order).ToArray();
    _contributors = contributors.ToArray();

    var duplicateOrder = _systems
        .GroupBy(system => system.Order)
        .FirstOrDefault(group => group.Count() > 1);
    if (duplicateOrder is not null)
    {
        throw new InvalidOperationException(
            $"Simulation system order {duplicateOrder.Key} is registered more than once.");
    }

    var duplicateSection = _contributors
        .Select(contributor => contributor.Key)
        .GroupBy(key => key, StringComparer.Ordinal)
        .FirstOrDefault(group => group.Count() > 1);
    if (duplicateSection is not null)
    {
        throw new InvalidOperationException(
            $"Snapshot section key {duplicateSection.Key} is registered more than once.");
    }
}
```

Replace the body of the per-tick loop in `AdvanceFrame(...)` with:

```csharp
var tick = _clock.AdvanceOne();
var tickResults = _commandBus.DispatchDue(tick);
commandResults.AddRange(tickResults);
if (tickResults.Count > 0)
{
    _lastCommandSequence = tickResults.Max(result => result.Sequence);
}

var notices = new SimulationNoticeSink();
var context = new SimulationStepContext(
    tick,
    SimulationClock.StepDuration,
    notices);
foreach (var system in _systems)
{
    system.Step(context);
}

var sections = _contributors
    .Select(contributor =>
    {
        var section = contributor.Capture();
        if (!string.Equals(contributor.Key, section.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot contributor key {contributor.Key} does not match captured section key {section.Key}.");
        }

        return section;
    })
    .ToDictionary(section => section.Key, StringComparer.Ordinal);
_snapshotStore.Publish(new SimulationSnapshot(
    tick,
    _lastCommandSequence,
    notices.Snapshot(),
    sections));
```

Keep `FrameAdvanceResult`, `CurrentTick`, `Submit(...)`, and the method return unchanged.

- [ ] **Step 5: Run query, host, and full tests**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/RailSim.Core.Tests/RailSim.Core.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~SnapshotStoreTests|FullyQualifiedName~SimulationHostTests"
.\.tools\dotnet\dotnet.exe test RailSim.sln --configuration Release
```

Expected: all query, host, command, and clock tests pass.

- [ ] **Step 6: Commit immutable query snapshots**

```powershell
git add src/RailSim.Core/Query src/RailSim.Core/Hosting tests/RailSim.Core.Tests/Query tests/RailSim.Core.Tests/Hosting
git commit -m "feat: publish immutable simulation snapshots"
```

---

### Task 6: Godot Runtime Adapter and One-Command Verification

**Files:**
- Modify: `game/App/Bootstrap.cs`
- Modify: `game/Main.tscn`
- Create: `scripts/verify.ps1`
- Create: `README.md`

**Interfaces:**
- Consumes: `SimulationHost`, `SimulationRate`, `SnapshotStore`, and Godot `_Process(double delta)`.
- Produces: a Godot adapter that advances the core at 1×, displays the latest tick, a deterministic headless smoke contract `RAILSIM_SMOKE tick=4`, and a repository verification script.

- [ ] **Step 1: Define the failing headless smoke expectation**

Run before changing `Bootstrap.cs`:

```powershell
$godot = Get-ChildItem '.\.tools\godot' -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
    Select-Object -First 1 -ExpandProperty FullName
$output = & $godot --headless --path game -- --smoke-test 2>&1 | Out-String
if ($output -notmatch 'RAILSIM_SMOKE tick=4') {
    throw "Expected deterministic smoke marker was not produced.`n$output"
}
```

Expected: FAIL because the current bootstrap only prints `RAILSIM_BOOT`.

- [ ] **Step 2: Connect Godot frame time to the core host**

Replace `game/App/Bootstrap.cs` with:

```csharp
using Godot;
using RailSim.Core.Commands;
using RailSim.Core.Hosting;
using RailSim.Core.Query;
using RailSim.Core.Time;

namespace RailSim.App;

public partial class Bootstrap : Control
{
    private SimulationHost _host = null!;
    private ISimulationQuery _query = null!;
    private Label _status = null!;
    private SimulationRate _rate =
        SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

    public override void _Ready()
    {
        var store = new SnapshotStore();
        _query = store;
        _host = new SimulationHost(
            new SimulationClock(),
            new SimulationCommandBus(),
            Array.Empty<ISimulationSystem>(),
            store,
            Array.Empty<ISnapshotContributor>());
        _status = GetNode<Label>("Status");

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            RunSmokeTest();
            return;
        }

        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        _host.AdvanceFrame(TimeSpan.FromSeconds(delta), _rate);
        UpdateStatus();
    }

    private void RunSmokeTest()
    {
        _host.AdvanceFrame(TimeSpan.FromSeconds(1), _rate);
        var tick = _host.CurrentTick.Value;
        GD.Print($"RAILSIM_SMOKE tick={tick}");
        GetTree().Quit(tick == 4 ? 0 : 1);
    }

    private void UpdateStatus()
    {
        var snapshot = _query.GetLatest();
        _status.Text =
            $"RailSim foundation | Tick {snapshot.Tick.Value} | Rate {_rate.Preset}";
    }
}
```

Keep `game/Main.tscn` node paths unchanged. Change its initial label text to:

```ini
text = "RailSim foundation | Tick 0 | Rate Normal"
```

- [ ] **Step 3: Create a verification script that resolves local or installed tools**

Create `scripts/verify.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$localGodot = Get-ChildItem (Join-Path $repoRoot '.tools\godot') `
    -Recurse `
    -Filter 'Godot*v4.6.3*mono*win64*.exe' `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
$godot = if ($null -ne $localGodot) {
    $localGodot
} else {
    $command = Get-Command godot, godot4 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $command) {
        throw "Godot 4.6.3 .NET was not found. Run scripts/bootstrap-toolchain.ps1."
    }
    $command.Source
}

Push-Location $repoRoot
try {
    & $dotnet restore RailSim.sln
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed"
    }

    & $dotnet format RailSim.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format verification failed"
    }

    & $dotnet build RailSim.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed"
    }

    & $dotnet test RailSim.sln --configuration Release --no-build `
        --collect:"XPlat Code Coverage"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed"
    }

    $smokeOutput = & $godot --headless --path game -- --smoke-test 2>&1 |
        Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Godot smoke test exited with $LASTEXITCODE.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_SMOKE tick=4') {
        throw "Godot smoke marker missing.`n$smokeOutput"
    }

    Write-Host 'VERIFY_OK'
} finally {
    Pop-Location
}
```

- [ ] **Step 4: Document the exact developer workflow**

Create `README.md`:

```markdown
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
```

- [ ] **Step 5: Run the complete verification gate**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
git diff --check
git status --short
```

Expected:

- `dotnet format --verify-no-changes` exits 0.
- Release build exits 0 with no warnings.
- All xUnit tests pass with zero failures.
- Godot exits 0 and prints `RAILSIM_SMOKE tick=4`.
- Script prints `VERIFY_OK`.
- `git diff --check` prints nothing.
- `git status --short` lists only the intended Task 6 files before commit.

- [ ] **Step 6: Commit the runtime adapter and verification gate**

```powershell
git add game/App/Bootstrap.cs game/Main.tscn scripts/verify.ps1 README.md
git commit -m "feat: connect Godot to simulation core"
```

- [ ] **Step 7: Verify the committed subproject**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
git status --short --branch
git log --oneline -6
```

Expected: `VERIFY_OK`, a clean working tree, and six focused implementation commits after the design and plan commits.

---

## Plan Self-Review Checklist

- The plan implements only implementation-decomposition item 1 from the approved design.
- Every public type consumed by a later task is introduced in an earlier task with an exact signature.
- The fixed 0.25-second step and all ten speed presets have explicit tests.
- Command ordering, system ordering, pause behavior, frame partition determinism, immutable snapshots, and Godot integration have explicit tests.
- `RailSim.Core` remains free of Godot dependencies.
- The development toolchain is reproducible without changing system-wide SDK installations.
- The final verification command covers formatting, Release build, unit tests, coverage collection, and Godot headless smoke.

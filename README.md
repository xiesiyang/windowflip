# WindowFlip

**WindowFlip** 是一个轻量的 Windows 托盘程序，用于在当前应用的多个顶层窗口之间循环切换。它按应用可执行文件归组，适合同时打开多个 **Visual Studio Code** 窗口的场景。

## 系统要求

- 运行发布版本：Microsoft 当前支持的 **Windows 10/11 x64**，无需另外安装 .NET Runtime。
- 从源码构建：安装 **.NET 10 SDK**。

.NET 的支持范围与生命周期以 [Microsoft .NET 支持策略](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)为准。

## 使用方式

1. 运行发布目录中的 `WindowFlip.exe`。
2. 程序启动后常驻系统托盘，不显示任务栏窗口。
3. 在任意应用中按快捷键切换该应用的窗口。

| 操作 | 默认快捷键 | 备用快捷键 |
| --- | --- | --- |
| 下一个窗口 | `Alt + \`` | `Win + \`` |
| 上一个窗口 | `Alt + Shift + \`` | `Win + Shift + \`` |

只有默认快捷键被其他程序占用时，程序才会自动改用备用快捷键。托盘菜单会显示本次实际采用的组合键。

### 托盘菜单

1. **下一个窗口 / 上一个窗口**：手动触发切换。
2. **开机启动**：为当前 Windows 用户写入或移除启动项。
3. **退出**：注销全局快捷键并结束程序。

双击托盘图标也会切换到下一个窗口。

## 项目结构

```text
src/WindowFlip.Core/              窗口顺序、选择状态和会话逻辑
src/WindowFlip/                   WinForms 应用与 Windows 平台实现
tests/WindowFlip.Core.Tests/      xUnit 核心单元测试
tests/WindowFlip.Application.Tests/ 应用协调器单元测试
tests/WindowFlip.IntegrationHost/ 真实窗口集成测试宿主
scripts/                          发布和集成测试脚本
```

核心选择逻辑不引用 WinForms 或 Win32。快捷键输入、窗口发现、选择计算、窗口激活和悬浮界面分别位于独立边界，便于后续加入基于 **DWM Thumbnail API** 的实时窗口预览。

## 开发命令

### 还原与构建

```powershell
dotnet restore .\WindowFlip.sln
dotnet build .\WindowFlip.sln --configuration Release
```

开发构建结果位于 `src\WindowFlip\bin\Release\net10.0-windows\`。

### 单元测试

```powershell
dotnet test .\WindowFlip.sln --configuration Release
```

### 应用自检

```powershell
$process = Start-Process `
    .\src\WindowFlip\bin\Release\net10.0-windows\WindowFlip.exe `
    -ArgumentList '--self-test' `
    -PassThru `
    -Wait
$process.ExitCode
```

退出码 `0` 表示窗口枚举、切换会话和图标生成自检通过。

### Windows 集成测试

```powershell
.\scripts\integration.ps1
```

集成测试会构建解决方案、执行应用自检、创建两个临时窗口，并验证：

1. 单实例保护。
2. 正向与反向快捷键切换。
3. 最小化窗口恢复。
4. 前台窗口激活。

集成测试使用独立的互斥锁，不会关闭用户正在运行的 WindowFlip。存在用户实例时，测试会使用仍可注册的备用快捷键；测试结束后只关闭测试窗口和测试实例。

## 发布

```powershell
.\scripts\publish.ps1
```

发布脚本生成 **win-x64、自包含、未裁剪的单文件应用**：

```text
artifacts\publish\win-x64\WindowFlip.exe
```

## 行为说明

1. 只枚举可见、有标题且可出现在任务切换界面的顶层窗口。
2. 最小化窗口会在切换时自动还原。
3. 连续按键会保持一个 **2.5 秒窗口顺序会话**，避免因 Windows 改变 Z 顺序而只在最近两个窗口之间往返。
4. 普通权限运行的 WindowFlip 不能把焦点切换到以管理员权限运行的应用窗口，这是 Windows 权限隔离行为。

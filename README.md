# WindowFlip

**WindowFlip** 是一个轻量的 Windows 托盘程序，用于在当前应用的多个顶层窗口之间循环切换。它按应用可执行文件归组，适合同时打开多个 **Visual Studio Code** 窗口的场景。

## 系统要求

- 运行发布版本：Microsoft 当前支持的 **Windows 10/11 x64**，无需另外安装 .NET Runtime。
- 从源码构建：安装 **.NET 10 SDK**。
- 构建安装程序：另外安装 **Inno Setup 6**。

.NET 的支持范围与生命周期以 [Microsoft .NET 支持策略](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)为准。

## 使用方式

1. 运行 `WindowFlip-Setup-<版本>.exe` 完成安装。
2. 从开始菜单启动 **WindowFlip**；安装完成页默认也会直接启动程序。
3. 程序启动后常驻系统托盘，不显示任务栏窗口。
4. 在任意应用中按住快捷键的修饰键，重复按 `` ` `` 浏览该应用的窗口缩略图。
5. 松开 `Alt`（备用组合为 `Win`）后切换到选中窗口；按 `Esc` 取消本次选择。

| 操作 | 默认快捷键 | 备用快捷键 |
| --- | --- | --- |
| 下一个窗口 | `Alt + \`` | `Win + \`` |
| 上一个窗口 | `Alt + Shift + \`` | `Win + Shift + \`` |

只有默认快捷键被其他程序占用时，程序才会自动改用备用快捷键。托盘菜单会显示本次实际采用的组合键。

### 托盘菜单

1. **下一个窗口 / 上一个窗口**：手动触发切换。
2. **开机启动**：为当前 Windows 用户写入或移除启动项。
3. **自动同步文件对话框目录**：默认开启，可随时关闭；选择会保存在当前用户设置中。
4. **退出**：注销全局快捷键和窗口事件监听并结束程序。

双击托盘图标也会切换到下一个窗口。

### 文件对话框目录同步

1. 在其他应用中打开标准的“打开文件”或“另存为”对话框。
2. 切到 **Windows 文件资源管理器**，进入需要的目录。
3. 返回**原来的文件对话框**，WindowFlip 自动将其切换至资源管理器当前目录。

同步在后台执行，不显示搜索框或额外悬浮界面，不使用剪贴板。正常导航保留已有文件名、文件名选择范围与文件类型，不确认打开或保存。中途切到其他普通应用、关闭来源窗口或关闭功能，会取消本次同步；不把历史目录应用到新开的对话框。

设置保存在 `%LOCALAPPDATA%\WindowFlip\settings.json` 的 `DirectorySyncEnabled` 字段中，缺失或损坏时默认开启。设置写入失败时，本次开关仍生效，托盘菜单会显示“设置未保存”。

**兼容范围**：系统资源管理器，以及现代、经典标准文件对话框。本地目录与可访问的 UNC 文件夹可作为来源；“此电脑”、搜索结果等非文件系统位置、自定义对话框、第三方文件管理器和权限不足的窗口会跳过。Windows 11 通过活动 Shell 视图匹配标签页，无法确定活动标签时跳过；当前自动化实测环境为 Windows 10 19045，Windows 11 标签页与 UNC 环境仍需实机验证。

后台读取在独立 STA 线程中执行，带取消和超时控制。经典对话框的目录查询使用目标进程的数据缓冲区，不注入 DLL 或执行代码；若目标在提交导航命令时失去响应，停止后续操作，不提前恢复文件名后误触发打开或保存。

## 项目结构

```text
src/WindowFlip.Core/              窗口顺序、选择状态和会话逻辑
src/WindowFlip/                   WinForms 应用与 Windows 平台实现
tests/WindowFlip.Core.Tests/      xUnit 核心单元测试
tests/WindowFlip.Application.Tests/ 应用协调器单元测试
tests/WindowFlip.IntegrationHost/ 真实窗口集成测试宿主
installer/                        Inno Setup 安装器定义
scripts/                          发布和集成测试脚本
```

核心选择逻辑不引用 WinForms 或 Win32。快捷键输入、窗口发现、选择计算、窗口激活和悬浮界面分别位于独立边界；悬浮界面通过 **DWM Thumbnail API** 合成实时窗口预览。

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

退出码 `0` 表示窗口枚举、切换会话、图标生成和 **DWM 缩略图注册**自检通过。

### Windows 集成测试

```powershell
.\scripts\integration.ps1
```

集成测试会构建解决方案、执行应用自检、创建两个临时窗口，并验证：

1. 单实例保护。
2. 持键期间重复选择与缩略图覆盖层显示。
3. 松开修饰键前不切换、松开后才激活最终选中窗口。
4. 正向与反向快捷键切换。
5. 最小化窗口恢复。
6. 前台窗口激活。

集成测试使用独立的互斥锁，不会关闭用户正在运行的 WindowFlip。存在用户实例时，测试会使用仍可注册的备用快捷键；测试结束后只关闭测试窗口和测试实例。

### 目录同步集成测试

```powershell
.\scripts\directory-sync-test.ps1
```

测试创建专用资源管理器窗口和临时目录，验证现代、经典打开与另存为对话框的往返同步、中文及空格路径、跨进程导航、文件名和选择范围、文件类型、剪贴板及关闭开关行为。测试会短暂切换前台，请在桌面空闲时运行；中途操作其他应用会使同步按设计取消。测试不读取或修改用户的持久化开关设置，只清理自己创建的窗口与临时目录。

## 发布

安装 [Inno Setup 6](https://jrsoftware.org/isdl.php) 后执行：

```powershell
.\scripts\publish.ps1
```

也可以使用 Windows 包管理器安装构建依赖：

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

发布脚本先生成 **win-x64、自包含、未裁剪的单文件应用**作为临时输入，再将其封装为仅当前用户安装、无需管理员权限的安装程序。最终只保留：

```text
artifacts\installer\WindowFlip-Setup-1.0.0.exe
```

安装程序会将 WindowFlip 安装到 `%LOCALAPPDATA%\Programs\WindowFlip`，创建开始菜单快捷方式并登记标准卸载入口。安装完成后默认启动应用；静默安装不会自动启动。

### 安装程序验证

```powershell
.\scripts\installer-test.ps1
```

验证脚本会构建安装程序，并依次检查静默安装、开始菜单快捷方式、已安装应用自检、覆盖安装、静默卸载和开机启动项清理。为了避免影响现有环境，如果检测到已安装或正在运行的 WindowFlip，脚本会直接停止。

## 行为说明

1. 只枚举可见、有标题且可出现在任务切换界面的顶层窗口。
2. 最小化窗口会在切换时自动还原。
3. 按住修饰键时会持续显示实时缩略图；重复按 `` ` `` 循环选择，松开修饰键后才切换。
4. 连续操作会保持一个 **2.5 秒窗口顺序会话**，避免因 Windows 改变 Z 顺序而只在最近两个窗口之间往返。
5. 普通权限运行的 WindowFlip 不能把焦点切换到以管理员权限运行的应用窗口，这是 Windows 权限隔离行为。

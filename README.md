# WindowFlip

**WindowFlip** 是一个轻量的 Windows 托盘程序，用于在当前应用的多个顶层窗口之间循环切换。它按应用可执行文件归组，适合同时打开多个 **Visual Studio Code** 窗口的场景。

## 直接使用

1. 双击 `WindowFlip.exe`。
2. 程序启动后常驻系统托盘，不显示任务栏窗口。
3. 在任意应用中按快捷键切换该应用的窗口。

| 操作 | 默认快捷键 | 备用快捷键 |
| --- | --- | --- |
| 下一个窗口 | `Alt + \`` | `Win + \`` |
| 上一个窗口 | `Alt + Shift + \`` | `Win + Shift + \`` |

只有当默认快捷键已被其他程序占用时，程序才会自动改用备用快捷键。托盘菜单会显示本次实际采用的组合键。

## 托盘菜单

1. **下一个窗口 / 上一个窗口**：不使用键盘时手动触发切换。
2. **开机启动**：为当前 Windows 用户写入或移除启动项。
3. **退出**：注销全局快捷键并结束程序。

双击托盘图标也会切换到下一个窗口。

## 从源码构建

在 Windows PowerShell 5.1 中运行：

```powershell
.\build.ps1
```

构建只依赖 Windows 自带的 **.NET Framework C# 编译器**，不会下载第三方包。生成文件为仓库根目录下的 `WindowFlip.exe`。

## 行为说明

1. 只枚举可见、有标题且可出现在任务切换界面的顶层窗口。
2. 最小化窗口会在切换时自动还原。
3. 连续按键会保持一个短暂的窗口顺序会话，避免因 Windows 改变 Z 顺序而只在最近两个窗口之间来回跳转。
4. 普通权限运行的 WindowFlip 不能把焦点切换到以管理员权限运行的应用窗口，这是 Windows 的权限隔离行为。

## 自检

```powershell
$process = Start-Process .\WindowFlip.exe -ArgumentList '--self-test' -PassThru -Wait
$process.ExitCode
```

返回 `0` 表示窗口枚举、循环索引和图标生成自检通过。

完整的快捷键集成验证会创建两个临时测试窗口并模拟正向、反向切换：

```powershell
.\tests\integration.ps1
```

执行前需要先退出正在运行的 WindowFlip 实例；验证完成后，测试窗口和测试实例会自动关闭。

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$solutionPath = Join-Path $projectRoot 'WindowFlip.sln'
$windowFlipPath = Join-Path $projectRoot "src\WindowFlip\bin\$Configuration\net10.0-windows\WindowFlip.exe"
$hostPath = Join-Path $projectRoot "tests\WindowFlip.IntegrationHost\bin\$Configuration\net10.0-windows\WindowFlip.IntegrationHost.exe"
$integrationState = Join-Path ([IO.Path]::GetTempPath()) ("WindowFlip.IntegrationHost.{0}.txt" -f $PID)
$selfTestLog = Join-Path ([IO.Path]::GetTempPath()) ("WindowFlip.SelfTest.{0}.txt" -f $PID)
$hostProcess = $null
$windowFlipProcess = $null
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) {
    $dotnetCommand.Source
}
else {
    Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'The .NET SDK is missing. Install .NET 10 SDK before running integration tests.'
}

& $dotnetPath build $solutionPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

foreach ($requiredPath in @($windowFlipPath, $hostPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required executable is missing: $requiredPath"
    }
}

$existingWindowFlip = @(Get-Process -Name WindowFlip -ErrorAction SilentlyContinue)
$testExecutablePath = [IO.Path]::GetFullPath($windowFlipPath)
$existingTestInstance = @($existingWindowFlip | Where-Object {
    $_.Path -and [string]::Equals(
        [IO.Path]::GetFullPath($_.Path),
        $testExecutablePath,
        [StringComparison]::OrdinalIgnoreCase)
})
if ($existingTestInstance.Count -gt 0) {
    throw 'The integration-test build of WindowFlip is already running.'
}
$foreignWindowFlipExists = $existingWindowFlip.Count -gt 0

Add-Type -Namespace WindowFlipIntegration -Name NativeMethods -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr GetForegroundWindow();

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr window);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool BringWindowToTop(System.IntPtr window);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint GetWindowThreadProcessId(System.IntPtr window, out uint processId);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool AttachThreadInput(uint firstThread, uint secondThread, bool attach);

[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint GetCurrentThreadId();

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindowAsync(System.IntPtr window, int command);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool IsIconic(System.IntPtr window);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, System.UIntPtr extraInfo);
'@

function Wait-ForHostState {
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if (Test-Path -LiteralPath $integrationState) {
            $lines = Get-Content -LiteralPath $integrationState
            if ($lines.Count -eq 2) {
                return $lines
            }
        }
        Start-Sleep -Milliseconds 100
    }

    throw 'Timed out waiting for the integration host window handles.'
}

function Wait-ForForeground {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$Expected,
        [int]$Attempts = 40
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        if ([WindowFlipIntegration.NativeMethods]::GetForegroundWindow() -eq $Expected) {
            return $true
        }
        Start-Sleep -Milliseconds 50
    }

    return $false
}

function Set-TestForeground {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$Window
    )

    $currentThread = [WindowFlipIntegration.NativeMethods]::GetCurrentThreadId()
    $foregroundWindow = [WindowFlipIntegration.NativeMethods]::GetForegroundWindow()
    $processId = [uint32]0
    $foregroundThread = if ($foregroundWindow -ne [IntPtr]::Zero) {
        [WindowFlipIntegration.NativeMethods]::GetWindowThreadProcessId(
            $foregroundWindow,
            [ref]$processId)
    }
    else {
        0
    }
    $targetThread = [WindowFlipIntegration.NativeMethods]::GetWindowThreadProcessId(
        $Window,
        [ref]$processId)

    $attachedForeground = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread
    $attachedTarget = $targetThread -ne 0 -and $targetThread -ne $currentThread
    if ($attachedForeground) {
        [WindowFlipIntegration.NativeMethods]::AttachThreadInput(
            $currentThread,
            $foregroundThread,
            $true) | Out-Null
    }
    if ($attachedTarget) {
        [WindowFlipIntegration.NativeMethods]::AttachThreadInput(
            $currentThread,
            $targetThread,
            $true) | Out-Null
    }

    try {
        [WindowFlipIntegration.NativeMethods]::ShowWindowAsync($Window, 9) | Out-Null
        [WindowFlipIntegration.NativeMethods]::BringWindowToTop($Window) | Out-Null
        [WindowFlipIntegration.NativeMethods]::SetForegroundWindow($Window) | Out-Null
    }
    finally {
        if ($attachedTarget) {
            [WindowFlipIntegration.NativeMethods]::AttachThreadInput(
                $currentThread,
                $targetThread,
                $false) | Out-Null
        }
        if ($attachedForeground) {
            [WindowFlipIntegration.NativeMethods]::AttachThreadInput(
                $currentThread,
                $foregroundThread,
                $false) | Out-Null
        }
    }

    return Wait-ForForeground -Expected $Window
}

function Press-WindowFlipHotkey {
    param(
        [ValidateSet('Alt', 'Win')]
        [string]$Modifier,
        [ValidateRange(1, 10)]
        [int]$PressCount = 1,
        [switch]$Reverse
    )

    $keyUp = 0x0002
    $vkModifier = if ($Modifier -eq 'Alt') { 0x12 } else { 0x5B }
    $vkShift = 0x10
    $vkOem3 = 0xC0

    [WindowFlipIntegration.NativeMethods]::keybd_event($vkModifier, 0, 0, [UIntPtr]::Zero)
    if ($Reverse) {
        [WindowFlipIntegration.NativeMethods]::keybd_event($vkShift, 0, 0, [UIntPtr]::Zero)
    }
    for ($press = 0; $press -lt $PressCount; $press++) {
        [WindowFlipIntegration.NativeMethods]::keybd_event($vkOem3, 0, 0, [UIntPtr]::Zero)
        [WindowFlipIntegration.NativeMethods]::keybd_event($vkOem3, 0, $keyUp, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 50
    }
    if ($Reverse) {
        [WindowFlipIntegration.NativeMethods]::keybd_event($vkShift, 0, $keyUp, [UIntPtr]::Zero)
    }
}

function Release-WindowFlipHotkey {
    param(
        [ValidateSet('Alt', 'Win')]
        [string]$Modifier
    )

    $keyUp = 0x0002
    $vkModifier = if ($Modifier -eq 'Alt') { 0x12 } else { 0x5B }
    [WindowFlipIntegration.NativeMethods]::keybd_event($vkModifier, 0, $keyUp, [UIntPtr]::Zero)
}

function Invoke-WindowFlipHotkeyTest {
    param(
        [ValidateSet('Alt', 'Win')]
        [string]$Modifier,
        [Parameter(Mandatory = $true)]
        [IntPtr]$BeforeRelease,
        [Parameter(Mandatory = $true)]
        [IntPtr]$AfterRelease,
        [ValidateRange(1, 10)]
        [int]$PressCount = 1,
        [switch]$Reverse
    )

    Press-WindowFlipHotkey `
        -Modifier $Modifier `
        -PressCount $PressCount `
        -Reverse:$Reverse
    Start-Sleep -Milliseconds 300

    if ([WindowFlipIntegration.NativeMethods]::GetForegroundWindow() -ne $BeforeRelease) {
        Release-WindowFlipHotkey -Modifier $Modifier
        throw 'The target window changed before the shortcut modifier was released.'
    }

    $windowFlipProcess.Refresh()
    $overlayWasVisible = $windowFlipProcess.MainWindowHandle -ne [IntPtr]::Zero
    Release-WindowFlipHotkey -Modifier $Modifier

    return [pscustomobject]@{
        Switched = Wait-ForForeground -Expected $AfterRelease -Attempts 20
        OverlayWasVisible = $overlayWasVisible
    }
}

try {
    $selfTest = Start-Process -FilePath $windowFlipPath -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden -RedirectStandardError $selfTestLog
    if ($selfTest.ExitCode -ne 0) {
        Get-Content -LiteralPath $selfTestLog
        throw "WindowFlip self-test failed with exit code $($selfTest.ExitCode)."
    }

    $hostProcess = Start-Process -FilePath $hostPath -ArgumentList ('"{0}"' -f $integrationState) -PassThru
    try {
        $hostProcess.WaitForInputIdle(3000) | Out-Null
    }
    catch {
        # A fast process exit is reported by the explicit check below.
    }
    $hostProcess.Refresh()
    if ($hostProcess.HasExited) {
        throw "The integration host exited before showing its windows. Exit code: $($hostProcess.ExitCode)"
    }

    $hostHandles = Wait-ForHostState
    $firstHandle = [IntPtr]([Int64]$hostHandles[0])
    $secondHandle = [IntPtr]([Int64]$hostHandles[1])

    $windowFlipProcess = Start-Process -FilePath $windowFlipPath -ArgumentList '--integration-test' -PassThru
    Start-Sleep -Milliseconds 700
    $windowFlipProcess.Refresh()
    if ($windowFlipProcess.HasExited) {
        throw "WindowFlip failed to start. Exit code: $($windowFlipProcess.ExitCode)"
    }

    $secondaryInstance = Start-Process -FilePath $windowFlipPath -ArgumentList '--integration-test' -PassThru -Wait
    $windowFlipProcess.Refresh()
    if ($secondaryInstance.ExitCode -ne 0 -or $windowFlipProcess.HasExited) {
        throw 'The single-instance guard did not preserve the primary process.'
    }

    [WindowFlipIntegration.NativeMethods]::ShowWindowAsync($secondHandle, 6) | Out-Null
    if (-not (Set-TestForeground -Window $firstHandle)) {
        throw 'Could not activate the first test window.'
    }

    $registeredModifier = if ($foreignWindowFlipExists) { 'Win' } else { 'Alt' }
    $forwardResult = Invoke-WindowFlipHotkeyTest `
        -Modifier $registeredModifier `
        -BeforeRelease $firstHandle `
        -AfterRelease $secondHandle `
        -PressCount 3
    $forwardPassed = $forwardResult.Switched
    if (-not $forwardPassed -and -not $foreignWindowFlipExists) {
        $registeredModifier = 'Win'
        $forwardResult = Invoke-WindowFlipHotkeyTest `
            -Modifier $registeredModifier `
            -BeforeRelease $firstHandle `
            -AfterRelease $secondHandle `
            -PressCount 3
        $forwardPassed = $forwardResult.Switched
    }
    if (-not $forwardPassed) {
        throw 'Neither registered hotkey candidate activated the second test window.'
    }
    if (-not $forwardResult.OverlayWasVisible) {
        throw 'The thumbnail overlay was not visible while the shortcut modifier was held.'
    }
    if ([WindowFlipIntegration.NativeMethods]::IsIconic($secondHandle)) {
        throw 'The minimized target window was not restored.'
    }

    $backwardResult = Invoke-WindowFlipHotkeyTest `
        -Modifier $registeredModifier `
        -BeforeRelease $secondHandle `
        -AfterRelease $firstHandle `
        -Reverse
    $backwardPassed = $backwardResult.Switched
    if (-not $backwardPassed) {
        throw 'The reverse hotkey did not return to the first test window.'
    }

    [pscustomobject]@{
        SelfTest = $true
        SingleInstance = $true
        ForwardSwitch = $forwardPassed
        BackwardSwitch = $backwardPassed
        DeferredUntilModifierRelease = $true
        RepeatedSelectionWhileHeld = $true
        ThumbnailOverlayVisible = $forwardResult.OverlayWasVisible
        MinimizedWindowRestored = $true
        CoexistedWithUserInstance = $foreignWindowFlipExists
        RegisteredModifier = $registeredModifier
        FirstWindow = $firstHandle
        SecondWindow = $secondHandle
    }
}
finally {
    if ($windowFlipProcess -and -not $windowFlipProcess.HasExited) {
        Stop-Process -Id $windowFlipProcess.Id
        $windowFlipProcess.WaitForExit()
    }
    if ($hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id
        $hostProcess.WaitForExit()
    }
    if (Test-Path -LiteralPath $integrationState) {
        Remove-Item -LiteralPath $integrationState -Force
    }
    if (Test-Path -LiteralPath $selfTestLog) {
        Remove-Item -LiteralPath $selfTestLog
    }
}

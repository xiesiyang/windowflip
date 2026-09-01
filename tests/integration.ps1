[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$windowFlipPath = Join-Path $projectRoot 'WindowFlip.exe'
$hostSourcePath = Join-Path $projectRoot 'tests\IntegrationHost.cs'
$integrationOutput = Join-Path ([IO.Path]::GetTempPath()) ("WindowFlip.IntegrationHost.{0}.exe" -f $PID)
$integrationState = Join-Path ([IO.Path]::GetTempPath()) ("WindowFlip.IntegrationHost.{0}.txt" -f $PID)
$hostProcess = $null
$windowFlipProcess = $null

if (-not (Test-Path -LiteralPath $windowFlipPath)) {
    throw 'WindowFlip.exe is missing. Run build.ps1 first.'
}

$existingWindowFlip = Get-Process -Name WindowFlip -ErrorAction SilentlyContinue
if ($existingWindowFlip) {
    throw 'WindowFlip is already running. Exit it before the integration test.'
}

Add-Type -Namespace WindowFlipIntegration -Name NativeMethods -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr GetForegroundWindow();

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr window);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindowAsync(System.IntPtr window, int command);

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
        [IntPtr]$Expected
    )

    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ([WindowFlipIntegration.NativeMethods]::GetForegroundWindow() -eq $Expected) {
            return $true
        }
        Start-Sleep -Milliseconds 50
    }

    return $false
}

function Send-WindowFlipHotkey {
    param(
        [switch]$Reverse
    )

    $keyUp = 0x0002
    $vkAlt = 0x12
    $vkShift = 0x10
    $vkOem3 = 0xC0

    [WindowFlipIntegration.NativeMethods]::keybd_event($vkAlt, 0, 0, [UIntPtr]::Zero)
    if ($Reverse) {
        [WindowFlipIntegration.NativeMethods]::keybd_event($vkShift, 0, 0, [UIntPtr]::Zero)
    }
    [WindowFlipIntegration.NativeMethods]::keybd_event($vkOem3, 0, 0, [UIntPtr]::Zero)
    [WindowFlipIntegration.NativeMethods]::keybd_event($vkOem3, 0, $keyUp, [UIntPtr]::Zero)
    if ($Reverse) {
        [WindowFlipIntegration.NativeMethods]::keybd_event($vkShift, 0, $keyUp, [UIntPtr]::Zero)
    }
    [WindowFlipIntegration.NativeMethods]::keybd_event($vkAlt, 0, $keyUp, [UIntPtr]::Zero)
}

try {
    $compilerParameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $compilerParameters.GenerateExecutable = $true
    $compilerParameters.GenerateInMemory = $false
    $compilerParameters.OutputAssembly = $integrationOutput
    $compilerParameters.CompilerOptions = '/target:winexe /optimize+'
    @('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll') | ForEach-Object {
        [void]$compilerParameters.ReferencedAssemblies.Add($_)
    }

    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    try {
        $compileResult = $provider.CompileAssemblyFromFile($compilerParameters, $hostSourcePath)
        if ($compileResult.Errors.HasErrors) {
            $errors = $compileResult.Errors | ForEach-Object { $_.ToString() }
            throw ($errors -join [Environment]::NewLine)
        }
    }
    finally {
        $provider.Dispose()
    }

    $hostProcess = Start-Process -FilePath $integrationOutput -ArgumentList ('"{0}"' -f $integrationState) -PassThru
    try {
        $hostProcess.WaitForInputIdle(3000) | Out-Null
    }
    catch {
        # A fast process exit is reported with the explicit exit-code check below.
    }
    $hostProcess.Refresh()
    if ($hostProcess.HasExited) {
        throw "The integration host exited before showing its windows. Exit code: $($hostProcess.ExitCode)"
    }
    $hostHandles = Wait-ForHostState
    $firstHandle = [IntPtr]([Int64]$hostHandles[0])
    $secondHandle = [IntPtr]([Int64]$hostHandles[1])

    $windowFlipProcess = Start-Process -FilePath $windowFlipPath -PassThru
    Start-Sleep -Milliseconds 700
    $windowFlipProcess.Refresh()
    if ($windowFlipProcess.HasExited) {
        throw "WindowFlip failed to start. Exit code: $($windowFlipProcess.ExitCode)"
    }

    [WindowFlipIntegration.NativeMethods]::ShowWindowAsync($firstHandle, 9) | Out-Null
    [WindowFlipIntegration.NativeMethods]::SetForegroundWindow($firstHandle) | Out-Null
    if (-not (Wait-ForForeground -Expected $firstHandle)) {
        throw 'Could not activate the first test window.'
    }

    Send-WindowFlipHotkey
    $forwardPassed = Wait-ForForeground -Expected $secondHandle
    if (-not $forwardPassed) {
        throw 'The forward hotkey did not activate the second test window.'
    }

    Send-WindowFlipHotkey -Reverse
    $backwardPassed = Wait-ForForeground -Expected $firstHandle
    if (-not $backwardPassed) {
        throw 'The reverse hotkey did not return to the first test window.'
    }

    [pscustomobject]@{
        ForwardSwitch = $forwardPassed
        BackwardSwitch = $backwardPassed
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
    if (Test-Path -LiteralPath $integrationOutput) {
        Remove-Item -LiteralPath $integrationOutput -Force
    }
    if (Test-Path -LiteralPath $integrationState) {
        Remove-Item -LiteralPath $integrationState -Force
    }
}

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectPath = Join-Path $projectRoot 'src\WindowFlip\WindowFlip.csproj'
$publishScriptPath = Join-Path $projectRoot 'scripts\publish.ps1'
$installerOutputPath = Join-Path $projectRoot 'artifacts\installer'
$installPath = Join-Path $env:LOCALAPPDATA 'Programs\WindowFlip'
$installedExecutablePath = Join-Path $installPath 'WindowFlip.exe'
$uninstallerPath = Join-Path $installPath 'unins000.exe'
$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WindowFlip\WindowFlip.lnk'
$uninstallRegistryPath = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$runRegistryPath = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run'
$startupValueCreated = $false
$installedByTest = $false

function Get-WindowFlipUninstallEntry {
    if (-not (Test-Path -LiteralPath $uninstallRegistryPath)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $uninstallRegistryPath |
        Where-Object { $_.GetValue('DisplayName') -eq 'WindowFlip' } |
        Select-Object -First 1
}

function Invoke-InstallerProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-' `
        -PassThru `
        -Wait
    if ($process.ExitCode -ne 0) {
        throw "Installer process failed with exit code $($process.ExitCode): $FilePath"
    }
}

if (Get-Process -Name WindowFlip -ErrorAction SilentlyContinue) {
    throw 'WindowFlip is running. Exit it before running the installer test.'
}
if ((Test-Path -LiteralPath $installPath) -or
    (Test-Path -LiteralPath $shortcutPath) -or
    (Get-WindowFlipUninstallEntry)) {
    throw 'An existing WindowFlip installation was detected. Uninstall it before running this test.'
}
if ((Get-ItemProperty -LiteralPath $runRegistryPath -Name WindowFlip -ErrorAction SilentlyContinue)) {
    throw 'An existing WindowFlip startup entry was detected. Disable it before running this test.'
}

try {
    if (-not $SkipBuild) {
        & $publishScriptPath -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Packaging failed with exit code $LASTEXITCODE."
        }
    }

    $installers = @(Get-ChildItem -LiteralPath $installerOutputPath -Filter 'WindowFlip-Setup-*.exe')
    if ($installers.Count -ne 1) {
        throw "Expected exactly one installer in: $installerOutputPath"
    }
    $installerPath = $installers[0].FullName

    Invoke-InstallerProcess -FilePath $installerPath
    $installedByTest = $true

    foreach ($requiredPath in @($installedExecutablePath, $uninstallerPath, $shortcutPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Installed resource is missing: $requiredPath"
        }
    }

    $uninstallEntry = Get-WindowFlipUninstallEntry
    if (-not $uninstallEntry) {
        throw 'The WindowFlip uninstall registry entry is missing.'
    }
    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $expectedVersion = ([string]$project.Project.PropertyGroup.Version).Trim()
    if ($uninstallEntry.GetValue('DisplayVersion') -ne $expectedVersion) {
        throw 'The installed version does not match the project version.'
    }

    $selfTest = Start-Process `
        -FilePath $installedExecutablePath `
        -ArgumentList '--self-test' `
        -PassThru `
        -Wait
    if ($selfTest.ExitCode -ne 0) {
        throw "The installed application self-test failed with exit code $($selfTest.ExitCode)."
    }

    Invoke-InstallerProcess -FilePath $installerPath
    $uninstallEntries = @(
        Get-ChildItem -LiteralPath $uninstallRegistryPath |
            Where-Object { $_.GetValue('DisplayName') -eq 'WindowFlip' }
    )
    if ($uninstallEntries.Count -ne 1) {
        throw 'Reinstalling created duplicate uninstall registry entries.'
    }

    New-Item -Path $runRegistryPath -Force | Out-Null
    Set-ItemProperty `
        -LiteralPath $runRegistryPath `
        -Name WindowFlip `
        -Value ('"{0}" --startup' -f $installedExecutablePath)
    $startupValueCreated = $true

    Invoke-InstallerProcess -FilePath $uninstallerPath
    $installedByTest = $false

    foreach ($removedPath in @($installPath, $shortcutPath)) {
        if (Test-Path -LiteralPath $removedPath) {
            throw "Uninstall left a resource behind: $removedPath"
        }
    }
    if (Get-WindowFlipUninstallEntry) {
        throw 'Uninstall left its registry entry behind.'
    }
    if (Get-ItemProperty -LiteralPath $runRegistryPath -Name WindowFlip -ErrorAction SilentlyContinue) {
        throw 'Uninstall left the WindowFlip startup entry behind.'
    }
    $startupValueCreated = $false

    [pscustomobject]@{
        InstallerBuilt = $true
        SilentInstall = $true
        StartMenuShortcut = $true
        InstalledSelfTest = $true
        Reinstall = $true
        SilentUninstall = $true
        StartupEntryRemoved = $true
    }
}
finally {
    if ($installedByTest -and (Test-Path -LiteralPath $uninstallerPath)) {
        Invoke-InstallerProcess -FilePath $uninstallerPath
    }
    if ($startupValueCreated) {
        Remove-ItemProperty `
            -LiteralPath $runRegistryPath `
            -Name WindowFlip `
            -ErrorAction SilentlyContinue
    }
}

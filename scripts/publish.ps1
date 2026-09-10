[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectPath = Join-Path $projectRoot 'src\WindowFlip\WindowFlip.csproj'
$installerScriptPath = Join-Path $projectRoot 'installer\WindowFlip.iss'
$setupIconPath = Join-Path $projectRoot 'src\WindowFlip\Assets\WindowFlip.ico'
$artifactsPath = Join-Path $projectRoot 'artifacts'
$stagingPath = Join-Path $artifactsPath 'staging\win-x64'
$installerOutputPath = Join-Path $artifactsPath 'installer'
$legacyPublishPath = Join-Path $artifactsPath 'publish'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) {
    $dotnetCommand.Source
}
else {
    Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'The .NET SDK is missing. Install .NET 10 SDK before publishing.'
}

foreach ($requiredPath in @($projectPath, $installerScriptPath, $setupIconPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required packaging input is missing: $requiredPath"
    }
}

$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$isccCandidates = @(
    $(if ($isccCommand) { $isccCommand.Source }),
    $(if ($env:LOCALAPPDATA) {
        Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    }),
    $(if (${env:ProgramFiles(x86)}) {
        Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }),
    $(if ($env:ProgramFiles) {
        Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    })
) | Where-Object { $_ } | Select-Object -Unique
$isccPath = $isccCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $isccPath) {
    throw 'Inno Setup 6 is missing. Install it with: winget install --id JRSoftware.InnoSetup --exact'
}

[xml]$project = Get-Content -Raw -LiteralPath $projectPath
$versionNodes = @(
    $project.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
)
if ($versionNodes.Count -ne 1) {
    throw "Expected exactly one Version property in: $projectPath"
}

$appVersion = ([string]$versionNodes[0]).Trim()
if ($appVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "The project Version is not suitable for an installer filename: $appVersion"
}

foreach ($outputPath in @($stagingPath, $installerOutputPath)) {
    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

try {
    & $dotnetPath publish $projectPath `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $stagingPath `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $executablePath = Join-Path $stagingPath 'WindowFlip.exe'
    if (-not (Test-Path -LiteralPath $executablePath)) {
        throw "Published executable is missing: $executablePath"
    }

    & $isccPath `
        "/DAppVersion=$appVersion" `
        "/DSourceDir=$stagingPath" `
        "/DOutputDir=$installerOutputPath" `
        "/DSetupIconPath=$setupIconPath" `
        $installerScriptPath

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
}

$installerPath = Join-Path $installerOutputPath "WindowFlip-Setup-$appVersion.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer is missing after packaging: $installerPath"
}

if (Test-Path -LiteralPath $legacyPublishPath) {
    Remove-Item -LiteralPath $legacyPublishPath -Recurse -Force
}

Write-Host "Packaged: $installerPath"

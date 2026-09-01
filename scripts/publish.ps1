[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectPath = Join-Path $projectRoot 'src\WindowFlip\WindowFlip.csproj'
$outputPath = Join-Path $projectRoot 'artifacts\publish\win-x64'
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

& $dotnetPath publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $outputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $outputPath 'WindowFlip.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Published executable is missing: $executablePath"
}

Write-Host "Published: $executablePath"

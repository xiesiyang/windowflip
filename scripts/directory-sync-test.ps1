[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $projectRoot 'WindowFlip.sln') --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$hostPath = Join-Path $projectRoot "tests\WindowFlip.IntegrationHost\bin\$Configuration\net10.0-windows\WindowFlip.IntegrationHost.exe"
$resultPath = Join-Path ([IO.Path]::GetTempPath()) ('WindowFlip.DirectorySync.Result.' + [Guid]::NewGuid().ToString('N') + '.json')
$testProcess = $null
try {
    $testProcess = Start-Process -FilePath $hostPath -ArgumentList '--directory-sync-test', ('"{0}"' -f $resultPath) -WindowStyle Hidden -PassThru
    if (-not $testProcess.WaitForExit(60000)) { throw 'Directory sync integration timed out.' }
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Encoding UTF8 }
    if ($testProcess.ExitCode -ne 0) { throw "Directory sync integration failed: $($testProcess.ExitCode)" }
}
finally {
    if ($testProcess -and -not $testProcess.HasExited) { Stop-Process -Id $testProcess.Id }
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
}

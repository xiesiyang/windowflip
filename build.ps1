[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $projectRoot 'WindowFlip.cs'
$manifestPath = Join-Path $projectRoot 'WindowFlip.manifest'
$outputPath = Join-Path $projectRoot 'WindowFlip.exe'
$iconPath = Join-Path $projectRoot 'WindowFlip.ico'
$temporaryOutput = Join-Path $projectRoot 'WindowFlip.build.exe'

Add-Type -AssemblyName System.Drawing

$bitmap = New-Object System.Drawing.Bitmap 32, 32
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(14, 99, 156))
$windowPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 2
$arrowPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(146, 255, 211)), 2.5

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.FillRectangle($background, 1, 1, 30, 30)
    $graphics.DrawRectangle($windowPen, 6, 7, 14, 12)
    $graphics.DrawRectangle($windowPen, 12, 13, 14, 12)
    $arrowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $graphics.DrawLine($arrowPen, 7, 25, 22, 25)

    $iconHandle = $bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
        $stream = [System.IO.File]::Create($iconPath)
        try {
            $icon.Save($stream)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        Add-Type -Namespace WindowFlipBuild -Name NativeMethods -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool DestroyIcon(System.IntPtr icon);
'@
        [WindowFlipBuild.NativeMethods]::DestroyIcon($iconHandle) | Out-Null
    }
}
finally {
    $arrowPen.Dispose()
    $windowPen.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

if (Test-Path -LiteralPath $temporaryOutput) {
    Remove-Item -LiteralPath $temporaryOutput -Force
}

$compilerParameters = New-Object System.CodeDom.Compiler.CompilerParameters
$compilerParameters.GenerateExecutable = $true
$compilerParameters.GenerateInMemory = $false
$compilerParameters.OutputAssembly = $temporaryOutput
$compilerParameters.CompilerOptions = '/target:winexe /optimize+ /platform:anycpu /win32manifest:"{0}" /win32icon:"{1}"' -f $manifestPath, $iconPath
@('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll') | ForEach-Object {
    [void]$compilerParameters.ReferencedAssemblies.Add($_)
}

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
try {
    $compileResult = $provider.CompileAssemblyFromFile($compilerParameters, $sourcePath)
    if ($compileResult.Errors.HasErrors) {
        $messages = $compileResult.Errors | ForEach-Object {
            $level = if ($_.IsWarning) { 'warning' } else { 'error' }
            '{0}({1},{2}): {3} {4}: {5}' -f $_.FileName, $_.Line, $_.Column, $level, $_.ErrorNumber, $_.ErrorText
        }
        throw ($messages -join [Environment]::NewLine)
    }
}
finally {
    $provider.Dispose()
}

Move-Item -LiteralPath $temporaryOutput -Destination $outputPath -Force
Write-Host "Built: $outputPath"

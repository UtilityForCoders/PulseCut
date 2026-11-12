param(
    [string]$DotNet = 'dotnet',
    [string]$MediaTools,
    [string]$Output = (Join-Path $PSScriptRoot '../release/PulseCut-Windows-x64')
)

$ErrorActionPreference = 'Stop'
if (-not $MediaTools) {
    $MediaTools = & (Join-Path $PSScriptRoot 'get-media-tools.ps1')
}
$mediaPath = (Resolve-Path -LiteralPath $MediaTools).Path
foreach ($binary in @('ffmpeg.exe', 'ffprobe.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $mediaPath $binary) -PathType Leaf)) {
        throw "Falta $binary en $mediaPath."
    }
}

$project = Join-Path $PSScriptRoot 'PulseCut/PulseCut.csproj'
$publishOptions = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None')
& $DotNet publish $project -c Release -r win-x64 --self-contained true @publishOptions -o $Output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$destination = New-Item -ItemType Directory -Force -Path (Join-Path $Output 'bin')
foreach ($binary in @('ffmpeg.exe', 'ffprobe.exe')) {
    Copy-Item -LiteralPath (Join-Path $mediaPath $binary) -Destination $destination.FullName
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $Output

$notices = New-Item -ItemType Directory -Force -Path (Join-Path $Output 'licenses')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MEDIA-NOTICES.md') -Destination $notices.FullName
foreach ($binary in @('ffmpeg.exe', 'ffprobe.exe')) {
    $tool = Join-Path $destination.FullName $binary
    $licenseProcess = [System.Diagnostics.Process]::new()
    $licenseProcess.StartInfo = [System.Diagnostics.ProcessStartInfo]::new($tool)
    $licenseProcess.StartInfo.UseShellExecute = $false
    $licenseProcess.StartInfo.CreateNoWindow = $true
    $licenseProcess.StartInfo.RedirectStandardOutput = $true
    $licenseProcess.StartInfo.RedirectStandardError = $true
    $licenseProcess.StartInfo.Arguments = '-L'
    if (-not $licenseProcess.Start()) { throw "No se pudo consultar la licencia de $binary." }
    $licenseOutput = $licenseProcess.StandardOutput.ReadToEndAsync()
    $licenseError = $licenseProcess.StandardError.ReadToEndAsync()
    $licenseProcess.WaitForExit()
    ($licenseOutput.Result + $licenseError.Result) |
        Out-File (Join-Path $notices.FullName "$binary-license.txt") -Encoding utf8
    if ($licenseProcess.ExitCode -ne 0) { throw "$binary no pudo mostrar su licencia." }
    $licenseProcess.Dispose()
}
$packagedNotices = Join-Path $mediaPath '../licenses'
foreach ($name in @('FFmpeg-LICENSE.txt', 'FFmpeg-README.txt')) {
    $notice = Join-Path $packagedNotices $name
    if (Test-Path -LiteralPath $notice -PathType Leaf) {
        Copy-Item -LiteralPath $notice -Destination $notices.FullName
    }
}

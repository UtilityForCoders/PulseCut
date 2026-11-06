param(
    [string]$DotNet = 'dotnet',
    [string]$MediaTools = (Join-Path $PSScriptRoot '../release/win-unpacked/resources/bin'),
    [string]$Output = (Join-Path $PSScriptRoot '../release/PulseCut-Windows-x64')
)

$ErrorActionPreference = 'Stop'
$mediaPath = (Resolve-Path -LiteralPath $MediaTools).Path
foreach ($binary in @('ffmpeg.exe', 'ffprobe.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $mediaPath $binary) -PathType Leaf)) {
        throw "Missing $binary in $mediaPath"
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
    & $tool -L 2>&1 | Out-File (Join-Path $notices.FullName "$binary-license.txt") -Encoding utf8
}
$packagedNotices = Join-Path $mediaPath '../licenses'
foreach ($name in @('FFmpeg-LICENSE.txt', 'FFmpeg-README.txt')) {
    $notice = Join-Path $packagedNotices $name
    if (Test-Path -LiteralPath $notice -PathType Leaf) {
        Copy-Item -LiteralPath $notice -Destination $notices.FullName
    }
}

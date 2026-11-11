param(
    [string]$Destination = (Join-Path $PSScriptRoot '../artifacts/media-tools')
)

$ErrorActionPreference = 'Stop'
$bin = (New-Item -ItemType Directory -Force (Join-Path $Destination 'bin')).FullName
$licenses = (New-Item -ItemType Directory -Force (Join-Path $Destination 'licenses')).FullName
$releaseUrl = 'https://github.com/eugeneware/ffmpeg-static/releases/download/b6.1.1'
$downloads = @(
    @{ Asset = 'ffmpeg-win32-x64'; Path = (Join-Path $bin 'ffmpeg.exe'); Sha256 = '04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00' }
    @{ Asset = 'ffprobe-win32-x64'; Path = (Join-Path $bin 'ffprobe.exe'); Sha256 = '3a7e2dc003dc2cd1472827e4c7c4f056ae1ae0ae7c5bbc580c99b49827351ba4' }
    @{ Asset = 'win32-x64.LICENSE'; Path = (Join-Path $licenses 'FFmpeg-LICENSE.txt'); Sha256 = '8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903' }
    @{ Asset = 'win32-x64.README'; Path = (Join-Path $licenses 'FFmpeg-README.txt'); Sha256 = 'a636a7183c58006351acbaf35303c0ed85c6e1320fd4e80de453ba6157de6311' }
)

foreach ($download in $downloads) {
    if ((Test-Path -LiteralPath $download.Path -PathType Leaf) -and
        (Get-FileHash -LiteralPath $download.Path).Hash -eq $download.Sha256) {
        continue
    }

    $partial = $download.Path + '.download'
    try {
        & curl.exe --fail --silent --show-error --location --retry 3 --max-time 180 "${releaseUrl}/$($download.Asset)" -o $partial
        if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $partial).Hash -ne $download.Sha256) {
            throw "Falló la descarga o la verificación de $($download.Asset)."
        }
        Move-Item -LiteralPath $partial -Destination $download.Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
    }
}

$bin

param(
    [string]$DotNet = 'dotnet',
    [string]$Compiler = (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6/ISCC.exe'),
    [string]$Output = (Join-Path $PSScriptRoot '../release/installer'),
    [string]$ExpectedTag
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'PulseCut/PulseCut.csproj'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = $project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw "Versión de proyecto no válida: $version"
}
if ($ExpectedTag -and $ExpectedTag -cne "v$version") {
    throw "El tag $ExpectedTag debe coincidir con v$version."
}

$work = (New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "../artifacts/installer/$version")).FullName
$mediaTools = & (Join-Path $PSScriptRoot 'get-media-tools.ps1')
$payload = Join-Path $work 'app'
& (Join-Path $PSScriptRoot 'publish.ps1') -DotNet $DotNet -MediaTools $mediaTools -Output $payload | Out-Host

$dotnetRoot = Split-Path (Get-Command $DotNet -CommandType Application -ErrorAction Stop).Source
foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $noticePath = Join-Path $dotnetRoot $notice
    if (Test-Path -LiteralPath $noticePath -PathType Leaf) {
        Copy-Item -LiteralPath $noticePath -Destination (Join-Path $payload "licenses/DotNet-$notice")
    }
}

$sample = Join-Path $work 'muestra.mp4'
$sampleOptions = @('-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi',
    '-i', 'testsrc2=size=320x180:rate=30:duration=4', '-f', 'lavfi', '-i', 'sine=frequency=440:duration=4',
    '-c:v', 'libx264', '-c:a', 'aac', '-shortest', $sample)
& (Join-Path $mediaTools 'ffmpeg.exe') @sampleOptions
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar la muestra para la prueba.' }

$test = (New-Item -ItemType Directory -Path (Join-Path $work ([guid]::NewGuid().ToString('N')))).FullName
$smokeArguments = '--smoke-test "{0}" "{1}"' -f $sample, $test
$process = Start-Process -FilePath (Join-Path $payload 'PulseCut.exe') -ArgumentList $smokeArguments -WindowStyle Hidden -PassThru -Wait
$reportPath = Join-Path $test 'report.json'
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    $errorPath = Join-Path $test 'error.txt'
    if (Test-Path -LiteralPath $errorPath -PathType Leaf) { Get-Content -LiteralPath $errorPath | Out-Host }
    throw 'La prueba de exportación falló.'
}
Get-Content -LiteralPath $reportPath | Out-Host

$compilerCommand = Get-Command $Compiler -CommandType Application -ErrorAction SilentlyContinue
if (-not $compilerCommand) {
    throw 'No se encontró ISCC.exe. Instala Inno Setup 6.3 o posterior o indica su ruta con -Compiler.'
}
$compilerPath = $compilerCommand.Source
$outputPath = (New-Item -ItemType Directory -Force -Path $Output).FullName
& $compilerPath "/DAppVersion=$version" "/DPayloadDir=$payload" "/O$outputPath" (Join-Path $PSScriptRoot 'PulseCut.iss') | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup no pudo compilar el instalador.' }
Get-Item -LiteralPath (Join-Path $outputPath "PulseCut-$version-setup-win-x64.exe")

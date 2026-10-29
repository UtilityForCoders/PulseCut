"""Collect release documentation, third-party notices and checksums."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile

root = Path(__file__).resolve().parent.parent
release = root / 'release'
version = json.loads((root / 'package.json').read_text(encoding='utf-8'))['version']
exe = release / f'PulseCut-{version}-portable.exe'
if not exe.is_file():
    raise SystemExit('Build the Windows executable first.')

for name in ['README.md', 'THIRD-PARTY-NOTICES.md', 'RELEASE_NOTES.md', 'CHANGELOG.md']:
    shutil.copyfile(root / name, release / name)
reports = {}
for name in ['desktop-smoke.json', 'portable-smoke.json']:
    source = root / 'artifacts' / name
    report = json.loads(source.read_text(encoding='utf-8'))
    if not report.get('ok') or not report.get('packaged') or report.get('version') != version:
        raise SystemExit(f'The packaged smoke test has not passed for this version: {name}')
    reports[name] = report
    shutil.copyfile(source, release / name)

(release / 'VERIFICACION.md').write_text(f'''# Verificación de PulseCut {version}

## Distribución

- Ejecutable portátil: `{exe.name}`.
- Tamaño: {exe.stat().st_size} bytes.
- Plataforma: Windows x64.
- Integridad: `SHA256SUMS.txt`.

## Pruebas realizadas

Las cinco pruebas del motor pasaron mediante `npm.cmd test`. Comprueban importación de video y audio, rangos HTTP, exportación con recorte y cambio de velocidad, video sin sonido y rechazo de archivos o parámetros inválidos.

Las pruebas de la aplicación sin comprimir y del ejecutable portátil finalizaron con código 0. Ambas verificaron la ventana, el aislamiento del renderizador, los binarios incluidos de FFmpeg y FFprobe, la importación de una muestra y la descarga de una exportación MP4.

Un tramo de los segundos 1 a 3 a velocidad 2x produjo video H.264 con audio y estas duraciones:

- Aplicación sin comprimir: {reports['desktop-smoke.json']['duration']} segundos.
- Ejecutable portátil: {reports['portable-smoke.json']['duration']} segundos.

Los informes completos están en `desktop-smoke.json` y `portable-smoke.json`.

Se verificaron manualmente la apertura de la ventana nativa, el selector de archivos de Windows y la importación de una muestra MP4.

## Alcance de la verificación

La prueba automática de descarga lee el archivo por HTTP local; no comprueba la confirmación del diálogo de guardado de Windows. No se verificó la ejecución en una segunda computadora, la reputación en SmartScreen ni el rendimiento con archivos grandes. La compilación no tiene firma digital.
''', encoding='utf-8')

runtime = subprocess.run(['npm.cmd', 'ls', '--omit=dev', '--parseable', '--all'],
    cwd=root, capture_output=True, text=True, check=True)
with zipfile.ZipFile(release / 'THIRD-PARTY-LICENSES.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    archive.write(root / 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES.md')
    for name in ['LICENSE.electron.txt', 'LICENSES.chromium.html']:
        archive.write(release / 'win-unpacked' / name, f'electron/{name}')
    for file in sorted((release / 'win-unpacked' / 'resources' / 'licenses').glob('*')):
        if file.is_file():
            archive.write(file, f'media/{file.name}')
    for item in sorted(set(runtime.stdout.splitlines())):
        package = Path(item)
        if package == root or not package.is_dir():
            continue
        try:
            metadata = json.loads((package / 'package.json').read_text(encoding='utf-8'))
        except (FileNotFoundError, json.JSONDecodeError):
            continue
        prefix = f'npm/{metadata["name"]}@{metadata["version"]}'
        archive.write(package / 'package.json', f'{prefix}/package.json')
        for file in package.iterdir():
            if file.is_file() and file.name.lower().startswith(('license', 'licence', 'copying', 'notice')):
                archive.write(file, f'{prefix}/{file.name}')
    for name in ['ffmpeg', 'ffprobe']:
        binary = release / 'win-unpacked' / 'resources' / 'bin' / f'{name}.exe'
        for flag, suffix in [('-version', 'version'), ('-L', 'license')]:
            result = subprocess.run([str(binary), flag], capture_output=True, check=True,
                creationflags=subprocess.CREATE_NO_WINDOW)
            archive.writestr(f'media/{name}-{suffix}.txt', result.stdout + result.stderr)

assets = sorted(file for file in release.iterdir() if file.is_file() and file.name != 'SHA256SUMS.txt'
    and file.suffix in {'.exe', '.md', '.zip', '.json'})
lines = []
for file in assets:
    with file.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    lines.append(f'{digest}  {file.name}')
(release / 'SHA256SUMS.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(json.dumps({'executable': exe.name, 'assets': [file.name for file in assets], 'checksum_file': 'SHA256SUMS.txt'}))

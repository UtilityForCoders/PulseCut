# Componentes de terceros

PulseCut incluye Electron, Chromium, Node.js, Express, Multer y dependencias transitivas. Sus avisos y licencias se conservan en los paquetes y en los archivos LICENSE.electron.txt y LICENSES.chromium.html de la distribución de Electron.

## FFmpeg

El ejecutable ffmpeg.exe se obtiene del paquete ffmpeg-static. La compilación incluida es FFmpeg 6.1.1 essentials de Gyan, distribuida bajo GPL v3. El archivo de licencia y las notas de la compilación se incluyen en resources/licenses.

- Proyecto: https://ffmpeg.org/
- Distribución: https://www.gyan.dev/ffmpeg/builds/
- Paquete: https://github.com/eugeneware/ffmpeg-static
- Código fuente de FFmpeg de esta compilación, según el proveedor: https://github.com/FFmpeg/FFmpeg/commit/e38092ef93

## FFprobe

El ejecutable ffprobe.exe se obtiene de ffprobe-static 3.1.0. El paquete JavaScript se distribuye bajo MIT. El ejecutable incluido es FFprobe 4.0.2, compilado con GPL y version3 habilitados, y su licencia es GPL v3 o posterior. La licencia MIT del envoltorio no sustituye la licencia del ejecutable.

- Paquete y procedencia de los binarios: https://github.com/joshwnj/ffprobe-static
- Proyecto y fuentes: https://ffmpeg.org/
- Fuentes de FFmpeg 4.0.2: https://github.com/FFmpeg/FFmpeg/tree/n4.0.2

Los ejecutables se utilizan como procesos independientes. La release adjunta THIRD-PARTY-LICENSES.zip con los textos de licencia, las versiones y configuraciones declaradas por ambos ejecutables, los avisos de Electron y Chromium y las licencias de las dependencias de producción de npm. Las fuentes y la procedencia de FFmpeg y FFprobe se enlazan arriba. El código de PulseCut y su archivo de dependencias se publican en el mismo tag de GitHub que el ejecutable.

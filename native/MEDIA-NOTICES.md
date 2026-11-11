# Componentes multimedia

PulseCut invoca FFmpeg y FFprobe como procesos independientes. El script de publicación copia los ejecutables de la carpeta indicada y registra las licencias que declaran con la opción `-L`.

El instalador incluye FFmpeg y FFprobe 6.1.1 essentials de Gyan. La compilación verifica el SHA-256 de los binarios y conserva su licencia y sus notas en la carpeta `licenses`. [Binarios y avisos de la compilación](https://github.com/eugeneware/ffmpeg-static/releases/tag/b6.1.1) · [Fuentes de FFmpeg 6.1.1](https://github.com/FFmpeg/FFmpeg/commit/e38092ef93).

La opción `-MediaTools` permite compilar con otros binarios locales.

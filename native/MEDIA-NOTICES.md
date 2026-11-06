# Componentes multimedia

PulseCut invoca FFmpeg y FFprobe como procesos independientes. El script de publicación copia los ejecutables de la carpeta indicada y registra las licencias que declaran con la opción `-L`.

Los binarios reutilizados de PulseCut 0.1.0 son FFmpeg 6.1.1 essentials de Gyan y FFprobe 4.0.2 de ffprobe-static 3.1.0. Consulta la [compilación de Gyan](https://www.gyan.dev/ffmpeg/builds/), la [revisión fuente de FFmpeg 6.1.1](https://github.com/FFmpeg/FFmpeg/commit/e38092ef93) y las [fuentes de FFprobe 4.0.2](https://github.com/FFmpeg/FFmpeg/tree/n4.0.2).

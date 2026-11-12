# PulseCut

Editor de video y audio para Windows x64, desarrollado en C# con .NET 10 y WPF. PulseCut usa FFmpeg y FFprobe para leer los archivos y exportar los clips en el equipo.

- Abre o arrastra un video o audio y revisa la vista previa.
- Define el tramo con los tiradores de la línea de tiempo o escribe los tiempos exactos.
- Cambia la velocidad de salida entre 0,5× y 2× y exporta a MP4, MP3 o WAV.
- Ajusta resolución y calidad para video sin salir de una interfaz compacta.
- Usa deshacer, rehacer, zoom y atajos para recortar con rapidez.

Para compilar la aplicación, instala el SDK de .NET 10 y ejecuta:

```powershell
.\native\publish.ps1
```

Abre `release\PulseCut-Windows-x64\PulseCut.exe`. La guía de escritorio incluye los pasos para generar el instalador y publicar una release. [Guía de escritorio](native/README.md)

El código de la aplicación está en `native/PulseCut`. Los avisos de FFmpeg y FFprobe se incluyen en la carpeta `licenses` del paquete. [Componentes multimedia](native/MEDIA-NOTICES.md)

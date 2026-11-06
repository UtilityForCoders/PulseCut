# PulseCut 0.2.0 alfa

Aplicación de escritorio para Windows x64, hecha en C# y WPF. Abre `PulseCut.exe` desde la carpeta extraída. El ejecutable incluye el runtime de .NET. Conserva la carpeta `bin` junto al ejecutable.

## Uso

1. Abre o arrastra un archivo de video o audio.
2. Escribe el inicio y el fin en segundos. También puedes mover el control de reproducción y usar esa posición como inicio o fin.
3. Elige la velocidad de salida y el formato. MP4 requiere video. MP3 y WAV requieren una pista de audio.
4. Pulsa **Exportar clip** y elige el archivo de destino. Puedes cancelar la exportación en curso.

La vista previa usa el reproductor de Windows a velocidad original. Su compatibilidad depende de los códecs disponibles en el equipo. Si Windows no puede reproducir un archivo que FFmpeg sí admite, puedes definir el recorte por tiempo y exportarlo. La aplicación edita un archivo a la vez.

## Compilar

Requiere el SDK de .NET 10 y dos ejecutables Windows x64 de FFmpeg y FFprobe. El script usa por defecto la carpeta de herramientas de la compilación 0.1.0 que ya existe en este equipo. Puedes indicar otra carpeta con `-MediaTools`.

```powershell
.\native\publish.ps1 -MediaTools 'C:\ruta\a\bin'
```

La publicación local se genera en `release\PulseCut-Windows-x64`. La carpeta `native` contiene el código de esta edición.

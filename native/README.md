# PulseCut 0.2.0 alfa 4

Aplicación de escritorio para Windows x64, hecha en C# y WPF. Abre `PulseCut.exe` desde la carpeta extraída. El ejecutable incluye el runtime de .NET. Conserva la carpeta `bin` junto al ejecutable.

## Uso

1. Abre o arrastra un archivo de video o audio.
2. Arrastra los extremos verdes en la línea de tiempo o escribe el inicio y el fin en segundos. También puedes usar la posición actual de reproducción. Con un extremo enfocado, las flechas ajustan 0,1 s y Mayús más flecha ajusta 1 s.
3. Elige la velocidad de salida y el formato. El panel muestra el códec y la duración del resultado. MP4 requiere video. MP3 y WAV requieren una pista de audio.
4. Pulsa **Exportar clip** y elige el archivo de destino. Puedes cancelar la exportación en curso.

La vista previa usa el reproductor de Windows a velocidad original. Su compatibilidad depende de los códecs disponibles en el equipo. Si Windows no puede reproducir un archivo que FFmpeg sí admite, puedes definir el recorte por tiempo y exportarlo. La aplicación edita un archivo a la vez.

La barra superior permite abrir archivos, minimizar, maximizar, restaurar y cerrar la ventana. Arrastra un espacio libre de la barra para moverla. Haz doble clic ahí para alternar entre el tamaño normal y maximizado.

## Compilar

Requiere el SDK de .NET 10 y dos ejecutables Windows x64 de FFmpeg y FFprobe. El script usa por defecto la carpeta de herramientas de la compilación 0.1.0 que ya existe en este equipo. Puedes indicar otra carpeta con `-MediaTools`.

```powershell
.\native\publish.ps1 -MediaTools 'C:\ruta\a\bin' -Output (Join-Path (Get-Location) 'release/PulseCut-0.2.0-alpha.4-win-x64')
```

La carpeta de salida contiene el ejecutable y las herramientas que necesita. La carpeta `native` contiene el código de esta edición.

## Flujo de ramas

- `main` contiene el código de las versiones publicadas. Cada versión se identifica con su etiqueta.
- `develop` integra los cambios de la próxima versión.
- `feature/*` parte de `develop` y vuelve a `develop` cuando el cambio está revisado. La edición de escritorio se trabaja en `feature/editor-escritorio`.
- `release/*` parte de `develop`. Al publicar, se integra en `main`, se etiqueta la versión y se incorporan los ajustes a `develop`.
- `hotfix/*` parte de `main` y lleva la corrección a `main` y a `develop`.

Usa Conventional Commits con el asunto en español, por ejemplo `feat(editor): añade tiradores de recorte` o `fix(ventana): corrige el tamaño al restaurar`. Cada commit debe describir un cambio concreto.

# PulseCut 0.2.0 alfa 4

Aplicación de escritorio para Windows x64, hecha en C# y WPF. Abre `PulseCut.exe` desde la carpeta extraída. El ejecutable incluye el runtime de .NET. Conserva la carpeta `bin` junto al ejecutable.

## Uso

1. Abre o arrastra un archivo de video o audio.
2. Arrastra los extremos del tramo en la línea de tiempo o escribe el inicio y el fin en segundos. También puedes usar la posición actual de reproducción. Con un extremo enfocado, las flechas ajustan 0,1 s y Mayús más flecha ajusta 1 s.
3. Elige la velocidad de salida, el formato y, para video, la resolución y calidad. El panel muestra el códec y la duración del resultado. MP4 requiere video. MP3 y WAV requieren una pista de audio.
4. Pulsa **Exportar clip** y elige el archivo de destino. Puedes cancelar la exportación en curso.

La vista previa usa el reproductor de Windows a velocidad original. Su compatibilidad depende de los códecs disponibles en el equipo. Si Windows no puede reproducir un archivo que FFmpeg sí admite, puedes definir el recorte por tiempo y exportarlo. La aplicación edita un archivo a la vez.

La barra superior permite abrir archivos, minimizar, maximizar, restaurar y cerrar la ventana. Arrastra un espacio libre de la barra para moverla. Haz doble clic ahí para alternar entre el tamaño normal y maximizado.

## Atajos

| Atajo | Acción |
| --- | --- |
| `Ctrl+O` | Abrir un archivo. |
| `Espacio` | Reproducir o pausar. |
| `I` / `O` | Marcar el inicio o el fin. |
| `Ctrl+Z` / `Ctrl+Y` | Deshacer o rehacer cambios de edición. |
| `Supr` | Restablecer el intervalo completo. |
| `Ctrl+E` | Exportar el clip. |
| `←` / `→` | Retroceder o avanzar un segundo. |
| `Ctrl` + rueda | Ajustar el zoom de la línea de tiempo. |

Los atajos de edición se suspenden mientras se escribe en un campo. La línea de tiempo representa un único archivo y un único intervalo: no elimina segmentos internos, no crea múltiples pistas y no genera miniaturas ni formas de onda. Esto mantiene la interfaz y el consumo de recursos reducidos.

## Compilar

Requiere Windows x64 y el SDK de .NET 10. El script obtiene FFmpeg y FFprobe y verifica su SHA-256. Si ya tienes ambos ejecutables en una carpeta, indícala con `-MediaTools`.

```powershell
.\native\publish.ps1
```

La carpeta `release/PulseCut-Windows-x64` contiene el ejecutable y las herramientas que necesita.

## Generar el instalador

Con .NET 10 e Inno Setup 6.3 o posterior instalados, ejecuta:

```powershell
.\native\build-installer.ps1
```

El script descarga las herramientas multimedia, comprueba su SHA-256, prueba las exportaciones y genera `release/installer/PulseCut-<versión>-setup-win-x64.exe`. El instalador incluye PulseCut, el runtime de .NET, FFmpeg, FFprobe y sus avisos de licencia.

El workflow `release.yml` crea una release al recibir un tag `v<versión>` que coincide con la versión del proyecto. Adjunta únicamente el instalador. La ejecución manual compila y guarda el instalador como artefacto de Actions para probarlo.

## Flujo de ramas

- `main` contiene el código de las versiones publicadas. Cada versión se identifica con su etiqueta.
- `develop` integra los cambios de la próxima versión.
- `feature/*` parte de `develop` y vuelve a `develop` cuando el cambio está revisado.
- `release/*` parte de `develop`. Al publicar, se integra en `main`, se etiqueta la versión y se incorporan los ajustes a `develop`.
- `hotfix/*` parte de `main` y lleva la corrección a `main` y a `develop`.

Usa Conventional Commits con el asunto en español, por ejemplo `feat(editor): añade tiradores de recorte` o `fix(ventana): corrige el tamaño al restaurar`. Cada commit debe describir un cambio concreto.

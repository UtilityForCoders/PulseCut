# PulseCut 0.1.0

La edición de escritorio en C# y WPF se desarrolla en `feature/editor-escritorio`. Consulta [la guía de la edición nativa](native/README.md) para compilarla y conocer el flujo de ramas. El resto de esta página describe la release 0.1.0.

Aplicación de escritorio para Windows que permite importar video y audio, recortar un tramo, cambiar su velocidad y exportar el resultado. El procesamiento se realiza en el equipo con FFmpeg.

## Ejecutar el programa

Descarga el ejecutable desde la [release 0.1.0 en GitHub](https://github.com/UtilityForCoders/PulseCut/releases/tag/v0.1.0). El código fuente del repositorio no contiene los binarios compilados.

El ejecutable portátil se encuentra en:

```text
release\PulseCut-0.1.0-portable.exe
```

1. Abre la carpeta `release`.
2. Ejecuta `PulseCut-0.1.0-portable.exe` con doble clic.
3. Espera a que aparezca la ventana de PulseCut. El primer inicio puede tardar mientras se extraen los componentes.

El ejecutable incluye Electron, Node.js, FFmpeg y FFprobe. No necesita instalación, permisos de administrador, Node.js instalado ni conexión a internet. No abre una consola ni un navegador externo.

También se genera una distribución sin comprimir:

```text
release\win-unpacked\PulseCut.exe
```

Esta alternativa inicia sin la extracción del archivo portátil. Debe conservarse la carpeta `win-unpacked` completa. No copies únicamente su `PulseCut.exe`.

La compilación es para Windows 10 o Windows 11 de 64 bits, en equipos x64. No incluye firma digital ni actualizaciones automáticas. Windows puede mostrar una advertencia de editor desconocido. La compatibilidad con equipos ARM y versiones anteriores de Windows no está verificada.

## Funciones

- Importar varios archivos mediante selección o arrastre.
- Editar un archivo por vez.
- Reproducir, pausar, silenciar y repetir el tramo seleccionado.
- Mostrar video en pantalla completa.
- Ajustar inicio y fin mediante tiempos en segundos o extremos arrastrables.
- Cambiar la velocidad a 0,25x, 0,5x, 0,75x, 1x, 1,25x, 1,5x, 2x, 3x o 4x.
- Mantener el tono del audio al cambiar la velocidad.
- Exportar a MP4, MP3 o WAV.
- Elegir codificación de video rápida o de mayor calidad.
- Consultar el progreso y cancelar una exportación.
- Elegir la ubicación de guardado mediante el diálogo de Windows.

## Formatos

| Operación | Compatibilidad |
| --- | --- |
| Entrada | Video y audio reconocidos por la compilación incluida de FFmpeg. Incluye MP4, MOV, MKV, WEBM, AVI, MP3, M4A, WAV y FLAC. |
| Video de salida | MP4 con video H.264 y audio AAC, si el origen contiene audio. |
| Audio de salida | MP3 a 192 kbit/s o WAV PCM de 16 bits. Requiere una pista de audio en el origen. |
| Tamaño de entrada | Hasta 4 GB por archivo. |
| Vista previa | Depende de los códecs admitidos por Chromium. La extensión por sí sola no garantiza reproducción. |

Un archivo que no se reproduzca en la vista previa puede recortarse mediante los campos de inicio y fin y exportarse, siempre que FFmpeg pueda leerlo.

## Uso

1. Pulsa `Importar archivos` o arrastra los archivos a la ventana.
2. Selecciona un archivo de la biblioteca.
3. Define el inicio y el fin del tramo que deseas conservar.
4. Elige la velocidad. La duración final se actualiza en los ajustes.
5. Selecciona el formato de salida y, para video, la calidad de codificación.
6. Pulsa `Exportar clip`.
7. Cuando termine, pulsa `Descargar clip` y selecciona dónde guardarlo.

Los cambios no modifican el archivo original. Quitar un archivo de la biblioteca elimina su copia temporal, no el original.

## Atajos

| Tecla | Acción |
| --- | --- |
| Espacio | Reproducir o pausar. |
| I | Marcar el inicio en la posición actual. |
| O | Marcar el fin en la posición actual. |
| Flecha izquierda o derecha | Ajustar el extremo del recorte enfocado en intervalos de 0,1 segundos. |

Los atajos de reproducción y marcado no se activan mientras se escribe en un campo o se utiliza otro control.

## Archivos temporales y privacidad

En la aplicación de escritorio, las copias de trabajo y las exportaciones temporales se almacenan en:

```text
%APPDATA%\PulseCut\media
```

El programa ejecuta un servidor interno en `127.0.0.1` con un puerto libre asignado automáticamente. La ventana utiliza ese servidor local. No escucha en la red local y no envía archivos a servicios externos. Todos los recursos de la interfaz son locales.

La biblioteca y el historial de exportaciones se mantienen en memoria y se pierden al cerrar la aplicación. Los archivos descargados permanecen en la ubicación elegida. Los temporales con más de 24 horas se eliminan al iniciar PulseCut. Cerrar la ventana detiene las exportaciones en curso.

Reserva espacio para el archivo original, su copia temporal y el resultado. El ejecutable portátil también extrae temporalmente los componentes necesarios para iniciarse.

## Limitaciones de esta versión

- No guarda proyectos ni recupera la sesión anterior.
- No une clips, mezcla pistas, agrega efectos ni incorpora subtítulos.
- Solo procesa una exportación a la vez.
- Vuelve a codificar los archivos para aplicar el recorte y la velocidad.
- No incluye aceleración por GPU ni corte sin recodificación.
- La línea de tiempo muestra marcas de posición; no representa una forma de onda del audio.
- El tiempo de exportación depende de la duración, resolución, códec y capacidad del equipo. No se han realizado mediciones comparativas de rendimiento.

## Desarrollo

Requiere Windows x64, Node.js 22 o superior y npm. La instalación de dependencias y la primera compilación requieren internet.

```powershell
npm.cmd ci
npm.cmd start
```

Si npm bloquea los scripts de descarga y faltan los binarios, ejecuta:

```powershell
node node_modules/electron/install.js
node node_modules/ffmpeg-static/install.js
```

Para usar únicamente el servidor y un navegador:

```powershell
npm.cmd run start:web
```

Después abre `http://127.0.0.1:3210`. En este modo, los temporales se guardan en `.pulsecut` dentro del proyecto. Para detener el servidor, pulsa Ctrl+C.

`iniciar.bat` es una alternativa para iniciar el código fuente; necesita Node.js y las dependencias. Para usar la distribución compilada, ejecuta el archivo de `release`.

## Generar el ejecutable

```powershell
npm.cmd run build:win
```

La configuración de empaquetado está en `electron-builder.yml`. El comando genera el ejecutable portátil y la carpeta `release\win-unpacked`. Los binarios de FFmpeg y FFprobe se copian a `resources\bin` fuera del archivo ASAR para poder ejecutarlos como procesos.

Para generar solo la carpeta sin comprimir:

```powershell
npm.cmd run build:dir
```

## Verificación

Pruebas del motor:

```powershell
npm.cmd test
```

Las pruebas generan muestras sintéticas, importan video y audio y verifican exportaciones reales mediante FFprobe. Comprueban duración, códecs, validación de parámetros, reproducción por rangos y rechazo de archivos inválidos.

Prueba del ejecutable compilado:

```powershell
.\release\win-unpacked\PulseCut.exe --smoke-test="$PWD\artifacts\desktop-smoke.json"
```

La carpeta `artifacts` debe existir. Esta prueba abre una ventana oculta, comprueba la interfaz y su aislamiento, genera una muestra con el FFmpeg incluido, importa el archivo, recorta del segundo 1 al 3, aplica velocidad 2x y verifica un resultado de aproximadamente 1 segundo con video y audio. Escribe el informe JSON y cierra la aplicación. Usa un perfil de prueba independiente de la sesión normal.

## Estructura

```text
desktop/main.js          Ventana de escritorio, servidor y guardado de archivos.
desktop/smoke.js         Prueba funcional del ejecutable.
public/                 Interfaz HTML, CSS y JavaScript.
server.js               Importación, metadatos y exportación con FFmpeg.
test/editor.test.js     Pruebas del motor.
electron-builder.yml    Configuración del ejecutable de Windows.
iniciar.bat             Inicio del código fuente en Windows.
release/                Archivos compilados.
```

## Diagnóstico

Si la aplicación falla al iniciar, revisa:

```text
%APPDATA%\PulseCut\error.log
```

Si falla una exportación, comprueba el formato de origen, el espacio disponible y que el archivo tenga la pista de video o audio requerida. Si falla el guardado, elige una carpeta con permisos de escritura y vuelve a descargar el clip mientras la sesión siga abierta.

## Componentes de terceros

Consulta `THIRD-PARTY-NOTICES.md`. El ejecutable incluye FFmpeg y FFprobe con sus licencias respectivas. La release adjunta `THIRD-PARTY-LICENSES.zip` con licencias, versiones, configuraciones y avisos de los componentes incluidos.

## Publicación

`scripts/github-release.py` publica una release desde un tag ya subido y verifica el SHA-256 de cada archivo adjunto. Requiere Python 3.11 o superior y una cuenta con permisos de escritura configurada en Git Credential Manager. No almacena ni imprime credenciales. Crea primero un borrador, adjunta los archivos y publica la release cuando termina de verificarlos.

Las notas de la primera versión están en `RELEASE_NOTES.md`. El historial se mantiene en `CHANGELOG.md`.

Después de compilar y verificar los ejecutables, `python scripts/package-release.py` recopila la documentación, los avisos de terceros y las sumas SHA-256 en `release`.

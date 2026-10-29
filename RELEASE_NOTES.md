Primera versión de PulseCut para Windows x64. Permite importar videos y audios, conservar un tramo, cambiar su velocidad y exportar el resultado mediante procesamiento local.

## Descarga y ejecución

Descarga `PulseCut-0.1.0-portable.exe` desde los archivos adjuntos y ejecútalo con doble clic. No necesita instalación, Node.js ni FFmpeg por separado. La aplicación abre una ventana propia. El primer inicio puede tardar mientras extrae los componentes incluidos.

## Funciones

- Importación múltiple mediante selección o arrastre; edición de un archivo por vez.
- Recorte con tiempos exactos y controles arrastrables.
- Velocidades de 0,25x a 4x con preservación del tono del audio.
- Exportación a MP4 con H.264/AAC, MP3 o WAV.
- Progreso, cancelación y selección de la ubicación de guardado.
- Reproducción, silencio, repetición y pantalla completa de video.
- Funcionamiento local y sin conexión.

## Verificación

Las cinco pruebas del motor pasaron. También se comprobaron el arranque y la exportación desde el ejecutable portátil y desde la distribución sin comprimir. Un recorte de 2 segundos a velocidad 2x produjo un archivo de 1,067 segundos con video H.264 y audio.

Consulta `VERIFICACION.md` y los informes JSON adjuntos. `SHA256SUMS.txt` permite comprobar la integridad del ejecutable. `THIRD-PARTY-LICENSES.zip` contiene los avisos y la información de procedencia de los componentes incluidos.

## Límites

Versión inicial para pruebas de uso. No incluye firma digital, actualizaciones automáticas, proyectos guardados, unión de clips, múltiples pistas ni aceleración por GPU. Solo procesa una exportación a la vez. La compatibilidad de la vista previa depende del códec. Cerrar la aplicación detiene las exportaciones en curso. La biblioteca se vacía al cerrar; los archivos ya guardados se conservan.

El ejecutable está dirigido a Windows 10 y Windows 11 de 64 bits en equipos x64. No se ha verificado en una segunda computadora ni en equipos ARM.

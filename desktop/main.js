import { app, BrowserWindow, dialog, Menu, session } from 'electron';
import { access, appendFile, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

app.setName('PulseCut');
const smokeArg = process.argv.find(arg => arg.startsWith('--smoke-test='));
const smokeReport = smokeArg?.slice('--smoke-test='.length);
if (smokeReport) app.setPath('userData', path.join(path.dirname(smokeReport), 'smoke-user-data'));
let window, server, stopProcessing;
let shuttingDown = false;

async function logError(err) {
  const text = `${new Date().toISOString()} ${err.stack || err}\n`;
  await mkdir(app.getPath('userData'), { recursive: true });
  await appendFile(path.join(app.getPath('userData'), 'error.log'), text).catch(() => {});
}
async function start() {
  await app.whenReady();
  process.env.PULSECUT_DATA_DIR = path.join(app.getPath('userData'), 'media');
  if (app.isPackaged) {
    process.env.PULSECUT_FFMPEG_PATH = path.join(process.resourcesPath, 'bin', 'ffmpeg.exe');
    process.env.PULSECUT_FFPROBE_PATH = path.join(process.resourcesPath, 'bin', 'ffprobe.exe');
    await Promise.all([access(process.env.PULSECUT_FFMPEG_PATH), access(process.env.PULSECUT_FFPROBE_PATH)]);
  }
  const backend = await import('../server.js');
  stopProcessing = backend.stopProcessing;
  server = backend.app.listen(0, '127.0.0.1');
  await new Promise((resolve, reject) => { server.once('listening', resolve); server.once('error', reject); });
  const origin = `http://127.0.0.1:${server.address().port}`;
  session.defaultSession.setPermissionRequestHandler((_contents, _permission, callback) => callback(false));
  session.defaultSession.setPermissionCheckHandler(() => false);
  // All application assets are bundled. The desktop app never needs remote requests.
  session.defaultSession.webRequest.onBeforeRequest((details, callback) => {
    callback({ cancel: !details.url.startsWith(`${origin}/`) && !details.url.startsWith('devtools://') });
  });
  Menu.setApplicationMenu(null);
  window = new BrowserWindow({
    title: `PulseCut ${app.getVersion()}`,
    width: 1440, height: 1000, minWidth: 800, minHeight: 650,
    backgroundColor: '#101211', show: false, autoHideMenuBar: true,
    webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true, webSecurity: true, devTools: !app.isPackaged },
  });
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', (event, url) => { if (new URL(url).origin !== origin) event.preventDefault(); });
  window.on('close', () => {
    if (shuttingDown || smokeReport) return;
    // Closing during an export stops it. This is also stated in the README.
    stopProcessing?.();
  });
  session.defaultSession.on('will-download', (_event, item) => {
    item.setSaveDialogOptions({ title: 'Guardar clip de PulseCut', defaultPath: path.join(app.getPath('downloads'), path.basename(item.getFilename())) });
    item.on('done', (_event, state) => {
      if (state === 'interrupted') dialog.showErrorBox('No se pudo guardar el clip', 'Comprueba la carpeta de destino y el espacio disponible. Puedes volver a descargarlo.');
    });
  });
  await window.loadURL(origin);
  if (smokeReport) {
    const { smokeTest } = await import('./smoke.js');
    const result = await smokeTest({ window, origin, packaged: app.isPackaged, version: app.getVersion() });
    await writeFile(smokeReport, JSON.stringify(result, null, 2));
    app.quit();
  } else window.show();
}

if (!smokeReport && !app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { if (window) { if (window.isMinimized()) window.restore(); window.show(); window.focus(); } });
  app.on('window-all-closed', () => app.quit());
  app.on('before-quit', () => { shuttingDown = true; stopProcessing?.(); server?.close(); });
  start().catch(async err => {
    await logError(err);
    if (smokeReport) await writeFile(smokeReport, JSON.stringify({ ok: false, error: err.stack || String(err) })).catch(() => {});
    else dialog.showErrorBox('PulseCut no pudo iniciarse', `${err.message}\n\nDetalle: ${path.join(app.getPath('userData'), 'error.log')}`);
    app.exit(1);
  });
}

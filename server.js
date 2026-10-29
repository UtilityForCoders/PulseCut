import express from 'express';
import multer from 'multer';
import ffmpeg from 'ffmpeg-static';
import ffprobe from 'ffprobe-static';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdir, unlink, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const data = process.env.PULSECUT_DATA_DIR || path.join(root, '.pulsecut');
const ffmpegPath = process.env.PULSECUT_FFMPEG_PATH || ffmpeg;
const ffprobePath = process.env.PULSECUT_FFPROBE_PATH || ffprobe.path;
await mkdir(data, { recursive: true });
const app = express();
const files = new Map();
const jobs = new Map();
app.use((req, res, next) => {
  if (!['127.0.0.1', 'localhost'].includes(req.hostname)) return res.sendStatus(403);
  if (req.headers.origin && req.headers.origin !== `http://${req.headers.host}`) return res.sendStatus(403);
  res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; media-src 'self' blob:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'");
  res.setHeader('X-Content-Type-Options', 'nosniff');
  next();
});
app.use(express.json());
app.use(express.static(path.join(root, 'public')));
const upload = multer({ dest: data, limits: { fileSize: 4 * 1024 ** 3, files: 1 } });
function probe(file) {
  return new Promise((resolve, reject) => {
    const child = spawn(ffprobePath, ['-v', 'error', '-show_format', '-show_streams', '-of', 'json', file], { windowsHide: true });
    let out = '';
    child.stdout.on('data', chunk => out += chunk);
    child.on('error', reject);
    child.on('close', code => { try { code === 0 ? resolve(JSON.parse(out)) : reject(new Error('Archivo no compatible o dañado.')); } catch (err) { reject(err); } });
  });
}
app.get('/api/files', (req, res) => res.json([...files.values()]));
app.post('/api/files', upload.single('file'), async (req, res, next) => {
  try {
    if (!req.file) return res.status(400).json({ error: 'Selecciona un archivo.' });
    const info = await probe(req.file.path);
    const video = info.streams.find(s => s.codec_type === 'video' && !s.disposition?.attached_pic);
    const audio = info.streams.find(s => s.codec_type === 'audio');
    const duration = Number(info.format.duration);
    if ((!video && !audio) || !Number.isFinite(duration) || duration <= 0) throw new Error('El archivo no contiene video o audio con duración válida.');
    const id = path.basename(req.file.path);
    const file = { id, name: req.file.originalname, size: req.file.size, duration, video: !!video, audio: !!audio, width: video?.width, height: video?.height, codec: video?.codec_name || audio?.codec_name };
    files.set(id, file);
    res.json(file);
  } catch (err) { if (req.file) await unlink(req.file.path).catch(() => {}); next(err); }
});
app.get('/api/files/:id/media', (req, res) => {
  const file = files.get(req.params.id);
  if (!file) return res.sendStatus(404);
  res.type(path.extname(file.name) || (file.video ? '.mp4' : '.mp3'));
  res.sendFile(path.join(data, file.id), { dotfiles: 'allow' });
});
app.delete('/api/files/:id', async (req, res) => {
  if ([...jobs.values()].some(j => j.fileId === req.params.id && j.status === 'processing')) return res.status(409).json({ error: 'Espera a que termine la exportación.' });
  if (!files.has(req.params.id)) return res.sendStatus(404);
  await unlink(path.join(data, req.params.id)).catch(() => {});
  files.delete(req.params.id);
  res.sendStatus(204);
});
export function exportArgs(file, input, output, options) {
  const { start, end, speed, format, quality } = options;
  const args = ['-hide_banner', '-y', '-ss', String(start), '-t', String(end - start), '-i', input];
  if (format === 'mp4') args.push('-map', '0:v:0', '-vf', `setpts=(PTS-STARTPTS)/${speed},scale=trunc(iw/2)*2:trunc(ih/2)*2`, '-c:v', 'libx264', '-preset', quality === 'fast' ? 'ultrafast' : 'veryfast', '-crf', quality === 'fast' ? '25' : '20', '-pix_fmt', 'yuv420p');
  else args.push('-vn');
  if (file.audio) {
    const tempo = speed < 0.5 ? 'atempo=0.5,atempo=0.5' : speed > 2 ? `atempo=2,atempo=${speed / 2}` : `atempo=${speed}`;
    args.push('-map', '0:a:0', '-af', `asetpts=PTS-STARTPTS,${tempo}`, '-c:a', format === 'wav' ? 'pcm_s16le' : format === 'mp3' ? 'libmp3lame' : 'aac');
    if (format !== 'wav') args.push('-b:a', '192k');
  } else args.push('-an');
  if (format === 'mp4') args.push('-movflags', '+faststart');
  args.push('-progress', 'pipe:1', '-nostats', output);
  return args;
}
app.post('/api/exports', (req, res) => {
  const { fileId, start, end, speed, format, quality } = req.body;
  const file = files.get(fileId);
  if (!file) return res.status(404).json({ error: 'Archivo no encontrado.' });
  if (![start, end, speed].every(Number.isFinite) || start < 0 || end > file.duration + 0.01 || end <= start || ![0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4].includes(speed) || !['mp4', 'mp3', 'wav'].includes(format) || (format === 'mp4' ? !file.video : !file.audio)) return res.status(400).json({ error: 'Revisa el tramo, la velocidad y el formato.' });
  if ([...jobs.values()].some(j => j.status === 'processing')) return res.status(409).json({ error: 'Ya hay una exportación en curso.' });
  const id = randomUUID();
  const output = path.join(data, `${id}.${format}`);
  const job = { id, fileId, status: 'processing', progress: 0, format, name: `${path.parse(file.name).name}-pulsecut.${format}` };
  jobs.set(id, job);
  const child = spawn(ffmpegPath, exportArgs(file, path.join(data, fileId), output, { start, end, speed, format, quality }), { windowsHide: true });
  job.child = child;
  let buffer = '';
  child.stdout.on('data', chunk => {
    buffer += chunk;
    const lines = buffer.split('\n'); buffer = lines.pop();
    for (const line of lines) if (line.startsWith('out_time_us=')) job.progress = Math.min(99, Math.round(Number(line.split('=')[1]) / 1e6 / ((end - start) / speed) * 100));
  });
  child.stderr.resume();
  child.on('error', () => { job.status = 'error'; job.error = 'No se pudo iniciar el motor de exportación.'; });
  child.on('close', async code => {
    delete job.child;
    if (job.status === 'cancelled') { await unlink(output).catch(() => {}); return; }
    job.status = code === 0 ? 'done' : 'error';
    job.progress = code === 0 ? 100 : job.progress;
    if (code !== 0) { job.error = 'No se pudo procesar este archivo. Prueba con otro formato.'; await unlink(output).catch(() => {}); }
  });
  res.json({ id });
});
app.get('/api/exports/:id', (req, res) => {
  const job = jobs.get(req.params.id);
  if (!job) return res.sendStatus(404);
  const { child, ...safe } = job;
  res.json(safe);
});
app.delete('/api/exports/:id', (req, res) => {
  const job = jobs.get(req.params.id);
  if (!job) return res.sendStatus(404);
  if (job.status === 'processing') { job.status = 'cancelled'; job.child?.kill(); }
  res.sendStatus(204);
});
app.get('/api/exports/:id/download', async (req, res) => {
  const job = jobs.get(req.params.id);
  if (!job || job.status !== 'done') return res.sendStatus(404);
  res.download(path.join(data, `${job.id}.${job.format}`), job.name, { dotfiles: 'allow' });
});
app.use((err, req, res, next) => res.status(400).json({ error: err.code === 'LIMIT_FILE_SIZE' ? 'El máximo por archivo es 4 GB.' : err.message || 'No se pudo completar la operación.' }));
// Remove temporary files left by previous sessions after 24 hours.
const { readdir } = await import('node:fs/promises');
for (const name of await readdir(data)) {
  const file = path.join(data, name);
  const info = await stat(file);
  if (info.isFile() && Date.now() - info.mtimeMs > 86400000) await unlink(file).catch(() => {});
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT || 3210);
  app.listen(port, '127.0.0.1', () => console.log(`PulseCut listo en http://127.0.0.1:${port}`));
}
function stopProcessing() {
  for (const job of jobs.values()) if (job.child) { job.status = 'cancelled'; job.child.kill(); }
}
export { app, stopProcessing };

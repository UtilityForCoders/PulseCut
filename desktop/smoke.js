import { spawn } from 'node:child_process';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

function run(bin, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(bin, args, { windowsHide: true }); let out = '', err = '';
    child.stdout.on('data', data => out += data); child.stderr.on('data', data => err += data);
    child.on('error', reject); child.on('close', code => code === 0 ? resolve(out) : reject(new Error(err)));
  });
}
export async function smokeTest({ window, origin, packaged, version }) {
  const ffmpeg = process.env.PULSECUT_FFMPEG_PATH || (await import('ffmpeg-static')).default;
  const ffprobe = process.env.PULSECUT_FFPROBE_PATH || (await import('ffprobe-static')).default.path;
  const dir = process.env.PULSECUT_DATA_DIR;
  const fixture = path.join(dir, 'smoke-sample.mp4');
  await run(ffmpeg, ['-y', '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=30:duration=4', '-f', 'lavfi', '-i', 'sine=frequency=440:duration=4', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-shortest', fixture]);
  const page = await window.webContents.executeJavaScript('({ title: document.title, heading: document.querySelector("h1").textContent, importButton: !!document.getElementById("importButton"), nodeAvailable: typeof require !== "undefined" })');
  assert.ok(page.title.includes('PulseCut')); assert.ok(page.importButton); assert.equal(page.nodeAvailable, false);
  const form = new FormData(); form.append('file', new Blob([await readFile(fixture)]), 'smoke-sample.mp4');
  const imported = await fetch(`${origin}/api/files`, { method: 'POST', body: form }); assert.equal(imported.status, 200);
  const file = await imported.json();
  const response = await fetch(`${origin}/api/exports`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId: file.id, start: 1, end: 3, speed: 2, format: 'mp4', quality: 'fast' }) });
  assert.equal(response.status, 200); const { id } = await response.json();
  const deadline = Date.now() + 30000;
  let output;
  while (Date.now() < deadline) {
    const job = await (await fetch(`${origin}/api/exports/${id}`)).json();
    assert.notEqual(job.status, 'error', job.error);
    if (job.status === 'done') {
      const download = await fetch(`${origin}/api/exports/${id}/download`); assert.equal(download.status, 200);
      output = path.join(dir, 'smoke-result.mp4'); await writeFile(output, Buffer.from(await download.arrayBuffer())); break;
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.ok(output, 'Exportación no completada');
  const info = JSON.parse(await run(ffprobe, ['-v', 'error', '-show_format', '-show_streams', '-of', 'json', output]));
  assert.ok(Math.abs(Number(info.format.duration) - 1) < 0.2);
  assert.ok(info.streams.some(stream => stream.codec_name === 'h264'));
  assert.ok(info.streams.some(stream => stream.codec_type === 'audio'));
  await fetch(`${origin}/api/files/${file.id}`, { method: 'DELETE' });
  return { ok: true, packaged, version, page, duration: Number(info.format.duration), checks: ['desktop-window', 'renderer-isolation', 'bundled-ffmpeg', 'bundled-ffprobe', 'import-video', 'trim-and-speed', 'download-output'] };
}

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import ffmpeg from 'ffmpeg-static';
import ffprobe from 'ffprobe-static';
import { app } from '../server.js';

const dir = path.resolve('test-output');
let server, base;
function run(bin, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(bin, args); let out = '', err = '';
    child.stdout.on('data', chunk => out += chunk); child.stderr.on('data', chunk => err += chunk);
    child.on('error', reject); child.on('close', code => code === 0 ? resolve(out) : reject(new Error(err)));
  });
}
async function upload(name) {
  const data = new FormData(); data.append('file', new Blob([await readFile(path.join(dir, name))]), name);
  const response = await fetch(`${base}/api/files`, { method: 'POST', body: data });
  assert.equal(response.status, 200); return response.json();
}
async function exportClip(file, format, speed, start = 1, end = 3) {
  const response = await fetch(`${base}/api/exports`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId: file.id, start, end, speed, format, quality: 'fast' }) });
  assert.equal(response.status, 200); const { id } = await response.json();
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    const job = await (await fetch(`${base}/api/exports/${id}`)).json();
    assert.notEqual(job.status, 'error', job.error);
    if (job.status === 'done') {
      const result = await fetch(`${base}/api/exports/${id}/download`); assert.equal(result.status, 200);
      const filePath = path.join(dir, `${id}.${format}`); await writeFile(filePath, Buffer.from(await result.arrayBuffer()));
      return JSON.parse(await run(ffprobe.path, ['-v', 'error', '-show_format', '-show_streams', '-of', 'json', filePath]));
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.fail('Export timed out');
}
before(async () => {
  await mkdir(dir, { recursive: true });
  await run(ffmpeg, ['-y', '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=30:duration=4', '-f', 'lavfi', '-i', 'sine=frequency=440:duration=4', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-shortest', path.join(dir, 'sample.mp4')]);
  await run(ffmpeg, ['-y', '-f', 'lavfi', '-i', 'sine=frequency=880:duration=4', path.join(dir, 'sample.wav')]);
  await run(ffmpeg, ['-y', '-f', 'lavfi', '-i', 'color=c=green:size=320x180:rate=25:duration=4', '-c:v', 'libx264', path.join(dir, 'silent.mp4')]);
  server = app.listen(0, '127.0.0.1'); await new Promise(resolve => server.on('listening', resolve));
  base = `http://127.0.0.1:${server.address().port}`;
});
after(async () => {
  const files = await (await fetch(`${base}/api/files`)).json();
  for (const file of files) await fetch(`${base}/api/files/${file.id}`, { method: 'DELETE' });
  await new Promise(resolve => server.close(resolve));
});
test('serves app and rejects requests from other origins', async () => {
  assert.equal((await fetch(base)).status, 200);
  assert.equal((await fetch(`${base}/api/files`, { headers: { Origin: 'https://example.com' } })).status, 403);
});
test('imports video, supports range playback and exports a trimmed 2x MP4', async () => {
  const file = await upload('sample.mp4'); assert.equal(file.video, true); assert.equal(file.audio, true);
  assert.equal((await fetch(`${base}/api/files/${file.id}/media`, { headers: { Range: 'bytes=0-99' } })).status, 206);
  const result = await exportClip(file, 'mp4', 2);
  assert.ok(Math.abs(Number(result.format.duration) - 1) < 0.2);
  assert.ok(result.streams.some(s => s.codec_name === 'h264'));
  assert.ok(result.streams.some(s => s.codec_type === 'audio'));
});
test('exports audio at 0.25x and 4x with expected duration', async () => {
  const file = await upload('sample.wav'); assert.equal(file.video, false);
  const slow = await exportClip(file, 'wav', 0.25); assert.ok(Math.abs(Number(slow.format.duration) - 8) < 0.3);
  const fast = await exportClip(file, 'mp3', 4); assert.ok(Math.abs(Number(fast.format.duration) - 0.5) < 0.2);
});
test('exports silent video, validates incompatible output and bad trim', async () => {
  const file = await upload('silent.mp4'); assert.equal(file.audio, false);
  const result = await exportClip(file, 'mp4', 0.5); assert.ok(Math.abs(Number(result.format.duration) - 4) < 0.2);
  for (const overrides of [{ format: 'mp3' }, { start: 3, end: 1 }, { speed: 99 }, { end: 900 }]) {
    const res = await fetch(`${base}/api/exports`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId: file.id, start: 0, end: 2, speed: 1, format: 'mp4', ...overrides }) });
    assert.equal(res.status, 400);
  }
  assert.equal((await fetch(`${base}/api/files/${file.id}`, { method: 'DELETE' })).status, 204);
  assert.equal((await fetch(`${base}/api/files/${file.id}/media`)).status, 404);
});
test('rejects non-media uploads without retaining them', async () => {
  const data = new FormData(); data.append('file', new Blob(['not media']), 'text.txt');
  const response = await fetch(`${base}/api/files`, { method: 'POST', body: data }); assert.equal(response.status, 400);
});

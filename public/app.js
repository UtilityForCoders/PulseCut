const $ = id => document.getElementById(id);
const speeds = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];
let files = [], active = null, start = 0, end = 0, speed = 1, busy = false, importing = false, jobId = null, repeat = false;
const player = $('player');
const time = seconds => { const n = Math.max(0, seconds || 0); return `${String(Math.floor(n / 60)).padStart(2, '0')}:${(n % 60).toFixed(2).padStart(5, '0')}`; };
const size = n => n < 1024 ** 2 ? `${Math.round(n / 1024)} KB` : `${(n / 1024 ** 2).toFixed(1)} MB`;
function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toast.timer); toast.timer = setTimeout(() => $('toast').hidden = true, 5500); }
async function api(url, options) {
  const res = await fetch(url, options);
  if (!res.ok) { let error; try { error = (await res.json()).error; } catch {} throw new Error(error || 'No se pudo conectar con PulseCut.'); }
  return res.status === 204 ? null : res.json();
}
function renderLibrary() {
  $('fileCount').textContent = files.length;
  $('fileList').replaceChildren();
  for (const file of files) {
    const row = document.createElement('div'); row.className = `file-row${active?.id === file.id ? ' selected' : ''}`;
    const button = document.createElement('button'); button.className = 'file-select'; button.disabled = busy; button.setAttribute('aria-pressed', active?.id === file.id ? 'true' : 'false');
    const icon = document.createElement('span'); icon.className = 'file-icon'; icon.textContent = file.video ? '▷' : '♫';
    const info = document.createElement('span'); info.className = 'file-info';
    const name = document.createElement('strong'); name.textContent = file.name; name.title = file.name;
    const detail = document.createElement('small'); detail.textContent = `${time(file.duration)} · ${size(file.size)}`;
    info.append(name, detail); button.append(icon, info); button.onclick = () => selectFile(file);
    const remove = document.createElement('button'); remove.className = 'delete-file'; remove.textContent = '×'; remove.disabled = busy; remove.setAttribute('aria-label', `Quitar ${file.name}`);
    remove.onclick = async () => { try { await api(`/api/files/${file.id}`, { method: 'DELETE' }); files = files.filter(f => f.id !== file.id); if (active?.id === file.id) { selectFile(files[0] || null); } renderLibrary(); } catch (err) { toast(err.message); } };
    row.append(button, remove); $('fileList').append(row);
  }
}
function selectFile(file) {
  if (busy) return;
  player.pause(); active = file;
  $('exportStatus').replaceChildren();
  $('previewWarning').hidden = true;
  $('previewEmpty').hidden = !!file;
  $('audioArt').hidden = !file || file.video;
  player.hidden = !file || !file.video;
  $('previewTitle').textContent = file?.name || 'Vista previa';
  $('mediaTag').textContent = file ? (file.video ? `${file.width} × ${file.height}` : file.codec.toUpperCase()) : 'SIN ARCHIVO';
  if (file) {
    player.src = `/api/files/${file.id}/media`;
    $('audioName').textContent = file.name;
    $('format').replaceChildren();
    for (const [value, label] of [['mp4', 'MP4 · Video'], ['mp3', 'MP3 · Audio'], ['wav', 'WAV · Audio sin compresión']]) {
      if ((value === 'mp4' && file.video) || (value !== 'mp4' && file.audio)) $('format').add(new Option(label, value));
    }
  } else { player.removeAttribute('src'); player.load(); }
  reset(); renderLibrary();
}
function reset() {
  start = 0; end = active?.duration || 0; speed = 1;
  $('speed').value = 3; player.playbackRate = 1; player.currentTime = 0;
  $('start').max = end; $('end').max = end;
  $('totalTime').textContent = time(end); $('currentTime').textContent = time(0);
  $('playhead').style.left = '0%';
  // Uniform marks indicate the clip's time grid, not an audio waveform.
  $('wave').replaceChildren();
  if (active) for (let i = 0; i < 90; i++) { const mark = document.createElement('i'); mark.style.height = i % 5 === 0 ? '100%' : '45%'; $('wave').append(mark); }
  for (const [i, label] of [...$('ruler').children].entries()) label.textContent = time(end * i / 4).slice(0, -3);
  sync();
}
function sync(updateInputs = true) {
  $('controls').disabled = !active || busy;
  for (const id of ['play', 'loop', 'mute', 'reset', 'leftHandle', 'rightHandle']) $(id).disabled = !active || busy;
  $('fullscreen').disabled = !active?.video || busy;
  if (updateInputs) { $('start').value = start.toFixed(2); $('end').value = end.toFixed(2); }
  $('selection').style.left = `${active ? start / active.duration * 100 : 0}%`;
  $('selection').style.right = `${active ? (1 - end / active.duration) * 100 : 0}%`;
  $('selectionLabel').textContent = active ? `${time(start)} → ${time(end)}` : 'Selecciona el tramo que quieres conservar';
  $('outputDuration').textContent = time((end - start) / speed);
  $('speedValue').replaceChildren(document.createTextNode(String(speed).replace('.', ',')), Object.assign(document.createElement('span'), { textContent: '×' }));
  $('speedDescription').textContent = speed === 1 ? 'Velocidad original' : speed < 1 ? 'Un poco más despacio' : 'Acelera el ritmo';
  document.querySelectorAll('[data-speed]').forEach(b => b.classList.toggle('active', speeds[Number(b.dataset.speed)] === speed));
  $('quality').disabled = !active || busy || $('format').value !== 'mp4';
}
async function importFiles(list) {
  if (importing || busy) { toast('Espera a que termine la operación actual.'); return; }
  importing = true; $('dropzone').disabled = true; $('addSmall').disabled = true; $('importButton').disabled = true;
  for (const [index, file] of [...list].entries()) {
    $('uploadStatus').textContent = `Importando ${index + 1}/${list.length} · ${file.name}`;
    try {
      const result = await new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest(); xhr.open('POST', '/api/files');
        xhr.upload.onprogress = e => { if (e.lengthComputable) $('uploadStatus').textContent = `${file.name} · ${Math.round(e.loaded / e.total * 100)} %`; };
        xhr.onload = () => { try { const body = JSON.parse(xhr.responseText); xhr.status === 200 ? resolve(body) : reject(new Error(body.error)); } catch { reject(new Error('No se pudo importar el archivo.')); } };
        xhr.onerror = () => reject(new Error('No se pudo conectar con el servidor local.'));
        const form = new FormData(); form.append('file', file); xhr.send(form);
      });
      files.push(result); if (!active) selectFile(result); else renderLibrary();
    } catch (err) { toast(`${file.name}: ${err.message}`); }
  }
  importing = false; $('dropzone').disabled = false; $('addSmall').disabled = false; $('importButton').disabled = false; $('uploadStatus').textContent = ''; $('fileInput').value = '';
}
for (const id of ['dropzone', 'addSmall', 'importButton']) $(id).onclick = () => $('fileInput').click();
$('fileInput').onchange = e => importFiles(e.target.files);
window.addEventListener('dragover', e => { e.preventDefault(); $('dropzone').classList.add('dragover'); });
window.addEventListener('dragleave', e => { if (!e.relatedTarget) $('dropzone').classList.remove('dragover'); });
window.addEventListener('drop', e => { e.preventDefault(); $('dropzone').classList.remove('dragover'); if (e.dataTransfer.files.length) importFiles(e.dataTransfer.files); });
function setTrim(which, value, updateInputs = true) {
  if (!active || busy) return;
  if (!Number.isFinite(value)) { sync(); return; }
  const gap = Math.min(0.05, active.duration);
  if (which === 'start') start = Math.max(0, Math.min(value, end - gap));
  else end = Math.min(active.duration, Math.max(value, start + gap));
  if (player.currentTime < start || player.currentTime > end) player.currentTime = start;
  sync(updateInputs);
}
$('start').oninput = e => { if (Number.isFinite(e.target.valueAsNumber)) setTrim('start', e.target.valueAsNumber, false); };
$('end').oninput = e => { if (Number.isFinite(e.target.valueAsNumber)) setTrim('end', e.target.valueAsNumber, false); };
$('start').onchange = e => setTrim('start', e.target.valueAsNumber);
$('end').onchange = e => setTrim('end', e.target.valueAsNumber);
$('markStart').onclick = () => setTrim('start', player.currentTime);
$('markEnd').onclick = () => setTrim('end', player.currentTime);
$('reset').onclick = () => { player.pause(); reset(); };
$('speed').oninput = e => { speed = speeds[Number(e.target.value)]; player.playbackRate = speed; sync(); };
document.querySelectorAll('[data-speed]').forEach(b => b.onclick = () => { $('speed').value = b.dataset.speed; $('speed').dispatchEvent(new Event('input')); });
$('format').onchange = sync;
async function play() {
  if (!active || busy) return;
  if (!player.paused) return player.pause();
  if (player.currentTime < start || player.currentTime >= end - 0.02) player.currentTime = start;
  try { await player.play(); } catch { $('previewWarning').hidden = false; toast('La vista previa no admite este formato. Puedes exportarlo igualmente.'); }
}
$('play').onclick = play;
player.onplay = () => $('play').textContent = 'Ⅱ';
player.onpause = () => $('play').textContent = '▶';
player.onerror = () => { if (active) $('previewWarning').hidden = false; };
player.ontimeupdate = () => {
  if (!active) return;
  $('currentTime').textContent = time(player.currentTime);
  $('playhead').style.left = `${Math.min(100, player.currentTime / active.duration * 100)}%`;
  if (!player.paused && player.currentTime >= end - 0.015) { if (repeat) player.currentTime = start; else { player.pause(); player.currentTime = end; } }
};
player.onended = () => { if (repeat) { player.currentTime = start; player.play().catch(() => {}); } };
$('loop').onclick = () => { repeat = !repeat; $('loop').setAttribute('aria-pressed', String(repeat)); };
$('mute').onclick = () => { player.muted = !player.muted; $('mute').textContent = player.muted ? '⊘' : '♫'; $('mute').title = player.muted ? 'Activar sonido' : 'Silenciar'; };
$('fullscreen').onclick = async () => { try { await player.requestFullscreen(); } catch { toast('La pantalla completa no está disponible en este navegador.'); } };
for (const [id, which] of [['leftHandle', 'start'], ['rightHandle', 'end']]) {
  const handle = $(id);
  const update = e => { const rect = $('timeline').getBoundingClientRect(); setTrim(which, (e.clientX - rect.left) / rect.width * active.duration); };
  handle.onpointerdown = e => { if (!active || busy) return; e.stopPropagation(); handle.setPointerCapture(e.pointerId); update(e); };
  handle.onpointermove = e => { if (handle.hasPointerCapture(e.pointerId)) update(e); };
  handle.onpointerup = e => { if (handle.hasPointerCapture(e.pointerId)) handle.releasePointerCapture(e.pointerId); };
  handle.onkeydown = e => { if (['ArrowLeft', 'ArrowRight'].includes(e.key)) { e.preventDefault(); setTrim(which, (which === 'start' ? start : end) + (e.key === 'ArrowLeft' ? -0.1 : 0.1)); } };
}
$('timeline').onpointerdown = e => { if (!active || busy || e.target.closest('.handle')) return; const rect = $('timeline').getBoundingClientRect(); player.currentTime = Math.min(end, Math.max(start, (e.clientX - rect.left) / rect.width * active.duration)); };
$('export').onclick = async () => {
  if (!active || busy || importing) { if (importing) toast('Espera a que termine la importación.'); return; }
  busy = true; player.pause(); sync(); renderLibrary();
  const status = $('exportStatus'); status.replaceChildren();
  const label = document.createElement('div'); label.textContent = 'Preparando exportación…';
  const progress = document.createElement('progress'); progress.max = 100; progress.value = 0;
  const cancel = document.createElement('button'); cancel.textContent = 'Cancelar exportación';
  status.append(label, progress, cancel);
  try {
    const result = await api('/api/exports', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId: active.id, start, end, speed, format: $('format').value, quality: $('quality').value }) });
    jobId = result.id;
    cancel.onclick = async () => { try { await api(`/api/exports/${jobId}`, { method: 'DELETE' }); cancel.disabled = true; } catch (err) { toast(err.message); } };
    for (;;) {
      const job = await api(`/api/exports/${jobId}`);
      if (job.status === 'done') {
        status.replaceChildren();
        label.textContent = '✓ Tu clip está listo.';
        const download = document.createElement('a'); download.href = `/api/exports/${job.id}/download`; download.textContent = '↓ Descargar clip'; download.download = job.name;
        status.append(label, download); toast('Exportación completada. Tu clip está listo para descargar.'); break;
      }
      if (job.status === 'error') throw new Error(job.error);
      if (job.status === 'cancelled') { status.textContent = 'Exportación cancelada.'; break; }
      label.textContent = `Exportando clip · ${job.progress} %`; progress.value = job.progress;
      await new Promise(resolve => setTimeout(resolve, 500));
    }
  } catch (err) { status.textContent = err.message; toast(err.message); }
  finally { busy = false; jobId = null; sync(); renderLibrary(); }
};
$('help').onclick = () => $('helpDialog').showModal();
$('closeHelp').onclick = () => $('helpDialog').close();
document.addEventListener('keydown', e => {
  if (e.target.closest('input,select,textarea,button,a') || $('helpDialog').open || !active || busy) return;
  if (e.code === 'Space') { e.preventDefault(); play(); }
  if (e.key.toLowerCase() === 'i') setTrim('start', player.currentTime);
  if (e.key.toLowerCase() === 'o') setTrim('end', player.currentTime);
});
try { files = await api('/api/files'); if (files.length) selectFile(files[0]); } catch (err) { toast(err.message); }
sync(); renderLibrary();

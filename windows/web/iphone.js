'use strict';

// Página que sirve DropToWin para los iPhone (iOS no permite enviar archivos por Bluetooth a un PC).
// Empareja con el código QR de un solo uso y sube por trozos reanudables (PUT con offset).

const CHUNK = 4 * 1024 * 1024;
const $ = (id) => document.getElementById(id);

const storage = {
  get(key) { try { return localStorage.getItem(key); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, value); } catch { /* modo privado */ } },
  remove(key) { try { localStorage.removeItem(key); } catch { /* modo privado */ } },
};

let token = null;
let deviceName = null;
let pcName = 'el PC';

// ───────────────────────── Utilidades ─────────────────────────

function show(viewId) {
  for (const id of ['view-nocode', 'view-pair', 'view-main']) $(id).hidden = id !== viewId;
}

function setStatus(text, isError) {
  const el = $('status');
  el.textContent = text;
  el.className = isError ? 'error' : 'muted';
}

function setPcName(name) {
  pcName = name || 'el PC';
  document.querySelectorAll('.pc-name').forEach((el) => { el.textContent = pcName; });
}

function formatSize(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = -1;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toLocaleString('es', { maximumFractionDigits: value < 10 ? 1 : 0 })} ${units[unit]}`;
}

function formatDuration(seconds) {
  if (!isFinite(seconds) || seconds < 0) return '';
  if (seconds < 60) return `${Math.ceil(seconds)} s`;
  if (seconds < 3600) return `${Math.ceil(seconds / 60)} min`;
  return `${Math.floor(seconds / 3600)} h ${Math.ceil((seconds % 3600) / 60)} min`;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function api(method, path, body) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  let payload;
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    payload = JSON.stringify(body);
  }
  let response;
  try {
    response = await fetch(path, { method, headers, body: payload, cache: 'no-store' });
  } catch {
    const err = new Error('No se puede conectar con el PC. ¿Está DropToWin abierto y en la misma Wi-Fi?');
    err.status = 0;
    throw err;
  }
  let data = null;
  try { data = await response.json(); } catch { /* sin cuerpo */ }
  if (!response.ok) {
    const err = new Error((data && data.message) || `Error ${response.status}`);
    err.status = response.status;
    err.data = data;
    throw err;
  }
  return data;
}

// ───────────────────────── Inicio y emparejamiento ─────────────────────────

function readHash() {
  const params = new URLSearchParams(location.hash.slice(1));
  return { code: params.get('p'), token: params.get('t'), name: params.get('n') };
}

/** Deja la llave en la URL: "Añadir a pantalla de inicio" la conserva (allí localStorage es otro). */
function rememberInUrl() {
  history.replaceState(null, '', `/iphone#t=${encodeURIComponent(token)}&n=${encodeURIComponent(deviceName || 'iPhone')}`);
}

async function init() {
  try {
    const info = await api('GET', '/api/info');
    setPcName(info.pcName);
  } catch (e) {
    setStatus(e.message, true);
  }

  const hash = readHash();
  token = storage.get('dtw.token') || hash.token;
  deviceName = storage.get('dtw.name') || hash.name;
  if (hash.token && !storage.get('dtw.token')) {
    storage.set('dtw.token', hash.token);
    storage.set('dtw.name', hash.name || 'iPhone');
  }

  if (hash.code && !token) {
    showPair(hash.code);
    return;
  }
  if (!token) {
    setStatus('Este iPhone aún no está conectado.', false);
    show('view-nocode');
    return;
  }
  try {
    const ping = await api('GET', '/api/ping');
    setPcName(ping.pcName);
    deviceName = ping.deviceName;
    showMain();
  } catch (e) {
    if (e.status === 401) {
      forget();
      setStatus('El PC quitó el permiso a este iPhone. Vuelve a conectarlo con el código QR.', true);
      show('view-nocode');
    } else {
      showMain();
      setStatus(e.message, true);
    }
  }
}

function showPair(code) {
  setStatus(`Listo para conectar con ${pcName}.`, false);
  show('view-pair');
  $('pair').onclick = async () => {
    const name = $('device-name').value.trim() || 'iPhone';
    $('pair').disabled = true;
    $('pair-error').hidden = true;
    try {
      const result = await api('POST', '/api/pair', { code, name });
      token = result.token;
      deviceName = result.deviceName;
      storage.set('dtw.token', token);
      storage.set('dtw.name', deviceName);
      setPcName(result.pcName);
      rememberInUrl();
      showMain();
      $('guide').open = true;
    } catch (e) {
      $('pair-error').textContent = e.message;
      $('pair-error').hidden = false;
      $('pair').disabled = false;
    }
  };
}

function showMain() {
  setStatus(`Conectado a ${pcName} como «${deviceName || 'iPhone'}».`, false);
  $('v-url').textContent = `${location.origin}/api/upload`;
  $('v-key').textContent = `Bearer ${token}`;
  if (!location.hash.includes('t=')) rememberInUrl();
  show('view-main');
}

function forget() {
  storage.remove('dtw.token');
  storage.remove('dtw.name');
  token = null;
  history.replaceState(null, '', '/iphone');
}

$('forget').onclick = () => {
  if (!confirm('¿Desconectar este iPhone? Para volver a enviar tendrás que escanear otra vez el código del PC.')) return;
  forget();
  setStatus('Este iPhone se desconectó.', false);
  show('view-nocode');
};

// ───────────────────────── Copiar valores del atajo ─────────────────────────

document.querySelectorAll('.copy').forEach((button) => {
  button.onclick = () => {
    const text = $(button.dataset.copy).textContent;
    // navigator.clipboard exige HTTPS; en la red local se usa el método clásico.
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    area.setSelectionRange(0, text.length);
    let ok = false;
    try { ok = document.execCommand('copy'); } catch { ok = false; }
    area.remove();
    button.textContent = ok ? 'Copiado ✓' : 'Mantén pulsado el texto';
    setTimeout(() => { button.textContent = 'Copiar'; }, 2500);
  };
});

// ───────────────────────── Texto ─────────────────────────

$('send-text').onclick = async () => {
  const text = $('text').value;
  if (!text.trim()) return;
  const status = $('text-status');
  $('send-text').disabled = true;
  status.hidden = false;
  status.className = 'muted small';
  status.textContent = 'Enviando…';
  try {
    await api('POST', '/api/text', { text });
    status.className = 'ok small';
    status.textContent = `Enviado ✓ (en ${pcName} ya está en el portapapeles)`;
    $('text').value = '';
  } catch (e) {
    status.className = 'error small';
    status.textContent = e.message;
  } finally {
    $('send-text').disabled = false;
  }
};

// ───────────────────────── Archivos ─────────────────────────

const groups = [];
let running = false;
const speed = { time: 0, bytes: 0, value: 0, sent: 0 };

$('files').addEventListener('change', (event) => {
  const files = Array.from(event.target.files || []);
  event.target.value = '';
  if (files.length) addFiles(files);
});

function addFiles(files) {
  const items = files.map((file) => ({ file, id: null, received: 0, inFlight: 0, state: 'waiting', savedAs: null, el: null }));
  items.forEach(renderItem);
  groups.push({ batchId: null, items });
  run();
}

function renderItem(item) {
  const li = document.createElement('li');
  li.innerHTML = '<div class="row"><span class="name"></span><span class="state"></span></div><div class="bar"><i></i></div>';
  li.querySelector('.name').textContent = item.file.name || 'archivo';
  $('queue').appendChild(li);
  item.el = li;
  updateItem(item);
}

function updateItem(item) {
  const state = item.el.querySelector('.state');
  const size = item.file.size;
  const done = Math.min(size, item.received + item.inFlight);
  const percent = size > 0 ? Math.floor((done * 100) / size) : 100;
  item.el.querySelector('.bar > i').style.width = `${item.state === 'done' ? 100 : percent}%`;
  state.className = 'state';
  switch (item.state) {
    case 'waiting': state.textContent = `En cola · ${formatSize(size)}`; break;
    case 'sending': state.textContent = `${percent} % de ${formatSize(size)}`; break;
    case 'retrying': state.textContent = 'Reintentando…'; break;
    case 'done': state.textContent = 'Enviado ✓'; state.classList.add('ok'); break;
    case 'error': state.textContent = 'Error'; state.classList.add('bad'); break;
    default: break;
  }
}

function updateSummary() {
  const all = groups.flatMap((g) => g.items);
  const total = all.reduce((sum, i) => sum + i.file.size, 0);
  const sent = all.reduce((sum, i) => sum + (i.state === 'done' ? i.file.size : Math.min(i.file.size, i.received + i.inFlight)), 0);
  const doneCount = all.filter((i) => i.state === 'done').length;
  const now = performance.now();
  if (speed.time === 0) { speed.time = now; speed.bytes = sent; }
  const elapsed = (now - speed.time) / 1000;
  if (elapsed >= 1) {
    const instant = Math.max(0, sent - speed.bytes) / elapsed;
    speed.value = speed.value ? speed.value * 0.6 + instant * 0.4 : instant;
    speed.time = now;
    speed.bytes = sent;
  }
  const summary = $('summary');
  summary.hidden = all.length === 0;
  if (doneCount === all.length) {
    summary.textContent = `Listo: ${doneCount} ${doneCount === 1 ? 'archivo enviado' : 'archivos enviados'} a ${pcName} (${formatSize(total)}).`;
    summary.className = 'ok';
    return;
  }
  const eta = speed.value > 1 ? formatDuration((total - sent) / speed.value) : '';
  summary.className = 'muted';
  summary.textContent = `${doneCount} de ${all.length} · ${formatSize(sent)} de ${formatSize(total)}` +
    (speed.value > 1 ? ` · ${formatSize(speed.value)}/s` : '') + (eta ? ` · quedan ${eta}` : '');
}

async function run() {
  if (running) return;
  running = true;
  try {
    for (let g = 0; g < groups.length; g++) {
      const group = groups[g];
      if (group.items.every((i) => i.state === 'done' || i.state === 'error')) continue;
      await sendGroup(group);
    }
  } finally {
    running = false;
    updateSummary();
  }
}

async function sendGroup(group) {
  if (!group.batchId) {
    const pending = group.items.filter((i) => i.state !== 'done');
    try {
      const result = await withRetries(() => api('POST', '/api/batch', {
        files: pending.map((i) => ({
          name: i.file.name || 'archivo',
          size: i.file.size,
          mime: i.file.type || null,
          modified: i.file.lastModified || null,
        })),
      }));
      group.batchId = result.batchId;
      result.files.forEach((f, index) => {
        pending[index].id = f.id;
        pending[index].received = 0;
        if (f.done) { pending[index].state = 'done'; updateItem(pending[index]); }
      });
    } catch (e) {
      pending.forEach((i) => { i.state = 'error'; updateItem(i); });
      setStatus(e.message, true);
      return;
    }
  }
  for (const item of group.items) {
    if (item.state === 'done' || item.state === 'error') continue;
    const outcome = await sendItem(group, item);
    if (outcome === 'lost-batch') {
      // El PC se reinició: se crea un lote nuevo con lo que falta.
      group.batchId = null;
      group.items.filter((i) => i.state !== 'done').forEach((i) => { i.state = 'waiting'; i.received = 0; updateItem(i); });
      await sendGroup(group);
      return;
    }
  }
}

async function sendItem(group, item) {
  item.state = 'sending';
  updateItem(item);
  let failures = 0;
  while (item.state !== 'done') {
    const start = item.received;
    const end = Math.min(start + CHUNK, item.file.size);
    try {
      const result = await putChunk(group.batchId, item.id, start, item.file.slice(start, end), (loaded) => {
        item.inFlight = loaded;
        updateItem(item);
        updateSummary();
      });
      item.inFlight = 0;
      item.received = result.received;
      failures = 0;
      if (result.done) {
        item.state = 'done';
        item.savedAs = result.savedAs;
      } else {
        item.state = 'sending';
      }
      updateItem(item);
      updateSummary();
    } catch (e) {
      item.inFlight = 0;
      if (e.status === 409 && e.data) { item.received = e.data.received; continue; }
      if (e.status === 404) return 'lost-batch';
      if (e.status === 401) { item.state = 'error'; updateItem(item); setStatus(e.message, true); return 'error'; }
      failures++;
      if (failures > 8) {
        item.state = 'error';
        updateItem(item);
        setStatus('No se pudo terminar el envío. Comprueba la Wi-Fi y vuelve a intentarlo.', true);
        return 'error';
      }
      item.state = 'retrying';
      updateItem(item);
      await sleep(Math.min(15000, 1000 * 2 ** failures));
      try {
        const state = await api('GET', `/api/batch/${encodeURIComponent(group.batchId)}/${encodeURIComponent(item.id)}`);
        item.received = state.received;
        if (state.done) { item.state = 'done'; item.savedAs = state.savedAs; updateItem(item); }
      } catch (inner) {
        if (inner.status === 404) return 'lost-batch';
      }
      if (item.state !== 'done') item.state = 'sending';
    }
  }
  return 'done';
}

async function withRetries(action) {
  let lastError;
  for (let attempt = 0; attempt < 4; attempt++) {
    try { return await action(); } catch (e) {
      lastError = e;
      if (e.status && e.status !== 503 && e.status < 500) throw e;
      await sleep(1000 * 2 ** attempt);
    }
  }
  throw lastError;
}

function putChunk(batchId, fileId, offset, blob, onProgress) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', `/api/batch/${encodeURIComponent(batchId)}/${encodeURIComponent(fileId)}?offset=${offset}`);
    xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.setRequestHeader('Content-Type', 'application/octet-stream');
    xhr.timeout = 120000;
    xhr.upload.onprogress = (event) => onProgress(event.loaded);
    xhr.onload = () => {
      let data = null;
      try { data = JSON.parse(xhr.responseText); } catch { /* sin cuerpo */ }
      if (xhr.status >= 200 && xhr.status < 300) { resolve(data); return; }
      const err = new Error((data && data.message) || `Error ${xhr.status}`);
      err.status = xhr.status;
      err.data = data;
      reject(err);
    };
    xhr.onerror = () => reject(Object.assign(new Error('Se perdió la conexión'), { status: 0 }));
    xhr.ontimeout = () => reject(Object.assign(new Error('Tiempo de espera agotado'), { status: 0 }));
    xhr.send(blob);
  });
}

// Al volver a la página (Safari pausa las subidas en segundo plano), se retoma lo pendiente.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible' && token) run();
});

init();

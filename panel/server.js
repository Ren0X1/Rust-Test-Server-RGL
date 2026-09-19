'use strict';
// ─────────────────────────────────────────────────────────────────────────────
//  RGL Control — panel web local para el servidor de Rust
//
//  Sin dependencias (Node >= 22): http + WebSocket nativo.
//  Solo escucha en 127.0.0.1. Cada peticion a /api lleva un token que se genera
//  al arrancar y se inyecta en la pagina, asi que otra web abierta en el
//  navegador no puede mandar comandos al servidor (CSRF / DNS rebinding).
// ─────────────────────────────────────────────────────────────────────────────
const http = require('http');
const fs = require('fs');
const fsp = fs.promises;
const path = require('path');
const crypto = require('crypto');
const { execFile, spawn } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const SERVER_DIR = path.join(ROOT, 'server');
const START_BAT = path.join(ROOT, 'start.bat');
const STOP_FLAG = path.join(__dirname, 'stop.flag');
const PUBLIC = path.join(__dirname, 'public');
const PLUGINS_DIR = path.join(SERVER_DIR, 'oxide', 'plugins');
const CONFIG_DIR = path.join(SERVER_DIR, 'oxide', 'config');

const HOST = '127.0.0.1';
const PORT = Number(process.env.PANEL_PORT) || 28080;
const TOKEN = crypto.randomBytes(24).toString('hex');
const ALLOWED_HOSTS = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`]);

// ─────────────────────────────────────────────────────────────────────────────
//  Consola: buffer circular + clientes SSE
// ─────────────────────────────────────────────────────────────────────────────
const LOG_MAX = 1500;
const logs = [];
let logSeq = 0;
const sseClients = new Set();

function pushLog(type, text) {
  if (text == null) return;
  text = String(text).replace(/\s+$/, '');
  if (!text) return;
  const entry = { id: ++logSeq, t: Date.now(), type, text };
  logs.push(entry);
  if (logs.length > LOG_MAX) logs.shift();
  broadcast('log', entry);
}

function broadcast(event, data) {
  const payload = `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
  for (const res of sseClients) res.write(payload);
}

// ─────────────────────────────────────────────────────────────────────────────
//  start.bat — argumentos de lanzamiento (+clave valor ^)
// ─────────────────────────────────────────────────────────────────────────────
const ARG_RE = /^\s*\+([\w.]+)\s+(.*?)\s*\^?\s*$/;

async function readLaunch() {
  const text = await fsp.readFile(START_BAT, 'utf8');
  const args = [];
  for (const line of text.split(/\r?\n/)) {
    const m = line.match(ARG_RE);
    if (!m) continue;
    let v = m[2].trim();
    const quoted = /^".*"$/.test(v);
    if (quoted) v = v.slice(1, -1);
    args.push({ key: m[1], value: v, quoted });
  }
  return args;
}

function launchValue(args, key, def) {
  const a = args.find((x) => x.key === key);
  return a ? a.value : def;
}

// Caracteres que un .bat interpretaria (o que romperian las comillas)
const BAT_UNSAFE = /["%^&|<>\r\n]/;

async function writeLaunch(args) {
  if (!Array.isArray(args) || !args.length) throw httpError(400, 'Lista de argumentos vacia');
  for (const a of args) {
    if (!/^[A-Za-z0-9_.]+$/.test(a.key || '')) throw httpError(400, `Clave invalida: ${a.key}`);
    if (BAT_UNSAFE.test(String(a.value ?? ''))) throw httpError(400, `El valor de ${a.key} no puede llevar " % ^ & | < >`);
  }
  // Se respetan las comillas que ya tuviera cada valor (guardar sin cambios = mismo fichero)
  const antes = new Map((await readLaunch()).map((a) => [a.key, a.quoted]));
  const text = await fsp.readFile(START_BAT, 'utf8');
  const lines = text.split(/\r?\n/);
  const idx = lines.map((l, i) => (ARG_RE.test(l) ? i : -1)).filter((i) => i >= 0);
  if (!idx.length) throw httpError(500, 'No se encuentran los argumentos en start.bat');
  for (let i = 1; i < idx.length; i++)
    if (idx[i] !== idx[i - 1] + 1) throw httpError(500, 'Los argumentos de start.bat no son contiguos');

  const nuevas = args.map((a, i) => {
    const v = String(a.value ?? '');
    const val = v === '' || /\s/.test(v) || antes.get(a.key) ? `"${v}"` : v;
    return `  +${a.key} ${val}${i < args.length - 1 ? ' ^' : ''}`;
  });
  lines.splice(idx[0], idx.length, ...nuevas);
  await fsp.writeFile(START_BAT, lines.join('\r\n'), 'utf8');
}

async function identityDir() {
  const args = await readLaunch();
  const id = launchValue(args, 'server.identity', 'skintest');
  if (!/^[\w.-]+$/.test(id)) throw httpError(500, 'server.identity invalido');
  return path.join(SERVER_DIR, 'server', id);
}

// ─────────────────────────────────────────────────────────────────────────────
//  server.cfg — secciones "// ---------- Titulo ----------"
// ─────────────────────────────────────────────────────────────────────────────
const SECTION_RE = /^\/\/\s*-{3,}\s*(.*?)\s*-{3,}\s*$/;

function parseCfg(text) {
  const header = [];
  const sections = [];
  let cur = null;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    const sec = line.match(SECTION_RE);
    if (sec) { cur = { title: sec[1], notes: [], items: [] }; sections.push(cur); continue; }
    if (!cur) { if (line) header.push(raw); continue; }
    if (!line) continue;
    if (line.startsWith('//')) { cur.notes.push(line.replace(/^\/\/\s?/, '')); continue; }
    const m = line.match(/^([\w.]+)\s+(.*?)\s*(?:\/\/\s*(.*))?$/);
    if (!m) continue;
    let v = m[2];
    if (/^".*"$/.test(v)) v = v.slice(1, -1);
    cur.items.push({ key: m[1], value: v, comment: m[3] || '' });
  }
  return { header, sections };
}

function renderCfg({ header, sections }) {
  const out = [...(header || [])];
  for (const s of sections || []) {
    out.push('', `// ---------- ${s.title} ----------`);
    for (const n of s.notes || []) out.push(`// ${n}`);
    for (const it of s.items || []) {
      if (!/^[\w.]+$/.test(it.key || '')) throw httpError(400, `Convar invalida: ${it.key}`);
      const v = String(it.value ?? '');
      if (/["\r\n]/.test(v)) throw httpError(400, `El valor de ${it.key} no puede llevar comillas ni saltos de linea`);
      const val = v === '' || /\s/.test(v) ? `"${v}"` : v;
      out.push(`${it.key} ${val}${it.comment ? '  // ' + it.comment : ''}`);
    }
  }
  return out.join('\r\n') + '\r\n';
}

// ─────────────────────────────────────────────────────────────────────────────
//  users.cfg — ownerid / moderatorid
// ─────────────────────────────────────────────────────────────────────────────
function parseUsers(text) {
  const users = [];
  for (const line of text.split(/\r?\n/)) {
    const m = line.match(/^\s*(ownerid|moderatorid)\s+(\d+)\s*(?:"([^"]*)")?\s*(?:"([^"]*)")?/);
    if (m) users.push({ role: m[1], steamid: m[2], name: m[3] || '', reason: m[4] || '' });
  }
  return users;
}

function renderUsers(users) {
  return users.map((u) => {
    if (!['ownerid', 'moderatorid'].includes(u.role)) throw httpError(400, `Rol invalido: ${u.role}`);
    if (!/^\d{17}$/.test(u.steamid || '')) throw httpError(400, `SteamID64 invalido: ${u.steamid}`);
    const limpio = (s) => String(s || '').replace(/["\r\n]/g, '');
    return `${u.role} ${u.steamid} "${limpio(u.name)}" "${limpio(u.reason)}"`;
  }).join('\r\n') + '\r\n';
}

// ─────────────────────────────────────────────────────────────────────────────
//  RCON (WebRCON de Rust)
//
//  Los comandos van en cola, de uno en uno: asi se sabe a quien pertenece cada
//  respuesta. Algunos comandos de Oxide contestan por el canal general (sin
//  Identifier), por eso existe captureBroadcast.
// ─────────────────────────────────────────────────────────────────────────────
class Rcon {
  constructor() {
    this.ws = null;
    this.connected = false;
    this.queue = [];
    this.current = null;
    this.nextId = 1000;
    this.timer = null;
  }

  async connect() {
    clearTimeout(this.timer);
    if (this.ws) return;
    let port = '28016', pass = '';
    try {
      const args = await readLaunch();
      port = launchValue(args, 'rcon.port', port);
      pass = launchValue(args, 'rcon.password', pass);
    } catch { /* se reintenta luego */ }

    let ws;
    try { ws = new WebSocket(`ws://127.0.0.1:${port}/${encodeURIComponent(pass)}`); }
    catch { return this.retry(); }
    this.ws = ws;

    ws.onopen = () => {
      this.connected = true;
      pushLog('system', `RCON conectado (puerto ${port})`);
      emitStatus();
    };
    ws.onmessage = (ev) => this.onMessage(ev.data);
    ws.onerror = () => {};
    ws.onclose = () => {
      const estaba = this.connected;
      this.ws = null;
      this.connected = false;
      if (this.current) this.finish(this.current);
      for (const j of this.queue.splice(0)) j.reject(httpError(503, 'RCON desconectado'));
      if (estaba) { pushLog('system', 'RCON desconectado'); emitStatus(); }
      this.retry();
    };
  }

  retry() { clearTimeout(this.timer); this.timer = setTimeout(() => this.connect(), 3000); }

  reconnect() { if (this.ws) this.ws.close(); else this.connect(); }

  onMessage(data) {
    let msg;
    try { msg = JSON.parse(data); } catch { return; }
    const id = Number(msg.Identifier);
    let text = msg.Message ?? '';
    let type = { Generic: 'info', Log: 'info', Warning: 'warn', Error: 'error', Chat: 'chat' }[msg.Type] || 'info';

    if (msg.Type === 'Chat') {
      try {
        const c = JSON.parse(text);
        text = `${c.Username || c.UserId || '?'}: ${c.Message}`;
      } catch { /* texto tal cual */ }
    }

    const cur = this.current;
    const esSuya = cur && id === cur.id;
    if (cur && (esSuya || (id <= 0 && cur.captureBroadcast))) { cur.parts.push(text); cur.bump(); }
    if (esSuya && cur.silent) return;
    pushLog(type, text);
  }

  send(command, opts = {}) {
    return new Promise((resolve, reject) => {
      if (!this.connected) return reject(httpError(503, 'RCON no conectado: el servidor esta apagado o arrancando'));
      this.queue.push({ command, silent: !!opts.silent, captureBroadcast: !!opts.captureBroadcast, resolve, reject });
      this.pump();
    });
  }

  pump() {
    if (this.current || !this.queue.length || !this.connected) return;
    const job = this.queue.shift();
    job.id = this.nextId++;
    job.parts = [];
    this.current = job;
    // Sin respuesta en 1,2 s se da por terminado; con respuesta, 300 ms tras la ultima linea
    job.idle = setTimeout(() => this.finish(job), 1200);
    job.hard = setTimeout(() => this.finish(job), 10000);
    job.bump = () => { clearTimeout(job.idle); job.idle = setTimeout(() => this.finish(job), 300); };
    try {
      this.ws.send(JSON.stringify({ Identifier: job.id, Message: job.command, Name: 'RGL Control' }));
    } catch (e) { this.finish(job); }
  }

  finish(job) {
    if (this.current !== job) return;
    clearTimeout(job.idle);
    clearTimeout(job.hard);
    this.current = null;
    job.resolve(job.parts.join('\n'));
    this.pump();
  }
}

const rcon = new Rcon();

// ─────────────────────────────────────────────────────────────────────────────
//  Estado del servidor
// ─────────────────────────────────────────────────────────────────────────────
let status = { process: false, rcon: false, info: null, launch: {}, updated: 0 };

// Historial para las graficas en directo: una muestra cada 3 s, 1 hora.
// Se guarda en disco para que las graficas no empiecen vacias si se reinicia el panel.
const HISTORY_MAX = 1200;
const HISTORY_FILE = path.join(__dirname, 'history.json');
const history = [];
try {
  const hora = Date.now() - HISTORY_MAX * 3000;
  for (const s of JSON.parse(fs.readFileSync(HISTORY_FILE, 'utf8'))) if (s && s.t > hora) history.push(s);
} catch { /* primera vez o fichero roto: se empieza de cero */ }

function saveHistory() {
  try { fs.writeFileSync(HISTORY_FILE, JSON.stringify(history)); } catch { /* no es critico */ }
}
setInterval(saveHistory, 60000);
for (const sig of ['SIGINT', 'SIGTERM', 'SIGBREAK']) process.on(sig, () => { saveHistory(); process.exit(0); });

function pushSample(info) {
  const s = {
    t: Date.now(),
    fps: Number(info.Framerate) || 0,
    players: Number(info.Players) || 0,
    entities: Number(info.EntityCount) || 0,
    memory: Number(info.Memory) || 0,
    netIn: Number(info.NetworkIn) || 0,
    netOut: Number(info.NetworkOut) || 0,
  };
  history.push(s);
  if (history.length > HISTORY_MAX) history.shift();
  broadcast('sample', s);
}

function processRunning() {
  return new Promise((resolve) => {
    execFile('tasklist', ['/FI', 'IMAGENAME eq RustDedicated.exe', '/FO', 'CSV', '/NH'], { windowsHide: true },
      (err, out) => resolve(!err && /RustDedicated\.exe/i.test(out)));
  });
}

async function refreshStatus() {
  const proc = await processRunning();
  let info = null;
  if (rcon.connected) {
    try { info = JSON.parse(await rcon.send('serverinfo', { silent: true })); } catch { info = null; }
    if (info) pushSample(info);
  }
  let launch = {};
  try {
    const args = await readLaunch();
    launch = Object.fromEntries(args.map((a) => [a.key, a.value]));
  } catch { /* nada */ }
  status = { process: proc, rcon: rcon.connected, info, launch, updated: Date.now() };
  emitStatus();
}

function emitStatus() { broadcast('status', { ...status, rcon: rcon.connected }); }

setInterval(() => refreshStatus().catch(() => {}), 3000);

// ─────────────────────────────────────────────────────────────────────────────
//  Parsers de salida de comandos
// ─────────────────────────────────────────────────────────────────────────────
function parsePluginList(text) {
  const out = [];
  for (const line of text.split(/\r?\n/)) {
    const m = line.match(/^\s*\d+\s+"(.+?)"\s+\(([^)]*)\)\s+by\s+(.+?)\s+\(([^)]*)\)\s+-\s+(\S+)/);
    if (m) out.push({ title: m[1], version: m[2], author: m[3], stats: m[4], file: m[5] });
  }
  return out;
}

// Los nombres de grupos y permisos nunca llevan espacios; asi se descartan
// mensajes como "No permissions currently granted"
const NOMBRE_VALIDO = /^[\w.*-]+$/;

// "Titulo:\na, b, c" -> [a, b, c]
function listaTrasDosPuntos(text) {
  const i = text.indexOf(':');
  if (i < 0) return [];
  return text.slice(i + 1).split(/[,\n]/).map((s) => s.trim()).filter((s) => NOMBRE_VALIDO.test(s));
}

function permisosDeGrupo(text) {
  const m = text.match(/permissions:\s*([\s\S]*)$/i);
  if (!m) return [];
  return m[1].split(/[,\n]/).map((s) => s.trim()).filter((s) => NOMBRE_VALIDO.test(s));
}

// ─────────────────────────────────────────────────────────────────────────────
//  RustMaps: ver un seed antes de usarlo
// ─────────────────────────────────────────────────────────────────────────────
const rustmapsCache = new Map();

async function rustmaps(size, seed) {
  const key = `${size}_${seed}`;
  if (rustmapsCache.has(key)) return rustmapsCache.get(key);
  const url = `https://rustmaps.com/map/${key}`;
  const res = await fetch(url, { headers: { 'user-agent': 'Mozilla/5.0 RGL-Control' } });
  const html = await res.text();
  const num = (k) => { const m = html.match(new RegExp(`"${k}":(\\d+(?:\\.\\d+)?)`)); return m ? Number(m[1]) : null; };
  const img = html.match(/https:\/\/content\.rustmaps\.com\/maps\/\d+\/[a-z0-9]+\/map_icons\.png/);
  const found = !!img;
  const data = {
    url, found,
    image: img ? img[0] : null,
    saveVersion: num('saveVersion'),
    land: num('landPercentageOfMap'),
    islands: num('islands'),
    rivers: num('rivers'),
    monuments: num('totalMonuments'),
    custom: /"isCustomMap":true/.test(html),
  };
  rustmapsCache.set(key, data);
  return data;
}

// ─────────────────────────────────────────────────────────────────────────────
//  HTTP
// ─────────────────────────────────────────────────────────────────────────────
function httpError(code, message) { const e = new Error(message); e.code = code; return e; }

function send(res, code, body, type = 'application/json; charset=utf-8') {
  res.writeHead(code, {
    'content-type': type,
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
    'referrer-policy': 'no-referrer',
  });
  res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (c) => {
      size += c.length;
      if (size > 2 * 1024 * 1024) { reject(httpError(413, 'Cuerpo demasiado grande')); req.destroy(); return; }
      chunks.push(c);
    });
    req.on('end', () => {
      const raw = Buffer.concat(chunks).toString('utf8');
      if (!raw) return resolve({});
      try { resolve(JSON.parse(raw)); } catch { reject(httpError(400, 'JSON invalido')); }
    });
    req.on('error', reject);
  });
}

function safeJoin(dir, name, re) {
  if (!re.test(name)) throw httpError(400, 'Nombre de fichero invalido');
  const p = path.join(dir, name);
  if (path.dirname(p) !== dir) throw httpError(400, 'Ruta invalida');
  return p;
}

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon' };

async function serveStatic(req, res, pathname) {
  const rel = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
  const file = path.normalize(path.join(PUBLIC, rel));
  if (!file.startsWith(PUBLIC + path.sep)) return send(res, 403, { error: 'Prohibido' });
  let body;
  try { body = await fsp.readFile(file); } catch { return send(res, 404, { error: 'No encontrado' }); }
  const ext = path.extname(file);
  if (ext === '.html') body = body.toString('utf8').replace('__PANEL_TOKEN__', TOKEN);
  send(res, 200, body, MIME[ext] || 'application/octet-stream');
}

// ─────────────────────────────────────────────────────────────────────────────
//  API
// ─────────────────────────────────────────────────────────────────────────────
const routes = [];
const route = (method, pattern, fn) => routes.push({ method, pattern, fn });

route('GET', /^\/api\/status$/, async () => ({ ...status, rcon: rcon.connected }));

route('GET', /^\/api\/logs$/, async () => ({ logs }));

route('POST', /^\/api\/command$/, async (req, body) => {
  const cmd = String(body.command || '').trim();
  if (!cmd) throw httpError(400, 'Comando vacio');
  if (cmd.length > 500) throw httpError(400, 'Comando demasiado largo');
  pushLog('command', cmd);
  const output = await rcon.send(cmd);
  return { output };
});

// ── Energia
route('POST', /^\/api\/power$/, async (req, body) => {
  const action = body.action;
  const running = await processRunning();

  if (action === 'start') {
    if (running) throw httpError(409, 'El servidor ya esta encendido');
    await fsp.rm(STOP_FLAG, { force: true });
    // RGL_FROM_PANEL: start.bat no vuelve a lanzar el panel ni a abrir el navegador
    const child = spawn('cmd.exe', ['/c', 'start', '"Rust Server"', START_BAT], {
      cwd: ROOT, detached: true, stdio: 'ignore', windowsVerbatimArguments: true,
      env: { ...process.env, RGL_FROM_PANEL: '1' },
    });
    child.unref();
    pushLog('system', 'Arrancando el servidor (start.bat en una ventana nueva)...');
    setTimeout(() => rcon.connect(), 2000);
    return { ok: true };
  }

  if (action === 'stop' || action === 'restart') {
    if (!running) throw httpError(409, 'El servidor ya esta apagado');
    if (!rcon.connected) throw httpError(503, 'RCON no conectado: espera a que termine de arrancar');
    if (action === 'stop') await fsp.writeFile(STOP_FLAG, 'panel');
    else await fsp.rm(STOP_FLAG, { force: true });
    pushLog('system', action === 'stop' ? 'Guardando y apagando el servidor...' : 'Guardando y reiniciando el servidor...');
    await rcon.send('server.save', { silent: true });
    rcon.send('quit').catch(() => {});
    return { ok: true };
  }

  if (action === 'save') {
    await rcon.send('server.save');
    return { ok: true };
  }

  throw httpError(400, 'Accion desconocida');
});

// ── Lanzamiento (start.bat)
route('GET', /^\/api\/launch$/, async () => ({ args: await readLaunch() }));
route('PUT', /^\/api\/launch$/, async (req, body) => {
  await writeLaunch(body.args);
  pushLog('system', 'start.bat actualizado (se aplica al reiniciar)');
  rcon.reconnect();   // por si ha cambiado el puerto o la contrasena de RCON
  return { ok: true, args: await readLaunch() };
});

// ── Mapa
route('GET', /^\/api\/map\/saves$/, async () => {
  const dir = await identityDir();
  let files = [];
  try { files = await fsp.readdir(dir); } catch { /* sin saves aun */ }
  const maps = new Map();
  for (const f of files) {
    const m = f.match(/^proceduralmap\.(\d+)\.(\d+)\.(\d+)/);
    if (!m) continue;
    const st = await fsp.stat(path.join(dir, f));
    const k = `${m[1]}_${m[2]}`;
    const e = maps.get(k) || { size: Number(m[1]), seed: m[2], protocol: m[3], bytes: 0, files: 0, modified: 0 };
    e.bytes += st.size;
    e.files++;
    e.modified = Math.max(e.modified, st.mtimeMs);
    maps.set(k, e);
  }
  return { saves: [...maps.values()].sort((a, b) => b.modified - a.modified) };
});

// Render del mapa actual con world.rendermap (lo guarda en server\map_<tamano>_<seed>.png)
route('POST', /^\/api\/map\/render$/, async () => {
  const out = await rcon.send('world.rendermap 0.35');
  const m = out.match(/map_\d+_\d+\.png/);
  return { ok: !!m, file: m ? m[0] : null, output: out };
});

route('GET', /^\/api\/map\/image$/, async (req, body, url) => {
  const size = String(url.searchParams.get('size') || '');
  const seed = String(url.searchParams.get('seed') || '');
  if (!/^\d{3,5}$/.test(size) || !/^\d{1,10}$/.test(seed)) throw httpError(400, 'Tamano o seed invalidos');
  const file = path.join(SERVER_DIR, `map_${size}_${seed}.png`);
  try { await fsp.access(file); } catch { throw httpError(404, 'Aun no hay render de este mapa'); }
  return { url: `/render/${size}_${seed}.png`, modified: (await fsp.stat(file)).mtimeMs };
});

route('GET', /^\/api\/history$/, async () => ({ history }));

route('GET', /^\/api\/rustmaps$/, async (req, body, url) => {
  const size = Number(url.searchParams.get('size'));
  const seed = String(url.searchParams.get('seed') || '');
  if (!(size >= 1000 && size <= 6000) || !/^\d{1,10}$/.test(seed)) throw httpError(400, 'Tamano o seed invalidos');
  try { return await rustmaps(size, seed); }
  catch { throw httpError(502, 'No se pudo consultar RustMaps'); }
});

// ── server.cfg
route('GET', /^\/api\/servercfg$/, async () => {
  const file = path.join(await identityDir(), 'cfg', 'server.cfg');
  const raw = await fsp.readFile(file, 'utf8');
  return { raw, ...parseCfg(raw) };
});
route('PUT', /^\/api\/servercfg$/, async (req, body) => {
  const file = path.join(await identityDir(), 'cfg', 'server.cfg');
  const text = typeof body.raw === 'string' ? body.raw : renderCfg(body);
  await fsp.writeFile(file, text, 'utf8');

  // Aplicar en vivo las convars que se han pedido
  const aplicadas = [];
  if (Array.isArray(body.apply) && rcon.connected) {
    for (const it of body.apply) {
      if (!/^[\w.]+$/.test(it.key || '') || /["\r\n]/.test(String(it.value))) continue;
      const v = String(it.value);
      await rcon.send(`${it.key} ${/\s/.test(v) || v === '' ? `"${v}"` : v}`);
      aplicadas.push(it.key);
    }
  }
  pushLog('system', `server.cfg guardado${aplicadas.length ? ` · ${aplicadas.length} convars aplicadas en vivo` : ''}`);
  return { ok: true, applied: aplicadas };
});

// ── users.cfg
route('GET', /^\/api\/users$/, async () => {
  const file = path.join(await identityDir(), 'cfg', 'users.cfg');
  let raw = '';
  try { raw = await fsp.readFile(file, 'utf8'); } catch { /* vacio */ }
  return { users: parseUsers(raw) };
});
route('PUT', /^\/api\/users$/, async (req, body) => {
  const users = Array.isArray(body.users) ? body.users : [];
  const file = path.join(await identityDir(), 'cfg', 'users.cfg');
  await fsp.writeFile(file, renderUsers(users), 'utf8');
  if (body.applyLive && rcon.connected) {
    for (const u of users) await rcon.send(`${u.role} ${u.steamid} "${u.name}" "${u.reason}"`);
  }
  pushLog('system', 'users.cfg guardado');
  return { ok: true };
});

// ── Plugins
route('GET', /^\/api\/plugins$/, async () => {
  let loaded = [];
  if (rcon.connected) loaded = parsePluginList(await rcon.send('oxide.plugins', { silent: true }));
  const files = (await fsp.readdir(PLUGINS_DIR)).filter((f) => f.endsWith('.cs')).sort();
  const configs = new Set((await fsp.readdir(CONFIG_DIR).catch(() => [])).filter((f) => f.endsWith('.json')));
  const plugins = files.map((file) => {
    const base = file.slice(0, -3);
    const l = loaded.find((p) => p.file === file);
    return { file, name: base, loaded: !!l, title: l ? l.title : base, version: l ? l.version : '', author: l ? l.author : '', stats: l ? l.stats : '', config: configs.has(base + '.json') ? base + '.json' : null };
  });
  return { plugins, rcon: rcon.connected };
});

route('POST', /^\/api\/plugins\/(load|unload|reload)$/, async (req, body, url, m) => {
  const name = String(body.name || '');
  if (!/^[\w.-]+$/.test(name)) throw httpError(400, 'Nombre de plugin invalido');
  const output = await rcon.send(`oxide.${m[1]} ${name}`);
  return { output };
});

route('GET', /^\/api\/configs$/, async () => {
  const files = (await fsp.readdir(CONFIG_DIR).catch(() => [])).filter((f) => f.endsWith('.json')).sort();
  return { configs: files };
});
route('GET', /^\/api\/configs\/([\w.-]+\.json)$/, async (req, body, url, m) => {
  const file = safeJoin(CONFIG_DIR, m[1], /^[\w.-]+\.json$/);
  return { name: m[1], raw: await fsp.readFile(file, 'utf8') };
});
route('PUT', /^\/api\/configs\/([\w.-]+\.json)$/, async (req, body, url, m) => {
  const file = safeJoin(CONFIG_DIR, m[1], /^[\w.-]+\.json$/);
  let data;
  try { data = typeof body.raw === 'string' ? JSON.parse(body.raw) : body.data; }
  catch (e) { throw httpError(400, 'JSON invalido: ' + e.message); }
  if (data === undefined) throw httpError(400, 'Falta el contenido');
  // Mismos saltos de linea que el fichero original (Oxide los escribe en CRLF)
  const crlf = /\r\n/.test(await fsp.readFile(file, 'utf8').catch(() => '\r\n'));
  const json = JSON.stringify(data, null, 2);
  await fsp.writeFile(file, crlf ? json.replace(/\n/g, '\r\n') : json, 'utf8');
  let output = '';
  if (body.reload && rcon.connected) output = await rcon.send(`oxide.reload ${m[1].slice(0, -5)}`);
  pushLog('system', `Config ${m[1]} guardada${body.reload ? ' y plugin recargado' : ''}`);
  return { ok: true, output };
});

// ── Permisos (Oxide, via RCON)
route('GET', /^\/api\/perms$/, async () => {
  const groups = listaTrasDosPuntos(await rcon.send('oxide.show groups', { silent: true, captureBroadcast: true }));
  const all = listaTrasDosPuntos(await rcon.send('oxide.show perms', { silent: true, captureBroadcast: true })).sort();
  const detail = {};
  for (const g of groups) {
    if (!/^[\w.-]+$/.test(g)) continue;
    detail[g] = permisosDeGrupo(await rcon.send(`oxide.show group ${g}`, { silent: true, captureBroadcast: true }));
  }
  return { groups, perms: all, detail };
});

route('POST', /^\/api\/perms$/, async (req, body) => {
  const { action, kind, target, perm } = body;
  if (!['grant', 'revoke'].includes(action)) throw httpError(400, 'Accion invalida');
  if (!['group', 'user'].includes(kind)) throw httpError(400, 'Tipo invalido');
  if (!/^[\w.-]+$/.test(target || '')) throw httpError(400, 'Destino invalido');
  if (!/^[\w.*-]+$/.test(perm || '')) throw httpError(400, 'Permiso invalido');
  const output = await rcon.send(`oxide.${action} ${kind} ${target} ${perm}`, { captureBroadcast: true });
  return { output };
});

route('POST', /^\/api\/groups$/, async (req, body) => {
  const { action, name } = body;
  if (!['add', 'remove'].includes(action)) throw httpError(400, 'Accion invalida');
  if (!/^[\w.-]+$/.test(name || '')) throw httpError(400, 'Nombre de grupo invalido');
  const output = await rcon.send(`oxide.group ${action} ${name}`, { captureBroadcast: true });
  return { output };
});

// ── Jugadores
route('GET', /^\/api\/players$/, async () => {
  const raw = await rcon.send('playerlist', { silent: true });
  let players = [];
  try { players = JSON.parse(raw); } catch { /* sin jugadores o formato raro */ }
  return { players };
});

route('POST', /^\/api\/players\/action$/, async (req, body) => {
  const { action, steamid } = body;
  if (!/^\d{17}$/.test(steamid || '')) throw httpError(400, 'SteamID64 invalido');
  const limpio = (s) => String(s || '').replace(/["\r\n]/g, '').slice(0, 120);
  let cmd;
  switch (action) {
    case 'kick': cmd = `kick ${steamid} "${limpio(body.reason) || 'Expulsado desde el panel'}"`; break;
    case 'ban': cmd = `banid ${steamid} "${limpio(body.name)}" "${limpio(body.reason) || 'Baneado desde el panel'}"`; break;
    case 'give': {
      if (!/^[\w.-]+$/.test(body.item || '')) throw httpError(400, 'Shortname invalido');
      const n = Math.max(1, Math.min(1000000, parseInt(body.amount, 10) || 1));
      cmd = `inventory.giveto ${steamid} ${body.item} ${n}`;
      break;
    }
    default: throw httpError(400, 'Accion desconocida');
  }
  pushLog('command', cmd);
  return { output: await rcon.send(cmd) };
});

// ─────────────────────────────────────────────────────────────────────────────
//  Servidor HTTP
// ─────────────────────────────────────────────────────────────────────────────
const server = http.createServer(async (req, res) => {
  if (!ALLOWED_HOSTS.has(req.headers.host || '')) return send(res, 403, { error: 'Host no permitido' });
  const url = new URL(req.url, `http://${req.headers.host}`);

  try {
    // Consola en directo (SSE). EventSource no manda cabeceras: token en la query
    if (url.pathname === '/api/stream') {
      if (url.searchParams.get('token') !== TOKEN) return send(res, 401, { error: 'Token invalido' });
      res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-store', connection: 'keep-alive' });
      res.write(`event: status\ndata: ${JSON.stringify({ ...status, rcon: rcon.connected })}\n\n`);
      res.write(`event: backlog\ndata: ${JSON.stringify(logs.slice(-400))}\n\n`);
      res.write(`event: history\ndata: ${JSON.stringify(history)}\n\n`);
      sseClients.add(res);
      const ping = setInterval(() => res.write(': ping\n\n'), 20000);
      req.on('close', () => { clearInterval(ping); sseClients.delete(res); });
      return;
    }

    if (url.pathname.startsWith('/api/')) {
      if (req.headers['x-panel-token'] !== TOKEN) return send(res, 401, { error: 'Token invalido' });
      for (const r of routes) {
        if (r.method !== req.method) continue;
        const m = url.pathname.match(r.pattern);
        if (!m) continue;
        const body = ['POST', 'PUT'].includes(req.method) ? await readBody(req) : {};
        return send(res, 200, await r.fn(req, body, url, m));
      }
      return send(res, 404, { error: 'Ruta no encontrada' });
    }

    if (req.method !== 'GET') return send(res, 405, { error: 'Metodo no permitido' });

    // Render del mapa (una <img> no puede mandar el token; es solo una imagen)
    const render = url.pathname.match(/^\/render\/(\d{3,5})_(\d{1,10})\.png$/);
    if (render) {
      try { return send(res, 200, await fsp.readFile(path.join(SERVER_DIR, `map_${render[1]}_${render[2]}.png`)), 'image/png'); }
      catch { return send(res, 404, { error: 'Sin render' }); }
    }

    return serveStatic(req, res, url.pathname);
  } catch (e) {
    return send(res, e.code >= 400 && e.code < 600 ? e.code : 500, { error: e.message || 'Error interno' });
  }
});

server.on('error', (e) => {
  if (e.code === 'EADDRINUSE') {
    console.log(`\n  El puerto ${PORT} ya esta en uso: el panel ya esta abierto en http://127.0.0.1:${PORT}\n`);
    process.exit(0);
  }
  throw e;
});

server.listen(PORT, HOST, () => {
  const link = `http://127.0.0.1:${PORT}`;
  console.log('');
  console.log('  RGL Control  ·  panel del servidor de Rust');
  console.log(`  ${link}`);
  console.log('  (Ctrl+C para cerrar el panel; el servidor de Rust sigue a lo suyo)');
  console.log('');
  pushLog('system', 'Panel iniciado');
  rcon.connect();
  refreshStatus().catch(() => {});
  if (!process.env.PANEL_NO_OPEN) spawn('cmd.exe', ['/c', 'start', '', link], { detached: true, stdio: 'ignore' }).unref();
});

/* ═══════════════════════════════════════════════════════════════════════════
   RGL Control — app
   ═══════════════════════════════════════════════════════════════════════════ */
(() => {
  'use strict';

  const TOKEN = document.querySelector('meta[name="panel-token"]').content;
  const { LineChart, sparkline, compact, hhmmss } = window.Charts;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  // ───────────────────────────── API ─────────────────────────────
  async function api(path, { method = 'GET', body } = {}) {
    const r = await fetch(path, {
      method,
      headers: { 'x-panel-token': TOKEN, 'content-type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    // El token cambia cada vez que arranca el panel: si se ha reiniciado, recargar la pagina
    if (r.status === 401) { location.reload(); throw new Error('El panel se ha reiniciado, recargando…'); }
    let j = {};
    try { j = await r.json(); } catch { /* sin cuerpo */ }
    if (!r.ok) throw new Error(j.error || `Error ${r.status}`);
    return j;
  }

  // ───────────────────────────── UI helpers ─────────────────────────────
  function toast(msg, type = 'ok', detail) {
    const t = document.createElement('div');
    t.className = `toast ${type}`;
    const body = document.createElement('div');
    body.textContent = msg;
    if (detail) { const pre = document.createElement('pre'); pre.textContent = detail; body.appendChild(pre); }
    t.appendChild(body);
    $('#toasts').appendChild(t);
    setTimeout(() => { t.style.opacity = '0'; t.style.transition = 'opacity .3s'; setTimeout(() => t.remove(), 300); }, detail ? 7000 : 3800);
  }

  function confirmar(title, text, okLabel = 'Confirmar', danger = true) {
    return new Promise((resolve) => {
      const m = $('#modal');
      $('#modalTitle').textContent = title;
      $('#modalText').textContent = text;
      const ok = $('#modalOk'), cancel = $('#modalCancel');
      ok.textContent = okLabel;
      ok.className = danger ? 'btn btn-danger' : 'btn btn-primary';
      m.hidden = false;
      ok.focus();
      const done = (v) => { m.hidden = true; ok.onclick = cancel.onclick = m.onclick = null; document.removeEventListener('keydown', key); resolve(v); };
      const key = (e) => { if (e.key === 'Escape') done(false); };
      ok.onclick = () => done(true);
      cancel.onclick = () => done(false);
      m.onclick = (e) => { if (e.target === m) done(false); };
      document.addEventListener('keydown', key);
    });
  }

  async function busy(btn, fn) {
    if (btn) { btn.classList.add('is-busy'); btn.disabled = true; }
    try { return await fn(); }
    catch (e) { toast(e.message, 'err'); throw e; }
    finally { if (btn) { btn.classList.remove('is-busy'); btn.disabled = false; } }
  }

  const fmtUptime = (s) => {
    s = Math.floor(s || 0);
    const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60);
    return d ? `${d}d ${h}h` : h ? `${h}h ${m}m` : `${m}m ${s % 60}s`;
  };
  const fmtBytes = (b) => b >= 1073741824 ? (b / 1073741824).toFixed(2) + ' GB' : b >= 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.round(b / 1024) + ' KB';
  const switchHtml = (checked, attrs = '') => `<label class="switch"><input type="checkbox" ${checked ? 'checked' : ''} ${attrs}><span></span></label>`;

  // ───────────────────────────── estado global ─────────────────────────────
  const S = { status: null, logs: [], history: [], range: 15 * 60000, cleanup: [] };
  const live = { status: new Set(), log: new Set(), sample: new Set() };
  function on(kind, fn) { live[kind].add(fn); S.cleanup.push(() => live[kind].delete(fn)); }
  function every(ms, fn) { const id = setInterval(fn, ms); S.cleanup.push(() => clearInterval(id)); }

  function serverState(st) {
    if (!st) return 'unknown';
    if (st.rcon && st.info) return 'online';
    if (st.process) return 'starting';
    return 'offline';
  }
  const STATE_TXT = { online: 'En línea', starting: 'Arrancando', offline: 'Apagado', unknown: '—' };

  // ───────────────────────────── tema ─────────────────────────────
  function setTheme(t) {
    document.documentElement.dataset.theme = t;
    try { localStorage.setItem('rgl-theme', t); } catch { /* sin storage */ }
    setTimeout(() => window.dispatchEvent(new Event('resize')), 30);
  }
  try { setTheme(localStorage.getItem('rgl-theme') || 'dark'); } catch { setTheme('dark'); }
  $('#themeToggle').onclick = () => setTheme(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark');

  // ───────────────────────────── stream en directo ─────────────────────────────
  function connectStream() {
    const es = new EventSource('/api/stream?token=' + TOKEN);
    es.addEventListener('status', (e) => onStatus(JSON.parse(e.data)));
    es.addEventListener('backlog', (e) => { S.logs = JSON.parse(e.data); live.log.forEach((f) => f(null)); });
    es.addEventListener('log', (e) => {
      const l = JSON.parse(e.data);
      S.logs.push(l);
      if (S.logs.length > 2000) S.logs.shift();
      live.log.forEach((f) => f(l));
    });
    es.addEventListener('history', (e) => { S.history = JSON.parse(e.data); live.sample.forEach((f) => f()); });
    es.addEventListener('sample', (e) => {
      S.history.push(JSON.parse(e.data));
      if (S.history.length > 1200) S.history.shift();
      live.sample.forEach((f) => f());
    });
    es.onerror = () => {
      const p = $('#pillServer');
      p.className = 'pill is-off';
      p.querySelector('b').textContent = 'Panel sin conexión';
      // Si el panel ha vuelto con otro token, el stream nunca reconectaria: recargar
      setTimeout(() => fetch('/api/status', { headers: { 'x-panel-token': TOKEN } })
        .then((r) => { if (r.status === 401) location.reload(); }).catch(() => {}), 2000);
    };
  }

  function onStatus(st) {
    S.status = st;
    const state = serverState(st);
    const app = $('.app');
    app.classList.toggle('online', state === 'online');
    app.classList.toggle('starting', state === 'starting');

    const p = $('#pillServer');
    p.className = 'pill ' + (state === 'online' ? 'is-on' : state === 'starting' ? 'is-mid' : 'is-off');
    p.querySelector('b').textContent = STATE_TXT[state];
    const r = $('#pillRcon');
    r.className = 'pill pill-ghost ' + (st.rcon ? 'is-on' : 'is-off');

    $$('[data-power]').forEach((b) => {
      const a = b.dataset.power;
      b.disabled = a === 'start' ? st.process : state !== 'online';
    });
    live.status.forEach((f) => f(st));
  }

  $$('[data-power]').forEach((b) => b.addEventListener('click', async () => {
    const action = b.dataset.power;
    if (action === 'stop' && !(await confirmar('Apagar el servidor', 'Se guarda el mundo y se cierra RustDedicated. Los jugadores conectados se desconectarán.', 'Apagar'))) return;
    if (action === 'restart' && !(await confirmar('Reiniciar el servidor', 'Se guarda el mundo, se cierra y start.bat lo vuelve a arrancar a los 10 segundos.', 'Reiniciar'))) return;
    await busy(b, async () => {
      await api('/api/power', { method: 'POST', body: { action } });
      toast({ start: 'Arrancando el servidor…', stop: 'Apagando el servidor…', restart: 'Reiniciando el servidor…', save: 'Mundo guardado' }[action]);
    }).catch(() => {});
  }));

  // ───────────────────────────── consola ─────────────────────────────
  const QUICK = ['serverinfo', 'playerlist', 'oxide.plugins', 'server.save', 'env.time 12', 'world.monuments', 'status'];

  function mountConsole(host, { mini = false } = {}) {
    host.innerHTML = `
      <div class="crt">
        ${mini ? '' : `
        <div class="crt-bar">
          <div class="seg" data-f>
            <button class="on" data-type="all" type="button">Todo</button>
            <button data-type="info" type="button">Info</button>
            <button data-type="warn" type="button">Avisos</button>
            <button data-type="error" type="button">Errores</button>
            <button data-type="chat" type="button">Chat</button>
            <button data-type="command" type="button">Comandos</button>
          </div>
          <input class="input" data-q placeholder="filtrar…" aria-label="Filtrar consola">
          <span class="spacer" style="margin-left:auto"></span>
          <button class="btn btn-sm" data-pause type="button">Pausar scroll</button>
          <button class="btn btn-sm" data-clear type="button">Limpiar vista</button>
        </div>`}
        <div class="crt-log ${mini ? 'mini' : ''}" data-log role="log" aria-live="off"></div>
        <form class="crt-input" data-form autocomplete="off">
          <span class="prompt">&gt;</span>
          <input data-cmd placeholder="${mini ? 'comando…' : 'escribe un comando del servidor y pulsa Enter  ·  ↑ ↓ historial'}" aria-label="Comando">
        </form>
        ${mini ? '' : `<div class="crt-bar" style="border-top:1px solid #2c261b;border-bottom:0"><div class="chips">${QUICK.map((q) => `<button class="chip" type="button" data-quick="${esc(q)}">${esc(q)}</button>`).join('')}</div></div>`}
      </div>`;

    const logEl = $('[data-log]', host);
    const cmd = $('[data-cmd]', host);
    let filter = 'all', q = '', paused = false, since = 0;
    let hist = [];
    try { hist = JSON.parse(localStorage.getItem('rgl-hist') || '[]'); } catch { /* nada */ }
    let hi = hist.length;

    const visible = (l) => l.id > since && (filter === 'all' || l.type === filter || (filter === 'info' && l.type === 'system')) && (!q || l.text.toLowerCase().includes(q));
    const lineEl = (l) => {
      const d = document.createElement('div');
      d.className = 'crt-line ' + l.type;
      const ts = document.createElement('span');
      ts.className = 'ts';
      ts.textContent = new Date(l.t).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
      const tx = document.createElement('span');
      tx.className = 'tx';
      tx.textContent = l.text;
      d.append(ts, tx);
      return d;
    };
    const nearBottom = () => logEl.scrollHeight - logEl.scrollTop - logEl.clientHeight < 40;
    const renderAll = () => {
      logEl.textContent = '';
      const frag = document.createDocumentFragment();
      S.logs.filter(visible).slice(mini ? -60 : -1200).forEach((l) => frag.appendChild(lineEl(l)));
      logEl.appendChild(frag);
      logEl.scrollTop = logEl.scrollHeight;
    };
    renderAll();

    on('log', (l) => {
      if (!l) return renderAll();
      if (!visible(l)) return;
      const stick = !paused && nearBottom();
      logEl.appendChild(lineEl(l));
      while (logEl.childElementCount > (mini ? 80 : 1500)) logEl.firstChild.remove();
      if (stick) logEl.scrollTop = logEl.scrollHeight;
    });

    const run = async (c) => {
      c = c.trim();
      if (!c) return;
      if (hist[hist.length - 1] !== c) hist.push(c);
      hist = hist.slice(-80);
      hi = hist.length;
      try { localStorage.setItem('rgl-hist', JSON.stringify(hist)); } catch { /* nada */ }
      try { await api('/api/command', { method: 'POST', body: { command: c } }); }
      catch (e) { toast(e.message, 'err'); }
    };

    $('[data-form]', host).addEventListener('submit', (e) => { e.preventDefault(); const c = cmd.value; cmd.value = ''; run(c); });
    cmd.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowUp') { e.preventDefault(); if (hi > 0) cmd.value = hist[--hi] || ''; }
      if (e.key === 'ArrowDown') { e.preventDefault(); if (hi < hist.length) { hi++; cmd.value = hist[hi] || ''; } }
    });

    if (!mini) {
      $$('[data-f] button', host).forEach((b) => b.onclick = () => {
        $$('[data-f] button', host).forEach((x) => x.classList.toggle('on', x === b));
        filter = b.dataset.type;
        renderAll();
      });
      $('[data-q]', host).addEventListener('input', (e) => { q = e.target.value.toLowerCase(); renderAll(); });
      $('[data-pause]', host).onclick = (e) => { paused = !paused; e.target.textContent = paused ? 'Reanudar scroll' : 'Pausar scroll'; };
      $('[data-clear]', host).onclick = () => { since = S.logs.length ? S.logs[S.logs.length - 1].id : 0; renderAll(); };
      $$('[data-quick]', host).forEach((b) => b.onclick = () => run(b.dataset.quick));
      setTimeout(() => cmd.focus(), 50);
    }
  }

  // ───────────────────────────── VISTA: panel ─────────────────────────────
  const METRICS = [
    { key: 'fps', title: 'FPS del servidor', unit: 'fps', series: [{ key: 'fps', label: 'FPS', color: 'var(--series-1)' }] },
    { key: 'players', title: 'Jugadores conectados', unit: '', series: [{ key: 'players', label: 'Jugadores', color: 'var(--series-1)' }] },
    { key: 'entities', title: 'Entidades en el mundo', unit: '', series: [{ key: 'entities', label: 'Entidades', color: 'var(--series-1)' }] },
    { key: 'memory', title: 'Memoria del servidor', unit: 'MB', series: [{ key: 'memory', label: 'Memoria', color: 'var(--series-1)' }] },
    { key: 'net', title: 'Tráfico de red', unit: 'B/s', wide: true, series: [
      { key: 'netIn', label: 'Entrada', color: 'var(--series-1)' },
      { key: 'netOut', label: 'Salida', color: 'var(--series-2)' },
    ] },
  ];

  function viewPanel(v) {
    v.innerHTML = `
      <section class="hero">
        <div class="card hero-main">
          <div class="kicker">estado</div>
          <div class="hero-state"><span class="big-led"></span><h2 data-h="state">—</h2></div>
          <div class="hero-host" data-h="host">—</div>
          <div class="hero-meta">
            <div><span>Mapa</span><b data-h="map">—</b></div>
            <div><span>Tiempo encendido</span><b data-h="uptime">—</b></div>
            <div><span>Versión</span><b data-h="proto">—</b></div>
            <div><span>Mundo creado</span><b data-h="created">—</b></div>
          </div>
        </div>
        <div class="tiles">
          ${[['players', 'Jugadores'], ['fps', 'FPS'], ['entities', 'Entidades'], ['memory', 'Memoria']].map(([k, l]) => `
            <div class="card tile"><div class="label">${l}</div><div class="value" data-t="${k}">—</div><div class="spark" data-s="${k}"></div></div>`).join('')}
        </div>
      </section>

      <section class="stack">
        <div class="charts-bar">
          <h2>En directo</h2>
          <div class="seg" data-range>
            <button type="button" data-ms="300000">5 min</button>
            <button type="button" data-ms="900000">15 min</button>
            <button type="button" data-ms="3600000">1 hora</button>
          </div>
          <span class="note">una muestra cada 3 s · pasa el ratón por encima para ver cada valor</span>
          <button class="btn btn-sm" data-table type="button" style="margin-left:auto">Ver tabla</button>
        </div>
        <div class="charts-grid">
          ${METRICS.map((m) => `
            <div class="card chart-card ${m.wide ? 'wide' : ''}">
              <div class="card-h">
                <div><div class="kicker">${esc(m.unit || 'total')}</div><h2>${esc(m.title)}</h2></div>
                <span class="spacer"></span>
                ${m.series.length > 1 ? `<div class="legend">${m.series.map((s) => `<span><i style="background:${s.color}"></i>${esc(s.label)}</span>`).join('')}</div>` : ''}
                <div class="chart-now" data-now="${m.key}">—</div>
              </div>
              <div class="card-b"><div data-chart="${m.key}"></div></div>
            </div>`).join('')}
        </div>
        <div class="card" data-tablecard hidden>
          <div class="card-h"><div><div class="kicker">tabla</div><h2>Últimas 20 muestras</h2></div></div>
          <div class="card-b flush table-wrap"><table class="table"><thead><tr>
            <th>Hora</th><th class="num">FPS</th><th class="num">Jugadores</th><th class="num">Entidades</th><th class="num">Memoria MB</th><th class="num">Entrada B/s</th><th class="num">Salida B/s</th>
          </tr></thead><tbody data-tbody></tbody></table></div>
        </div>
      </section>

      <section class="card">
        <div class="card-h"><div><div class="kicker">consola</div><h2>Últimos eventos</h2></div><span class="spacer"></span><a class="btn btn-sm" href="#consola">Abrir consola completa</a></div>
        <div class="card-b" data-mini></div>
      </section>`;

    // graficas
    const charts = {};
    for (const m of METRICS) {
      charts[m.key] = new LineChart($(`[data-chart="${m.key}"]`, v), { title: m.title, unit: m.unit, series: m.series });
      S.cleanup.push(() => charts[m.key].destroy());
    }
    const setRange = (ms) => {
      S.range = ms;
      $$('[data-range] button', v).forEach((b) => b.classList.toggle('on', Number(b.dataset.ms) === ms));
      drawCharts();
    };
    $$('[data-range] button', v).forEach((b) => b.onclick = () => setRange(Number(b.dataset.ms)));

    const tableCard = $('[data-tablecard]', v);
    $('[data-table]', v).onclick = (e) => { tableCard.hidden = !tableCard.hidden; e.target.textContent = tableCard.hidden ? 'Ver tabla' : 'Ocultar tabla'; drawTable(); };

    function drawTable() {
      if (tableCard.hidden) return;
      $('[data-tbody]', v).innerHTML = S.history.slice(-20).reverse().map((s) => `
        <tr><td>${esc(hhmmss(s.t))}</td><td class="num">${s.fps}</td><td class="num">${s.players}</td><td class="num">${s.entities.toLocaleString('es-ES')}</td>
        <td class="num">${s.memory.toLocaleString('es-ES')}</td><td class="num">${s.netIn.toLocaleString('es-ES')}</td><td class="num">${s.netOut.toLocaleString('es-ES')}</td></tr>`).join('')
        || '<tr><td colspan="7" class="empty">Sin muestras todavía</td></tr>';
    }

    function drawCharts() {
      for (const m of METRICS) charts[m.key].setData(S.history, S.range);
      const last = S.history[S.history.length - 1];
      const fresh = last && Date.now() - last.t < 10000;
      for (const m of METRICS) {
        const el = $(`[data-now="${m.key}"]`, v);
        if (!fresh) { el.textContent = '—'; continue; }
        el.innerHTML = m.series.length > 1
          ? m.series.map((s) => `${esc(compact(last[s.key]))}`).join(' <small>/</small> ') + `<small>${esc(m.unit)}</small>`
          : `${esc(compact(last[m.series[0].key]))}<small>${esc(m.unit)}</small>`;
      }
      // tarjetas
      const tail = S.history.slice(-40);
      const st = S.status;
      const info = st && st.info;
      $('[data-t="players"]', v).innerHTML = info ? `${info.Players}<small>/ ${info.MaxPlayers}</small>` : '—';
      $('[data-t="fps"]', v).innerHTML = info ? `${Math.round(info.Framerate)}<small>fps</small>` : '—';
      $('[data-t="entities"]', v).textContent = info ? compact(info.EntityCount) : '—';
      $('[data-t="memory"]', v).innerHTML = info ? `${compact(info.Memory)}<small>MB</small>` : '—';
      for (const k of ['players', 'fps', 'entities', 'memory']) sparkline($(`[data-s="${k}"]`, v), fresh ? tail.map((s) => s[k]) : []);
      drawTable();
    }

    function drawHero(st) {
      const state = serverState(st);
      const info = st && st.info, l = (st && st.launch) || {};
      $('[data-h="state"]', v).textContent = STATE_TXT[state];
      $('[data-h="host"]', v).textContent = (info && info.Hostname) || l['server.hostname'] || '—';
      $('[data-h="map"]', v).textContent = l['server.worldsize'] ? `${l['server.worldsize']} · seed ${l['server.seed']}` : '—';
      $('[data-h="uptime"]', v).textContent = info ? fmtUptime(info.Uptime) : '—';
      $('[data-h="proto"]', v).textContent = info ? info.Protocol : '—';
      $('[data-h="created"]', v).textContent = info && info.SaveCreatedTime ? new Date(info.SaveCreatedTime + ' UTC').toLocaleString('es-ES', { dateStyle: 'short', timeStyle: 'short' }) : '—';
    }

    mountConsole($('[data-mini]', v), { mini: true });
    setRange(S.range);
    drawHero(S.status);
    on('sample', drawCharts);
    on('status', (st) => { drawHero(st); });
    every(5000, drawCharts);   // para que el eje de tiempo avance aunque el servidor este apagado
  }

  // ───────────────────────────── VISTA: mapa ─────────────────────────────
  async function viewMapa(v) {
    v.innerHTML = `<div class="empty"><span class="big">cargando…</span></div>`;
    const { args } = await api('/api/launch');
    const get = (k, d) => (args.find((a) => a.key === k) || {}).value ?? d;
    const cur = { seed: get('server.seed', '2026'), size: Number(get('server.worldsize', 3700)), level: get('server.level', 'Procedural Map') };
    const form = { ...cur };

    v.innerHTML = `
      <div class="grid g-side">
        <section class="card">
          <div class="card-h"><div><div class="kicker">start.bat</div><h2>Generación del mapa</h2></div></div>
          <div class="card-b stack">
            <div class="field">
              <label for="mSeed">Seed</label>
              <div class="input-group">
                <input class="input" id="mSeed" inputmode="numeric" value="${esc(form.seed)}">
                <button class="btn" data-dice type="button" title="Seed aleatorio">Aleatorio</button>
              </div>
              <span class="hint">Número entre 0 y 2147483647. Cada seed genera un mapa distinto.</span>
            </div>
            <div class="field">
              <label for="mSize">Tamaño · <b data-sizelbl>${form.size}</b> m</label>
              <input type="range" id="mSize" min="1000" max="6000" step="50" value="${form.size}">
              <div class="size-presets">${[2500, 3000, 3500, 3700, 4000, 4500].map((n) => `<button class="chip" type="button" data-size="${n}">${n}</button>`).join('')}</div>
              <span class="hint">Los servidores normales van de 3500 a 4500. Por debajo de 1500 casi todo es océano.</span>
            </div>
            <div class="field">
              <label for="mLevel">Tipo de mapa</label>
              <select class="select" id="mLevel"><option>Procedural Map</option></select>
            </div>
            <div class="callout"><span class="ico">!</span><div>Cambiar el seed o el tamaño genera un <b>mapa nuevo</b> al reiniciar (unos 7–8 minutos la primera vez). El save del mapa anterior se queda guardado por si vuelves a él.</div></div>
            <div class="row">
              <button class="btn btn-primary" data-save type="button">Guardar</button>
              <button class="btn" data-saverestart type="button">Guardar y reiniciar</button>
              <span class="note" data-dirty></span>
            </div>
          </div>
        </section>

        <section class="card">
          <div class="card-h">
            <div><div class="kicker">vista previa</div><h2>Así se verá</h2></div>
            <span class="spacer"></span>
            <div class="seg" data-src><button class="on" type="button" data-s="rm">RustMaps</button><button type="button" data-s="local">Render del servidor</button></div>
          </div>
          <div class="card-b stack" data-preview></div>
        </section>
      </div>

      <section class="card">
        <div class="card-h"><div><div class="kicker">server\\server\\${esc(get('server.identity', 'skintest'))}</div><h2>Mapas guardados</h2></div></div>
        <div class="card-b flush table-wrap" data-saves><div class="empty">cargando…</div></div>
      </section>`;

    const seedEl = $('#mSeed', v), sizeEl = $('#mSize', v);
    let src = 'rm', timer = null;

    const dirty = () => {
      const d = String(form.seed) !== String(cur.seed) || form.size !== cur.size;
      $('[data-dirty]', v).textContent = d ? 'Hay cambios sin guardar' : '';
      seedEl.classList.toggle('is-dirty', String(form.seed) !== String(cur.seed));
      return d;
    };
    const schedule = () => { dirty(); clearTimeout(timer); timer = setTimeout(preview, 500); };

    seedEl.addEventListener('input', () => { form.seed = seedEl.value.replace(/\D/g, '').slice(0, 10); seedEl.value = form.seed; schedule(); });
    sizeEl.addEventListener('input', () => { form.size = Number(sizeEl.value); $('[data-sizelbl]', v).textContent = form.size; schedule(); });
    $$('[data-size]', v).forEach((b) => b.onclick = () => { form.size = Number(b.dataset.size); sizeEl.value = form.size; $('[data-sizelbl]', v).textContent = form.size; schedule(); });
    $('[data-dice]', v).onclick = () => { form.seed = String(Math.floor(Math.random() * 2147483647)); seedEl.value = form.seed; schedule(); };
    $$('[data-src] button', v).forEach((b) => b.onclick = () => { src = b.dataset.s; $$('[data-src] button', v).forEach((x) => x.classList.toggle('on', x === b)); preview(); });

    async function preview() {
      const box = $('[data-preview]', v);
      if (!form.seed) { box.innerHTML = '<div class="empty">Escribe un seed</div>'; return; }
      if (src === 'local') return previewLocal(box);
      box.innerHTML = `<div class="map-frame"><div class="empty"><span class="big">buscando…</span></div></div>`;
      try {
        const d = await api(`/api/rustmaps?size=${form.size}&seed=${form.seed}`);
        const proto = S.status && S.status.info ? String(S.status.info.Protocol).split('.')[1] : null;
        let aviso = '';
        if (!d.found) aviso = `<div class="callout teal"><span class="ico">?</span><div>RustMaps no tiene generado este seed a ${form.size}. No pasa nada: el servidor lo genera igual, solo que no se puede ver antes. Puedes abrir el enlace y generarlo allí.</div></div>`;
        else if (d.custom) aviso = `<div class="callout err"><span class="ico">!</span><div>Este es un <b>mapa custom</b> de RustMaps: el servidor vanilla no genera este mapa con este seed.</div></div>`;
        else if (proto && d.saveVersion && String(d.saveVersion) !== proto) aviso = `<div class="callout"><span class="ico">!</span><div>Render de otra versión de Rust (${d.saveVersion}, el servidor es ${proto}): puede no coincidir exactamente.</div></div>`;
        box.innerHTML = `
          <div class="map-frame">${d.found ? `<img alt="Mapa ${form.size} seed ${esc(form.seed)}" src="${esc(d.image)}">` : '<div class="empty"><span class="big">sin render</span></div>'}</div>
          ${d.found ? `<div class="stats-row">
            <div class="mini-stat"><span>Tierra</span><b>${d.land ?? '—'}%</b></div>
            <div class="mini-stat"><span>Islas</span><b>${d.islands ?? '—'}</b></div>
            <div class="mini-stat"><span>Ríos</span><b>${d.rivers ?? '—'}</b></div>
            <div class="mini-stat"><span>Monumentos</span><b>${d.monuments ?? '—'}</b></div>
          </div>` : ''}
          ${aviso}
          <div class="row"><a class="btn btn-sm" href="${esc(d.url)}" target="_blank" rel="noopener noreferrer">Abrir en RustMaps ↗</a></div>`;
      } catch (e) {
        box.innerHTML = `<div class="callout err"><span class="ico">!</span><div>${esc(e.message)}</div></div>`;
      }
    }

    async function previewLocal(box) {
      const st = S.status;
      const running = st && st.info && String(st.launch['server.seed']) === String(form.seed) && Number(st.launch['server.worldsize']) === form.size;
      let img = null;
      try { img = await api(`/api/map/image?size=${form.size}&seed=${form.seed}`); } catch { /* sin render */ }
      box.innerHTML = `
        <div class="map-frame">${img ? `<img alt="Render del servidor" src="${esc(img.url)}?v=${Math.round(img.modified)}">` : '<div class="empty"><span class="big">sin render</span><div>Todavía no se ha renderizado este mapa</div></div>'}</div>
        <div class="note">${running ? 'Es el mapa que tiene cargado ahora el servidor.' : 'Solo se puede renderizar el mapa que tiene cargado el servidor.'}</div>
        <div class="row"><button class="btn" data-render type="button" ${running ? '' : 'disabled'}>Renderizar ahora</button><span class="note">tarda unos 20–40 s</span></div>`;
      const b = $('[data-render]', box);
      if (b) b.onclick = () => busy(b, async () => {
        const r = await api('/api/map/render', { method: 'POST' });
        if (!r.ok) throw new Error('El servidor no ha devuelto el render');
        toast('Mapa renderizado');
        previewLocal(box);
      }).catch(() => {});
    }

    async function save(restart) {
      if (!/^\d{1,10}$/.test(form.seed) || Number(form.seed) > 2147483647) throw new Error('Seed inválido');
      const nuevos = args.map((a) => a.key === 'server.seed' ? { ...a, value: form.seed } : a.key === 'server.worldsize' ? { ...a, value: String(form.size) } : a);
      await api('/api/launch', { method: 'PUT', body: { args: nuevos } });
      args.splice(0, args.length, ...nuevos);
      cur.seed = form.seed; cur.size = form.size;
      dirty();
      toast('Mapa guardado en start.bat');
      if (restart && S.status && S.status.rcon) await api('/api/power', { method: 'POST', body: { action: 'restart' } });
    }
    $('[data-save]', v).onclick = (e) => busy(e.currentTarget, () => save(false)).catch(() => {});
    $('[data-saverestart]', v).onclick = async (e) => {
      const b = e.currentTarget;
      if (!(await confirmar('Guardar y reiniciar', 'Se guarda el mapa en start.bat y se reinicia el servidor. Si el mapa es nuevo tardará unos minutos en generarse.', 'Reiniciar'))) return;
      busy(b, () => save(true)).catch(() => {});
    };

    // saves
    try {
      const { saves } = await api('/api/map/saves');
      $('[data-saves]', v).innerHTML = saves.length ? `<table class="table"><thead><tr><th>Mapa</th><th>Seed</th><th class="num">Ficheros</th><th class="num">Tamaño</th><th>Último guardado</th><th></th></tr></thead><tbody>
        ${saves.map((s) => `<tr><td>${s.size} m</td><td class="mono">${esc(s.seed)}</td><td class="num">${s.files}</td><td class="num">${fmtBytes(s.bytes)}</td>
        <td>${new Date(s.modified).toLocaleString('es-ES', { dateStyle: 'short', timeStyle: 'short' })}</td>
        <td class="actions">${String(s.seed) === String(cur.seed) && s.size === cur.size ? '<span class="tag accent">actual</span>' : ''}</td></tr>`).join('')}</tbody></table>`
        : '<div class="empty">Aún no hay mapas guardados</div>';
    } catch (e) { $('[data-saves]', v).innerHTML = `<div class="empty">${esc(e.message)}</div>`; }

    preview();
  }

  // ───────────────────────────── VISTA: servidor ─────────────────────────────
  const LAUNCH_FIELDS = [
    ['server.hostname', 'Nombre del servidor', 'text', 'Lo que se ve en la lista de servidores'],
    ['server.maxplayers', 'Jugadores máximos', 'number', ''],
    ['server.port', 'Puerto de juego', 'number', 'Por defecto 28015'],
    ['server.identity', 'Identidad', 'text', 'Carpeta del save y la config (server\\server\\…)'],
    ['rcon.port', 'Puerto RCON', 'number', 'El panel se conecta por aquí'],
    ['rcon.password', 'Contraseña RCON', 'password', ''],
    ['rcon.web', 'RCON web', 'bool01', 'Necesario para el panel'],
  ];
  const MAP_KEYS = new Set(['server.level', 'server.seed', 'server.worldsize']);

  const CVAR_HELP = {
    'server.hostname': 'Nombre en la lista de servidores', 'server.description': 'Descripción del servidor',
    'server.maxplayers': 'Jugadores máximos', 'server.tickrate': 'Ticks por segundo del servidor',
    'server.saveinterval': 'Segundos entre guardados automáticos', 'creative.allusers': 'Modo creativo para todos',
    'creative.freebuild': 'Construir sin gastar recursos', 'creative.freeplacement': 'Colocar sin restricciones',
    'creative.freerepair': 'Reparar sin esperar', 'creative.unlimitedio': 'Cables sin límite de longitud',
    'craft.instant': 'Crafteo instantáneo nativo (solo admins)', 'server.pve': 'Sin daño entre jugadores',
    'server.radiation': 'Radiación en monumentos', 'server.stability': 'Estabilidad de construcciones',
    'server.dropitems': 'Soltar items al morir', 'decay.scale': 'Velocidad del decay (0 = sin decay)',
    'decay.upkeep': 'Coste de mantenimiento del TC', 'env.time': 'Hora del día (0–24)',
    'env.progresstime': 'Que pase el tiempo', 'ai.think': 'NPCs activos', 'ai.move': 'NPCs se mueven',
    'bradley.enabled': 'Tanque Bradley en Launch Site', 'cargoship.event_enabled': 'Evento del barco de carga',
    'halloween.enabled': 'Evento de Halloween', 'xmas.enabled': 'Evento de Navidad',
    'antihack.enforcementlevel': 'Nivel del antihack (0 = desactivado)', 'antihack.terrain_protection': 'Protección de terreno',
    'antihack.terrain_kill': 'Matar al entrar en el terreno', 'antihack.admincheat': 'Los admins se saltan el antihack',
  };

  async function viewServidor(v) {
    v.innerHTML = `<div class="empty"><span class="big">cargando…</span></div>`;
    const [{ args }, cfg] = await Promise.all([api('/api/launch'), api('/api/servercfg')]);
    const known = new Set(LAUNCH_FIELDS.map((f) => f[0]));
    let extras = args.filter((a) => !known.has(a.key) && !MAP_KEYS.has(a.key)).map((a) => ({ ...a }));
    const val = (k) => (args.find((a) => a.key === k) || {}).value ?? '';

    v.innerHTML = `
      <section class="card">
        <div class="card-h">
          <div><div class="kicker">start.bat · se aplica al reiniciar</div><h2>Lanzamiento</h2></div>
          <span class="spacer"></span>
          <button class="btn btn-primary" data-savelaunch type="button">Guardar</button>
        </div>
        <div class="card-b stack">
          <div class="form-grid">
            ${LAUNCH_FIELDS.map(([k, label, type, hint]) => `
              <div class="field">
                <label for="l-${k}">${esc(label)}</label>
                ${type === 'bool01'
                  ? `<div class="row">${switchHtml(val(k) === '1', `id="l-${k}" data-l="${k}" data-bool01`)}<span class="note">${val(k) === '1' ? 'activado' : 'desactivado'}</span></div>`
                  : type === 'password'
                    ? `<div class="input-group"><input class="input" type="password" id="l-${k}" data-l="${k}" value="${esc(val(k))}"><button class="btn" type="button" data-peek>Ver</button></div>`
                    : `<input class="input" type="${type}" id="l-${k}" data-l="${k}" value="${esc(val(k))}">`}
                ${hint ? `<span class="hint">${esc(hint)}</span>` : ''}
              </div>`).join('')}
          </div>
          <div>
            <div class="label" style="margin-bottom:8px">Argumentos extra</div>
            <div class="stack" data-extras style="gap:8px"></div>
            <button class="btn btn-sm" data-addextra type="button" style="margin-top:10px">+ Añadir argumento</button>
          </div>
          <div class="note">El seed, el tamaño y el tipo de mapa se cambian en <a href="#mapa">Mapa</a>.</div>
        </div>
      </section>

      <section class="card">
        <div class="card-h">
          <div><div class="kicker">server\\server\\${esc(val('server.identity') || 'skintest')}\\cfg\\server.cfg</div><h2>Configuración del mundo</h2></div>
          <span class="spacer"></span>
          <div class="seg" data-mode><button class="on" type="button" data-m="form">Formulario</button><button type="button" data-m="raw">Texto</button></div>
          <button class="btn" data-savecfg type="button">Guardar</button>
          <button class="btn btn-primary" data-applycfg type="button">Guardar y aplicar en vivo</button>
        </div>
        <div data-cfg></div>
      </section>`;

    // lanzamiento
    $$('[data-peek]', v).forEach((b) => b.onclick = () => { const i = b.previousElementSibling; i.type = i.type === 'password' ? 'text' : 'password'; b.textContent = i.type === 'password' ? 'Ver' : 'Ocultar'; });
    $$('[data-bool01]', v).forEach((i) => i.onchange = () => { i.closest('.row').querySelector('.note').textContent = i.checked ? 'activado' : 'desactivado'; });

    const drawExtras = () => {
      $('[data-extras]', v).innerHTML = extras.length ? extras.map((a, i) => `
        <div class="input-group"><input class="input" data-ek="${i}" value="${esc(a.key)}" placeholder="server.description" style="max-width:260px"><input class="input" data-ev="${i}" value="${esc(a.value)}" placeholder="valor">
        <button class="icon-btn" type="button" data-edel="${i}" title="Quitar"><svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg></button></div>`).join('')
        : '<div class="note">Ninguno. Aquí puedes añadir cualquier +convar de arranque (server.description, server.url…).</div>';
      $$('[data-ek]', v).forEach((i) => i.oninput = () => { extras[i.dataset.ek].key = i.value.trim(); });
      $$('[data-ev]', v).forEach((i) => i.oninput = () => { extras[i.dataset.ev].value = i.value; });
      $$('[data-edel]', v).forEach((b) => b.onclick = () => { extras.splice(Number(b.dataset.edel), 1); drawExtras(); });
    };
    drawExtras();
    $('[data-addextra]', v).onclick = () => { extras.push({ key: '', value: '' }); drawExtras(); };

    $('[data-savelaunch]', v).onclick = (e) => busy(e.currentTarget, async () => {
      const out = [];
      // se respeta el orden original; los campos conocidos se actualizan en su sitio
      for (const a of args) {
        if (known.has(a.key)) {
          const el = $(`[data-l="${a.key}"]`, v);
          out.push({ key: a.key, value: el.type === 'checkbox' ? (el.checked ? '1' : '0') : el.value.trim() });
        } else if (MAP_KEYS.has(a.key)) out.push(a);
      }
      for (const [k] of LAUNCH_FIELDS) if (!out.some((a) => a.key === k)) {
        const el = $(`[data-l="${k}"]`, v);
        const value = el.type === 'checkbox' ? (el.checked ? '1' : '0') : el.value.trim();
        if (value !== '') out.push({ key: k, value });
      }
      for (const x of extras) if (x.key) out.push({ key: x.key.replace(/^\+/, ''), value: x.value });
      const r = await api('/api/launch', { method: 'PUT', body: { args: out } });
      args.splice(0, args.length, ...r.args);
      toast('start.bat guardado · se aplica al reiniciar el servidor');
    }).catch(() => {});

    // server.cfg
    let mode = 'form';
    let model = { header: cfg.header, sections: cfg.sections.map((s) => ({ ...s, items: s.items.map((i) => ({ ...i })) })) };
    const original = new Map();
    cfg.sections.forEach((s) => s.items.forEach((i) => original.set(i.key, i.value)));
    let raw = cfg.raw;

    const isBool = (x) => x === 'true' || x === 'false';
    const isNum = (x) => /^-?\d+(\.\d+)?$/.test(x);

    function drawCfg() {
      const box = $('[data-cfg]', v);
      if (mode === 'raw') {
        box.innerHTML = `<div class="card-b"><textarea class="textarea code" data-raw spellcheck="false">${esc(raw)}</textarea></div>`;
        $('[data-raw]', box).oninput = (e) => { raw = e.target.value; };
        return;
      }
      box.innerHTML = model.sections.map((s, si) => `
        <div class="cfg-section">
          <div class="card-h" style="border-top:1px dashed var(--line);border-bottom:0;padding-bottom:4px">
            <div class="kicker" style="margin:0">${esc(s.title)}</div>
          </div>
          ${s.notes.length ? `<div class="sec-notes">${s.notes.map(esc).join('<br>')}</div>` : ''}
          ${s.items.map((it, ii) => `
            <div class="cfg-row ${original.get(it.key) !== it.value ? 'dirty' : ''}" data-row="${si}.${ii}">
              <div class="k">${esc(it.key)}${CVAR_HELP[it.key] || it.comment ? `<small>${esc(CVAR_HELP[it.key] || it.comment)}</small>` : ''}</div>
              <div class="v">
                ${isBool(it.value)
                  ? `${switchHtml(it.value === 'true', `data-bool="${si}.${ii}" aria-label="${esc(it.key)}"`)}<span class="note">${it.value}</span>`
                  : `<input class="input" data-val="${si}.${ii}" value="${esc(it.value)}" ${isNum(it.value) ? 'inputmode="decimal"' : ''} aria-label="${esc(it.key)}">`}
              </div>
              <button class="icon-btn" type="button" data-del="${si}.${ii}" title="Quitar convar"><svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg></button>
            </div>`).join('')}
          <div class="cfg-row" style="grid-template-columns:minmax(180px,1.1fr) minmax(0,1fr) auto">
            <input class="input" data-newk="${si}" placeholder="+ nueva convar (ej. server.url)">
            <input class="input" data-newv="${si}" placeholder="valor">
            <button class="btn btn-sm" type="button" data-add="${si}">Añadir</button>
          </div>
        </div>`).join('');

      const at = (p) => { const [a, b] = p.split('.').map(Number); return model.sections[a].items[b]; };
      const mark = (p) => { const it = at(p); $(`[data-row="${p}"]`, box).classList.toggle('dirty', original.get(it.key) !== it.value); };
      $$('[data-bool]', box).forEach((i) => i.onchange = () => { const it = at(i.dataset.bool); it.value = i.checked ? 'true' : 'false'; i.closest('.v').querySelector('.note').textContent = it.value; mark(i.dataset.bool); });
      $$('[data-val]', box).forEach((i) => i.oninput = () => { at(i.dataset.val).value = i.value; mark(i.dataset.val); });
      $$('[data-del]', box).forEach((b) => b.onclick = () => { const [a, c] = b.dataset.del.split('.').map(Number); model.sections[a].items.splice(c, 1); drawCfg(); });
      $$('[data-add]', box).forEach((b) => b.onclick = () => {
        const si = b.dataset.add, k = $(`[data-newk="${si}"]`, box).value.trim(), val2 = $(`[data-newv="${si}"]`, box).value.trim();
        if (!/^[\w.]+$/.test(k)) return toast('Nombre de convar inválido', 'err');
        model.sections[si].items.push({ key: k, value: val2, comment: '' });
        drawCfg();
      });
    }

    $$('[data-mode] button', v).forEach((b) => b.onclick = async () => {
      const next = b.dataset.m;
      if (next === mode) return;
      if (next === 'raw') {
        // el formulario pasa a texto tal cual quedaria guardado (con los cambios sin guardar)
        raw = renderCfgLocal(model);
      } else {
        toast('Guarda el texto para volver al formulario con esos cambios', 'info');
        const fresh = await api('/api/servercfg');
        model = { header: fresh.header, sections: fresh.sections };
      }
      mode = next;
      $$('[data-mode] button', v).forEach((x) => x.classList.toggle('on', x === b));
      drawCfg();
    });

    function renderCfgLocal(m) {
      const out = [...m.header];
      for (const s of m.sections) {
        out.push('', `// ---------- ${s.title} ----------`);
        s.notes.forEach((n) => out.push(`// ${n}`));
        s.items.forEach((it) => out.push(`${it.key} ${it.value === '' || /\s/.test(it.value) ? `"${it.value}"` : it.value}${it.comment ? '  // ' + it.comment : ''}`));
      }
      return out.join('\n') + '\n';
    }

    function changedItems() {
      const out = [];
      model.sections.forEach((s) => s.items.forEach((it) => { if (original.get(it.key) !== it.value) out.push({ key: it.key, value: it.value }); }));
      return out;
    }

    async function saveCfg(apply) {
      const body = mode === 'raw' ? { raw } : { header: model.header, sections: model.sections };
      if (apply && mode === 'form') body.apply = changedItems();
      if (apply && mode === 'raw') throw new Error('Aplicar en vivo solo funciona desde el formulario');
      const r = await api('/api/servercfg', { method: 'PUT', body });
      const fresh = await api('/api/servercfg');
      original.clear();
      fresh.sections.forEach((s) => s.items.forEach((i) => original.set(i.key, i.value)));
      raw = fresh.raw;
      if (mode === 'form') model = { header: fresh.header, sections: fresh.sections };
      drawCfg();
      toast(apply ? `server.cfg guardado · ${r.applied.length} convars aplicadas en vivo` : 'server.cfg guardado · se aplica al reiniciar');
    }
    $('[data-savecfg]', v).onclick = (e) => busy(e.currentTarget, () => saveCfg(false)).catch(() => {});
    $('[data-applycfg]', v).onclick = (e) => {
      if (!(S.status && S.status.rcon)) return toast('El servidor tiene que estar encendido para aplicar en vivo', 'err');
      busy(e.currentTarget, () => saveCfg(true)).catch(() => {});
    };

    drawCfg();
  }

  // ───────────────────────────── VISTA: permisos ─────────────────────────────
  async function viewPermisos(v) {
    v.innerHTML = `
      <div class="grid g-side" style="grid-template-columns:minmax(0,.8fr) minmax(0,1.6fr)">
        <div class="stack" style="gap:22px">
          <section class="card">
            <div class="card-h"><div><div class="kicker">oxide</div><h2>Grupos</h2></div></div>
            <div data-groups><div class="empty">cargando…</div></div>
            <form class="card-b" data-addgroup style="border-top:1px dashed var(--line);padding-top:14px">
              <div class="input-group"><input class="input" name="g" placeholder="nuevo grupo" aria-label="Nuevo grupo"><button class="btn" type="submit">Crear</button></div>
            </form>
          </section>
          <section class="card">
            <div class="card-h"><div><div class="kicker">permiso a un jugador</div><h2>Por SteamID</h2></div></div>
            <form class="card-b stack" data-userperm>
              <div class="field"><label>SteamID64</label><input class="input" name="id" inputmode="numeric" placeholder="7656119…"></div>
              <div class="field"><label>Permiso</label><input class="input" name="perm" list="permList" placeholder="skins.use"></div>
              <div class="row"><button class="btn btn-primary" type="submit" data-a="grant">Conceder</button><button class="btn btn-danger" type="submit" data-a="revoke">Quitar</button></div>
            </form>
          </section>
        </div>
        <section class="card">
          <div class="card-h">
            <div><div class="kicker">permisos del grupo</div><h2 data-gname>—</h2></div>
            <span class="spacer"></span>
            <input class="input" data-pq placeholder="buscar permiso…" style="max-width:220px" aria-label="Buscar permiso">
            <button class="btn btn-danger btn-sm" data-delgroup type="button">Borrar grupo</button>
          </div>
          <div data-perms><div class="empty">cargando…</div></div>
        </section>
      </div>

      <section class="card">
        <div class="card-h">
          <div><div class="kicker">users.cfg · auth level de Rust</div><h2>Administradores y moderadores</h2></div>
          <span class="spacer"></span>
          <label class="row note" style="gap:8px">${switchHtml(true, 'data-live')} aplicar también en vivo</label>
          <button class="btn btn-primary" data-saveusers type="button">Guardar</button>
        </div>
        <div class="card-b flush table-wrap" data-users><div class="empty">cargando…</div></div>
        <div class="card-b" style="border-top:1px dashed var(--line)"><button class="btn btn-sm" data-adduser type="button">+ Añadir</button>
        <span class="note" style="margin-left:10px">owner = acceso total (auth 2) · moderator = auth 1. Quitar a alguien aquí se aplica al reiniciar.</span></div>
      </section>
      <datalist id="permList"></datalist>`;

    let data = null, sel = 'default', q = '';

    async function load() {
      if (!(S.status && S.status.rcon)) {
        const off = '<div class="empty"><span class="big">sin rcon</span>Enciende el servidor para gestionar los permisos de Oxide.</div>';
        $('[data-groups]', v).innerHTML = off;
        $('[data-perms]', v).innerHTML = off;
        return;
      }
      try { data = await api('/api/perms'); }
      catch (e) { $('[data-perms]', v).innerHTML = `<div class="empty">${esc(e.message)}</div>`; return; }
      if (!data.groups.includes(sel)) sel = data.groups[0] || 'default';
      $('#permList').innerHTML = data.perms.map((p) => `<option value="${esc(p)}">`).join('');
      drawGroups();
      drawPerms();
    }

    function drawGroups() {
      $('[data-groups]', v).innerHTML = `<div class="groups">${data.groups.map((g) => `
        <button class="group-btn ${g === sel ? 'on' : ''}" type="button" data-g="${esc(g)}"><b>${esc(g)}</b><span>${(data.detail[g] || []).length} permisos</span></button>`).join('')}</div>`;
      $$('[data-g]', v).forEach((b) => b.onclick = () => { sel = b.dataset.g; drawGroups(); drawPerms(); });
    }

    function drawPerms() {
      $('[data-gname]', v).textContent = sel;
      const has = new Set(data.detail[sel] || []);
      const perms = data.perms.filter((p) => !q || p.includes(q));
      const byPlugin = {};
      perms.forEach((p) => { const k = p.split('.')[0]; (byPlugin[k] = byPlugin[k] || []).push(p); });
      const box = $('[data-perms]', v);
      box.innerHTML = Object.keys(byPlugin).length ? Object.entries(byPlugin).map(([plugin, list]) => `
        <div class="perm-block"><h4>${esc(plugin)} · ${list.filter((p) => has.has(p)).length}/${list.length}</h4>
        <div class="perm-list">${list.map((p) => `<label class="perm-item">${switchHtml(has.has(p), `data-p="${esc(p)}"`)}<span>${esc(p)}</span></label>`).join('')}</div></div>`).join('')
        : '<div class="empty">Ningún permiso coincide</div>';
      $$('[data-p]', box).forEach((i) => i.onchange = async () => {
        const item = i.closest('.perm-item');
        item.classList.add('busy');
        try {
          await api('/api/perms', { method: 'POST', body: { action: i.checked ? 'grant' : 'revoke', kind: 'group', target: sel, perm: i.dataset.p } });
          const set = new Set(data.detail[sel] || []);
          i.checked ? set.add(i.dataset.p) : set.delete(i.dataset.p);
          data.detail[sel] = [...set];
          toast(`${i.checked ? 'Concedido' : 'Quitado'} ${i.dataset.p} a ${sel}`);
          drawGroups();
          drawPerms();   // actualiza los contadores "x/y" de cada plugin
          return;
        } catch (e) { i.checked = !i.checked; toast(e.message, 'err'); }
        item.classList.remove('busy');
      });
    }

    $('[data-pq]', v).oninput = (e) => { q = e.target.value.trim().toLowerCase(); if (data) drawPerms(); };

    $('[data-addgroup]', v).onsubmit = async (e) => {
      e.preventDefault();
      const name = e.target.g.value.trim();
      if (!name) return;
      try { await api('/api/groups', { method: 'POST', body: { action: 'add', name } }); e.target.g.value = ''; sel = name; toast(`Grupo ${name} creado`); load(); }
      catch (err) { toast(err.message, 'err'); }
    };
    $('[data-delgroup]', v).onclick = async () => {
      if (['default', 'admin'].includes(sel)) return toast(`El grupo ${sel} es de Oxide y no se puede borrar`, 'err');
      if (!(await confirmar(`Borrar el grupo ${sel}`, 'Se quitan también todos sus permisos y miembros.', 'Borrar'))) return;
      try { await api('/api/groups', { method: 'POST', body: { action: 'remove', name: sel } }); toast(`Grupo ${sel} borrado`); sel = 'default'; load(); }
      catch (err) { toast(err.message, 'err'); }
    };

    let action = 'grant';
    $$('[data-userperm] [data-a]', v).forEach((b) => b.onclick = () => { action = b.dataset.a; });
    $('[data-userperm]', v).onsubmit = async (e) => {
      e.preventDefault();
      const id = e.target.id.value.trim(), perm = e.target.perm.value.trim();
      if (!/^\d{17}$/.test(id)) return toast('El SteamID64 tiene 17 dígitos', 'err');
      try {
        const r = await api('/api/perms', { method: 'POST', body: { action, kind: 'user', target: id, perm } });
        toast(`${action === 'grant' ? 'Concedido' : 'Quitado'} ${perm}`, 'ok', r.output || undefined);
      } catch (err) { toast(err.message, 'err'); }
    };

    // users.cfg
    let users = (await api('/api/users')).users;
    function drawUsers() {
      $('[data-users]', v).innerHTML = users.length ? `<table class="table"><thead><tr><th>Rol</th><th>SteamID64</th><th>Nombre</th><th>Motivo</th><th></th></tr></thead><tbody>
        ${users.map((u, i) => `<tr>
          <td><select class="select" data-u="${i}" data-f="role" style="width:150px"><option value="ownerid" ${u.role === 'ownerid' ? 'selected' : ''}>owner</option><option value="moderatorid" ${u.role === 'moderatorid' ? 'selected' : ''}>moderator</option></select></td>
          <td><input class="input mono" data-u="${i}" data-f="steamid" value="${esc(u.steamid)}" inputmode="numeric" style="min-width:190px"></td>
          <td><input class="input" data-u="${i}" data-f="name" value="${esc(u.name)}"></td>
          <td><input class="input" data-u="${i}" data-f="reason" value="${esc(u.reason)}"></td>
          <td class="actions"><button class="icon-btn" type="button" data-udel="${i}" title="Quitar"><svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg></button></td></tr>`).join('')}
        </tbody></table>` : '<div class="empty">No hay administradores</div>';
      $$('[data-u]', v).forEach((i) => i.oninput = i.onchange = () => { users[i.dataset.u][i.dataset.f] = i.value.trim(); });
      $$('[data-udel]', v).forEach((b) => b.onclick = () => { users.splice(Number(b.dataset.udel), 1); drawUsers(); });
    }
    drawUsers();
    $('[data-adduser]', v).onclick = () => { users.push({ role: 'moderatorid', steamid: '', name: '', reason: '' }); drawUsers(); };
    $('[data-saveusers]', v).onclick = (e) => busy(e.currentTarget, async () => {
      if (!users.some((u) => u.role === 'ownerid')) {
        if (!(await confirmar('Sin owners', 'No queda ningún owner: nadie tendrá permisos de administrador al reiniciar.', 'Guardar igual'))) return;
      }
      await api('/api/users', { method: 'PUT', body: { users, applyLive: $('[data-live]', v).checked } });
      toast('users.cfg guardado');
    }).catch(() => {});

    on('status', (st) => { if (!data && st.rcon) load(); });
    load();
  }

  // ───────────────────────────── VISTA: plugins ─────────────────────────────
  async function viewPlugins(v) {
    v.innerHTML = `
      <section class="card">
        <div class="card-h"><div><div class="kicker">server\\oxide\\plugins</div><h2>Plugins</h2></div><span class="spacer"></span><button class="btn btn-sm" data-refresh type="button">Actualizar</button></div>
        <div class="card-b flush table-wrap" data-list><div class="empty">cargando…</div></div>
      </section>
      <section class="card" data-editor hidden></section>`;

    async function load() {
      const box = $('[data-list]', v);
      try {
        const { plugins, rcon } = await api('/api/plugins');
        box.innerHTML = `${rcon ? '' : '<div class="callout" style="margin:14px 16px 0"><span class="ico">!</span><div>Servidor apagado: se muestran los ficheros, pero para cargar o recargar hace falta que esté encendido.</div></div>'}
          <table class="table"><thead><tr><th>Plugin</th><th>Versión</th><th>Autor</th><th>Estado</th><th>Uso</th><th></th></tr></thead><tbody>
          ${plugins.map((p) => `<tr>
            <td><b>${esc(p.title)}</b><div class="faint" style="font-size:11px">${esc(p.file)}</div></td>
            <td class="mono">${esc(p.version || '—')}</td>
            <td class="muted">${esc(p.author || '—')}</td>
            <td>${p.loaded ? '<span class="tag ok">cargado</span>' : rcon ? '<span class="tag warn">no cargado</span>' : '<span class="tag off">—</span>'}</td>
            <td class="muted mono" style="font-size:11px">${esc(p.stats || '')}</td>
            <td class="actions">
              ${p.config ? `<button class="btn btn-sm" type="button" data-cfg="${esc(p.config)}">Config</button>` : ''}
              ${p.loaded ? `<button class="btn btn-sm" type="button" data-act="reload" data-n="${esc(p.name)}" ${rcon ? '' : 'disabled'}>Recargar</button>
                            <button class="btn btn-sm btn-danger" type="button" data-act="unload" data-n="${esc(p.name)}" ${rcon ? '' : 'disabled'}>Descargar</button>`
                         : `<button class="btn btn-sm btn-ok" type="button" data-act="load" data-n="${esc(p.name)}" ${rcon ? '' : 'disabled'}>Cargar</button>`}
            </td></tr>`).join('')}</tbody></table>`;
        $$('[data-act]', box).forEach((b) => b.onclick = () => busy(b, async () => {
          const r = await api(`/api/plugins/${b.dataset.act}`, { method: 'POST', body: { name: b.dataset.n } });
          toast({ reload: 'Recargado', unload: 'Descargado', load: 'Cargado' }[b.dataset.act] + ' ' + b.dataset.n, 'ok', r.output || undefined);
          load();
        }).catch(() => {}));
        $$('[data-cfg]', box).forEach((b) => b.onclick = () => openEditor(b.dataset.cfg));
      } catch (e) { box.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
    }

    async function openEditor(name) {
      const ed = $('[data-editor]', v);
      ed.hidden = false;
      ed.innerHTML = '<div class="empty">cargando…</div>';
      const { raw } = await api(`/api/configs/${encodeURIComponent(name)}`);
      let data = JSON.parse(raw), text = raw, mode = 'form';
      ed.innerHTML = `
        <div class="card-h">
          <div><div class="kicker">oxide\\config</div><h2>${esc(name)}</h2></div>
          <span class="spacer"></span>
          <div class="seg" data-m><button class="on" type="button" data-x="form">Formulario</button><button type="button" data-x="raw">JSON</button></div>
          <button class="btn" type="button" data-s="0">Guardar</button>
          <button class="btn btn-primary" type="button" data-s="1">Guardar y recargar</button>
          <button class="btn btn-ghost" type="button" data-close>Cerrar</button>
        </div>
        <div class="card-b" data-body></div>`;
      const body = $('[data-body]', ed);
      const draw = () => {
        if (mode === 'raw') {
          body.innerHTML = `<textarea class="textarea code" spellcheck="false" data-t>${esc(text)}</textarea><div class="note" data-jerr style="margin-top:8px"></div>`;
          $('[data-t]', body).oninput = (e) => {
            text = e.target.value;
            try { JSON.parse(text); $('[data-jerr]', body).textContent = ''; e.target.classList.remove('is-dirty'); }
            catch (err) { $('[data-jerr]', body).textContent = 'JSON inválido: ' + err.message; e.target.classList.add('is-dirty'); }
          };
        } else {
          body.innerHTML = '';
          const root = document.createElement('div');
          root.className = 'jf';
          buildJsonForm(root, data);
          body.appendChild(root);
        }
      };
      $$('[data-m] button', ed).forEach((b) => b.onclick = () => {
        if (b.dataset.x === mode) return;
        if (b.dataset.x === 'raw') text = JSON.stringify(data, null, 2);
        else { try { data = JSON.parse(text); } catch (e) { return toast('Arregla el JSON antes de volver al formulario', 'err'); } }
        mode = b.dataset.x;
        $$('[data-m] button', ed).forEach((x) => x.classList.toggle('on', x === b));
        draw();
      });
      $$('[data-s]', ed).forEach((b) => b.onclick = () => busy(b, async () => {
        const payload = mode === 'raw' ? { raw: text } : { data };
        const r = await api(`/api/configs/${encodeURIComponent(name)}`, { method: 'PUT', body: { ...payload, reload: b.dataset.s === '1' } });
        toast(b.dataset.s === '1' ? `${name} guardado y plugin recargado` : `${name} guardado`, 'ok', r.output || undefined);
        if (b.dataset.s === '1') load();
      }).catch(() => {}));
      $('[data-close]', ed).onclick = () => { ed.hidden = true; };
      draw();
      ed.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    $('[data-refresh]', v).onclick = load;
    load();
  }

  // Formulario generico a partir de un JSON (modifica el objeto en sitio)
  function buildJsonForm(container, obj) {
    for (const key of Object.keys(obj)) {
      const val = obj[key];
      if (val && typeof val === 'object' && !Array.isArray(val)) {
        const g = document.createElement('div');
        g.className = 'jf-group';
        const t = document.createElement('div');
        t.className = 'jf-title';
        t.textContent = key;
        g.appendChild(t);
        buildJsonForm(g, val);
        container.appendChild(g);
        continue;
      }
      const row = document.createElement('div');
      row.className = 'jf-row';
      const k = document.createElement('div');
      k.className = 'k';
      k.textContent = key;
      row.appendChild(k);
      const cell = document.createElement('div');

      if (typeof val === 'boolean') {
        cell.innerHTML = switchHtml(val);
        cell.querySelector('input').onchange = (e) => { obj[key] = e.target.checked; };
      } else if (typeof val === 'number') {
        const i = document.createElement('input');
        i.className = 'input'; i.type = 'number'; i.step = 'any'; i.value = val;
        i.oninput = () => { const n = Number(i.value); if (i.value !== '' && !isNaN(n)) obj[key] = n; };
        cell.appendChild(i);
      } else if (Array.isArray(val) && val.every((x) => x === null || typeof x !== 'object')) {
        const nums = val.length > 0 && val.every((x) => typeof x === 'number');
        const ta = document.createElement('textarea');
        ta.className = 'textarea'; ta.style.minHeight = '90px';
        ta.value = val.join('\n');
        ta.placeholder = 'uno por línea';
        ta.oninput = () => {
          const lines = ta.value.split('\n').map((s) => s.trim()).filter(Boolean);
          obj[key] = nums ? lines.map(Number).filter((n) => !isNaN(n)) : lines;
        };
        cell.appendChild(ta);
      } else if (Array.isArray(val)) {
        const ta = document.createElement('textarea');
        ta.className = 'textarea'; ta.value = JSON.stringify(val, null, 2);
        ta.oninput = () => { try { obj[key] = JSON.parse(ta.value); ta.classList.remove('is-dirty'); } catch { ta.classList.add('is-dirty'); } };
        cell.appendChild(ta);
      } else {
        const i = document.createElement('input');
        i.className = 'input'; i.value = val ?? '';
        i.oninput = () => { obj[key] = i.value; };
        cell.appendChild(i);
      }
      row.appendChild(cell);
      container.appendChild(row);
    }
  }

  // ───────────────────────────── VISTA: jugadores ─────────────────────────────
  function viewJugadores(v) {
    v.innerHTML = `
      <section class="card">
        <div class="card-h"><div><div class="kicker">se actualiza cada 5 s</div><h2>Jugadores conectados</h2></div></div>
        <div class="card-b flush table-wrap" data-list><div class="empty">cargando…</div></div>
      </section>
      <section class="card" data-give hidden>
        <div class="card-h"><div><div class="kicker">inventory.giveto</div><h2 data-givename>Dar item</h2></div></div>
        <form class="card-b row" data-giveform>
          <input class="input" name="item" placeholder="shortname (ej. rifle.ak)" style="max-width:260px" aria-label="Shortname">
          <input class="input" name="amount" type="number" min="1" value="1" style="max-width:120px" aria-label="Cantidad">
          <button class="btn btn-primary" type="submit">Dar</button>
          <button class="btn btn-ghost" type="button" data-cancel>Cancelar</button>
        </form>
      </section>`;

    let giveTo = null, lastJson = '';
    async function load() {
      const box = $('[data-list]', v);
      if (!(S.status && S.status.rcon)) { box.innerHTML = '<div class="empty"><span class="big">sin rcon</span>El servidor está apagado.</div>'; lastJson = ''; return; }
      try {
        const { players } = await api('/api/players');
        const json = JSON.stringify(players);
        if (json === lastJson) return;   // sin cambios: no se repinta (no se pierde el foco)
        lastJson = json;
        box.innerHTML = players.length ? `<table class="table"><thead><tr><th>Jugador</th><th>SteamID64</th><th class="num">Ping</th><th class="num">Conectado</th><th class="num">Vida</th><th></th></tr></thead><tbody>
          ${players.map((p) => `<tr>
            <td><b>${esc(p.DisplayName)}</b></td>
            <td class="mono">${esc(p.SteamID)}</td>
            <td class="num">${esc(p.Ping)} ms</td>
            <td class="num">${fmtUptime(p.ConnectedSeconds)}</td>
            <td class="num">${Math.round(p.Health || 0)}</td>
            <td class="actions">
              <button class="btn btn-sm" type="button" data-a="give" data-id="${esc(p.SteamID)}" data-name="${esc(p.DisplayName)}">Dar item</button>
              <button class="btn btn-sm" type="button" data-a="kick" data-id="${esc(p.SteamID)}" data-name="${esc(p.DisplayName)}">Expulsar</button>
              <button class="btn btn-sm btn-danger" type="button" data-a="ban" data-id="${esc(p.SteamID)}" data-name="${esc(p.DisplayName)}">Banear</button>
            </td></tr>`).join('')}</tbody></table>`
          : '<div class="empty"><span class="big">vacío</span>No hay nadie conectado. En Rust: F1 → client.connect localhost:28015</div>';
        $$('[data-a]', box).forEach((b) => b.onclick = async () => {
          const { a, id, name } = b.dataset;
          if (a === 'give') { giveTo = { id, name }; $('[data-give]', v).hidden = false; $('[data-givename]', v).textContent = `Dar item a ${name}`; $('[data-giveform] [name=item]', v).focus(); return; }
          if (!(await confirmar(a === 'kick' ? `Expulsar a ${name}` : `Banear a ${name}`, a === 'kick' ? 'Se le desconecta del servidor; puede volver a entrar.' : 'No podrá volver a entrar hasta que se le quite el ban (unban).', a === 'kick' ? 'Expulsar' : 'Banear'))) return;
          try { await api('/api/players/action', { method: 'POST', body: { action: a, steamid: id, name } }); toast(a === 'kick' ? `${name} expulsado` : `${name} baneado`); lastJson = ''; load(); }
          catch (e) { toast(e.message, 'err'); }
        });
      } catch (e) { box.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
    }

    $('[data-giveform]', v).onsubmit = async (e) => {
      e.preventDefault();
      if (!giveTo) return;
      try {
        await api('/api/players/action', { method: 'POST', body: { action: 'give', steamid: giveTo.id, item: e.target.item.value.trim(), amount: e.target.amount.value } });
        toast(`${e.target.item.value} x${e.target.amount.value} para ${giveTo.name}`);
      } catch (err) { toast(err.message, 'err'); }
    };
    $('[data-cancel]', v).onclick = () => { $('[data-give]', v).hidden = true; giveTo = null; };

    load();
    every(5000, load);
  }

  // ───────────────────────────── VISTA: consola ─────────────────────────────
  function viewConsola(v) {
    v.innerHTML = '<section data-c></section>';
    mountConsole($('[data-c]', v));
  }

  // ───────────────────────────── router ─────────────────────────────
  const VIEWS = {
    panel: ['Panel', 'resumen en directo', viewPanel],
    mapa: ['Mapa', 'seed · tamaño · vista previa', viewMapa],
    servidor: ['Servidor', 'lanzamiento · server.cfg', viewServidor],
    permisos: ['Permisos', 'grupos · jugadores · admins', viewPermisos],
    plugins: ['Plugins', 'oxide · configuraciones', viewPlugins],
    jugadores: ['Jugadores', 'conectados ahora', viewJugadores],
    consola: ['Consola', 'rcon en directo', viewConsola],
  };

  async function route() {
    const name = VIEWS[location.hash.slice(1)] ? location.hash.slice(1) : 'panel';
    S.cleanup.splice(0).forEach((f) => { try { f(); } catch { /* nada */ } });
    LineChart.hideAll();
    const [title, kicker, fn] = VIEWS[name];
    $('#viewTitle').textContent = title;
    $('#viewKicker').textContent = kicker;
    document.title = `${title} · RGL Control`;
    $$('#nav a').forEach((a) => a.classList.toggle('active', a.dataset.view === name));
    const v = $('#view');
    v.innerHTML = '';
    try { await fn(v); }
    catch (e) { v.innerHTML = `<div class="callout err"><span class="ico">!</span><div>${esc(e.message)}</div></div>`; }
  }

  window.addEventListener('hashchange', route);
  connectStream();
  route();
})();

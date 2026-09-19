/* ═══════════════════════════════════════════════════════════════════════════
   Graficas en SVG sin librerias.
   - Un solo eje Y por grafica (nunca doble eje): cada metrica va en la suya.
   - Linea de 2px, relleno al 10%, punto final de 8px con anillo del color de fondo.
   - Cruceta sincronizada entre todas las graficas; el tooltip sale en la que tocas.
   - El texto nunca lleva el color de la serie: la identidad la da la marca.
   ═══════════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';
  const NS = 'http://www.w3.org/2000/svg';
  const charts = new Set();
  const tip = () => document.getElementById('chartTip');

  function svgEl(tag, attrs, parent) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }

  // 0 · 1 · 2 · 2.5 · 5 · 10 × 10^n
  function niceMax(v) {
    if (!(v > 0)) return 1;
    const exp = Math.pow(10, Math.floor(Math.log10(v)));
    const f = v / exp;
    const nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
    return nf * exp;
  }

  function compact(n) {
    if (n == null || isNaN(n)) return '—';
    const a = Math.abs(n);
    if (a >= 1e6) return (n / 1e6).toFixed(1).replace(/\.0$/, '') + 'M';
    if (a >= 1e4) return (n / 1e3).toFixed(1).replace(/\.0$/, '') + 'K';
    if (a >= 1000) return Math.round(n).toLocaleString('es-ES');
    if (a > 0 && a < 10 && n % 1 !== 0) return n.toFixed(1);
    return String(Math.round(n));
  }

  function hhmmss(t) {
    const d = new Date(t);
    return d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  function rangoTexto(ms) {
    const m = ms / 60000;
    if (m >= 60) return `-${Math.round(m / 60)} h`;
    return `-${Number.isInteger(m) ? m : m.toFixed(1).replace('.', ',')} min`;
  }

  const GAP_MS = 10000;   // mas de 10 s sin muestra = servidor caido: se corta la linea

  class LineChart {
    constructor(host, opts) {
      this.host = host;
      this.opts = Object.assign({ format: compact, unit: '' }, opts);
      this.samples = [];
      this.range = 15 * 60000;
      host.classList.add('chart');
      host.setAttribute('role', 'img');
      host.setAttribute('aria-label', opts.title || 'grafica');
      this.svg = svgEl('svg', { preserveAspectRatio: 'none' }, host);
      this.emptyEl = document.createElement('div');
      this.emptyEl.className = 'chart-empty';
      this.emptyEl.textContent = 'sin señal';
      host.appendChild(this.emptyEl);

      this.ro = new ResizeObserver(() => this.draw());
      this.ro.observe(host);

      host.addEventListener('pointermove', (e) => this.onMove(e));
      host.addEventListener('pointerleave', () => LineChart.hideAll());
      charts.add(this);
    }

    destroy() { this.ro.disconnect(); charts.delete(this); }

    setData(samples, range) {
      this.samples = samples;
      if (range) this.range = range;
      this.draw();
    }

    draw() {
      const w = this.host.clientWidth, h = this.host.clientHeight;
      if (!w || !h) return;
      const pad = { l: 44, r: 62, t: 12, b: 24 };
      this.pad = pad; this.w = w; this.h = h;
      const svg = this.svg;
      svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
      svg.textContent = '';

      const now = Date.now();
      const t0 = now - this.range;
      this.t0 = t0; this.t1 = now;
      const data = this.samples.filter((s) => s.t >= t0 - 3000);
      this.data = data;
      const series = this.opts.series;

      this.emptyEl.hidden = data.length >= 2;
      if (data.length < 2) return;

      let max = 0;
      for (const s of data) for (const se of series) max = Math.max(max, s[se.key] || 0);
      const yMax = niceMax(max * 1.08 || 1);
      this.yMax = yMax;

      const iw = w - pad.l - pad.r, ih = h - pad.t - pad.b;
      const X = (t) => pad.l + ((t - t0) / (now - t0)) * iw;
      const Y = (v) => pad.t + ih - (v / yMax) * ih;
      this.X = X; this.Y = Y;

      // rejilla + ticks del eje Y (0, mitad, maximo)
      for (const v of [0, yMax / 2, yMax]) {
        const y = Math.round(Y(v)) + .5;
        svgEl('line', { x1: pad.l, x2: w - pad.r + 8, y1: y, y2: y, class: 'grid-line' }, svg);
        const tx = svgEl('text', { x: pad.l - 8, y: y + 3.5, 'text-anchor': 'end', class: 'axis-text' }, svg);
        tx.textContent = this.opts.format(v);
      }
      // eje X: inicio, mitad, ahora
      [[pad.l, rangoTexto(this.range), 'start'], [pad.l + iw / 2, rangoTexto(this.range / 2), 'middle'], [pad.l + iw, 'ahora', 'end']]
        .forEach(([x, t, a]) => { const tx = svgEl('text', { x, y: h - 6, 'text-anchor': a, class: 'axis-text' }, svg); tx.textContent = t; });

      // series
      const single = series.length === 1;
      series.forEach((se) => {
        let line = '', area = '', open = false, first = null, prev = null;
        const cerrarArea = () => { if (open && first) area += `L${X(prev.t).toFixed(1)},${Y(0).toFixed(1)}L${X(first.t).toFixed(1)},${Y(0).toFixed(1)}Z`; };
        for (const s of data) {
          const x = X(Math.max(s.t, t0)).toFixed(1), y = Y(s[se.key] || 0).toFixed(1);
          if (!prev || s.t - prev.t > GAP_MS) {
            cerrarArea();
            line += `M${x},${y}`; area += `M${x},${y}`; first = s; open = true;
          } else { line += `L${x},${y}`; area += `L${x},${y}`; }
          prev = s;
        }
        cerrarArea();
        if (single) svgEl('path', { d: area, class: 'series-area', style: `fill:${se.color}` }, svg);
        svgEl('path', { d: line, class: 'series-line', style: `stroke:${se.color}` }, svg);
      });

      // punto y valor final (solo el ultimo punto: etiquetado selectivo)
      const last = data[data.length - 1];
      const ends = series.map((se) => ({ se, y: Y(last[se.key] || 0) }));
      if (ends.length === 2 && Math.abs(ends[0].y - ends[1].y) < 13) {
        // si chocan, no se apilan: la leyenda y el tooltip ya llevan el valor
        ends.forEach(({ se, y }) => svgEl('circle', { cx: X(last.t), cy: y, r: 4, class: 'end-dot', style: `fill:${se.color}` }, svg));
      } else {
        ends.forEach(({ se, y }) => {
          svgEl('circle', { cx: X(last.t), cy: y, r: 4, class: 'end-dot', style: `fill:${se.color}` }, svg);
          const tx = svgEl('text', { x: X(last.t) + 9, y: y + 4, class: 'end-label' }, svg);
          tx.textContent = this.opts.format(last[se.key] || 0);
        });
      }

      // capa de cruceta
      this.crossG = svgEl('g', { visibility: 'hidden' }, svg);
      this.crossLine = svgEl('line', { y1: pad.t, y2: pad.t + ih, class: 'cross' }, this.crossG);
      this.hoverDots = series.map((se) => svgEl('circle', { r: 4, class: 'hover-dot', style: `fill:${se.color}` }, this.crossG));
    }

    nearest(t) {
      const d = this.data;
      if (!d || !d.length) return null;
      let lo = 0, hi = d.length - 1;
      while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (d[mid].t < t) lo = mid; else hi = mid; }
      return Math.abs(d[lo].t - t) <= Math.abs(d[hi].t - t) ? d[lo] : d[hi];
    }

    showCross(t) {
      if (!this.crossG || !this.data || this.data.length < 2) return null;
      const s = this.nearest(t);
      if (!s) return null;
      const x = Math.round(this.X(s.t)) + .5;
      this.crossLine.setAttribute('x1', x);
      this.crossLine.setAttribute('x2', x);
      this.opts.series.forEach((se, i) => {
        this.hoverDots[i].setAttribute('cx', this.X(s.t));
        this.hoverDots[i].setAttribute('cy', this.Y(s[se.key] || 0));
      });
      this.crossG.setAttribute('visibility', 'visible');
      return s;
    }

    hideCross() { if (this.crossG) this.crossG.setAttribute('visibility', 'hidden'); }

    onMove(e) {
      if (!this.data || this.data.length < 2) return;
      const r = this.host.getBoundingClientRect();
      const px = e.clientX - r.left;
      const t = this.t0 + ((px - this.pad.l) / (this.w - this.pad.l - this.pad.r)) * (this.t1 - this.t0);
      for (const c of charts) if (c !== this && c.host.isConnected) c.showCross(t);
      const s = this.showCross(t);
      if (!s) return;

      const el = tip();
      el.textContent = '';
      const head = document.createElement('div');
      head.className = 't';
      head.textContent = hhmmss(s.t);
      el.appendChild(head);
      for (const se of this.opts.series) {
        const row = document.createElement('div');
        row.className = 'r';
        const key = document.createElement('i');
        key.style.background = se.color;
        const val = document.createElement('b');
        val.textContent = this.opts.format(s[se.key] || 0) + (this.opts.unit ? ' ' + this.opts.unit : '');
        const lab = document.createElement('span');
        lab.textContent = se.label;
        row.append(key, val, lab);
        el.appendChild(row);
      }
      el.hidden = false;
      const tw = el.offsetWidth, th = el.offsetHeight;
      let left = e.clientX + 16, top = e.clientY - th - 12;
      if (left + tw > window.innerWidth - 8) left = e.clientX - tw - 16;
      if (top < 8) top = e.clientY + 16;
      el.style.left = left + 'px';
      el.style.top = top + 'px';
    }

    static hideAll() {
      for (const c of charts) c.hideCross();
      const el = tip();
      if (el) el.hidden = true;
    }
  }

  // Sparkline de las tarjetas: linea en tono apagado, ultimo punto en el acento
  function sparkline(host, values) {
    host.textContent = '';
    const w = host.clientWidth || 160, h = host.clientHeight || 34;
    const svg = svgEl('svg', { viewBox: `0 0 ${w} ${h}`, preserveAspectRatio: 'none', width: '100%', height: '100%' }, host);
    svg.style.overflow = 'visible';
    if (!values || values.length < 2) return;
    const max = Math.max(...values, 1), min = Math.min(...values, 0);
    const X = (i) => (i / (values.length - 1)) * (w - 6) + 1;
    const Y = (v) => h - 3 - ((v - min) / (max - min || 1)) * (h - 8);
    let d = '';
    values.forEach((v, i) => { d += (i ? 'L' : 'M') + X(i).toFixed(1) + ',' + Y(v).toFixed(1); });
    svgEl('path', { d, fill: 'none', style: 'stroke:var(--faint)', 'stroke-width': 2, 'stroke-linejoin': 'round', 'stroke-linecap': 'round' }, svg);
    svgEl('circle', { cx: X(values.length - 1), cy: Y(values[values.length - 1]), r: 3.5, style: 'fill:var(--accent);stroke:var(--panel)', 'stroke-width': 2 }, svg);
  }

  window.Charts = { LineChart, sparkline, compact, hhmmss };
})();

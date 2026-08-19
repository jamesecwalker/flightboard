/* FlightBoard split-flap simulation.
   Receives BoardFrames over SSE (/api/stream) and animates each tile through the charset
   to its target character, with a synthesised click per flap. No dependencies. */
(() => {
  'use strict';

  const STEP_MS = 62;          // time per flap step
  const STAGGER_MS = 140;      // random start delay per tile, makes the wave look mechanical
  const MAX_STEPS = 60;        // safety cap

  const boardEl = document.getElementById('board');
  const statusEl = document.getElementById('status');
  const connEl = document.getElementById('conn');
  const unlockEl = document.getElementById('unlock');
  const unlockBtn = document.getElementById('unlockBtn');

  let caps = { rows: 4, cols: 22, charset: ' ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.,:*/\'&?!+()', colour: true };
  let tiles = [];              // tiles[r][c]
  let pendingFrame = null;

  /* ---------------- audio ---------------- */
  const audio = {
    ctx: null, enabled: localStorage.getItem('fb.sound') !== 'off', lastAt: 0, inWindow: 0, noise: null,
    unlock() {
      if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      this.ctx = new AC();
      const len = Math.floor(this.ctx.sampleRate * 0.05);
      const buf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
      const d = buf.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, 3);
      this.noise = buf;
    },
    click(accent) {
      if (!this.enabled || !this.ctx || this.ctx.state !== 'running') return;
      const now = this.ctx.currentTime;
      // Throttle: at most ~8 clicks per 20 ms window; past that the ear can't tell anyway.
      if (now - this.lastAt < 0.02) { if (++this.inWindow > 8) return; } else { this.inWindow = 0; this.lastAt = now; }
      const t = now + Math.random() * 0.004;
      const src = this.ctx.createBufferSource();
      src.buffer = this.noise;
      const bp = this.ctx.createBiquadFilter();
      bp.type = 'bandpass';
      bp.frequency.value = (accent ? 2600 : 1900) + Math.random() * 700;
      bp.Q.value = 1.2;
      const g = this.ctx.createGain();
      g.gain.setValueAtTime(0.0001, t);
      g.gain.exponentialRampToValueAtTime(0.18, t + 0.002);
      g.gain.exponentialRampToValueAtTime(0.0001, t + 0.045);
      // a tiny "tock" body underneath the noise
      const osc = this.ctx.createOscillator();
      osc.type = 'square';
      osc.frequency.setValueAtTime(420 + Math.random() * 120, t);
      osc.frequency.exponentialRampToValueAtTime(120, t + 0.03);
      const og = this.ctx.createGain();
      og.gain.setValueAtTime(0.05, t);
      og.gain.exponentialRampToValueAtTime(0.0001, t + 0.03);
      src.connect(bp).connect(g).connect(this.ctx.destination);
      osc.connect(og).connect(this.ctx.destination);
      src.start(t); osc.start(t); osc.stop(t + 0.035);
    },
    toggle() {
      this.enabled = !this.enabled;
      localStorage.setItem('fb.sound', this.enabled ? 'on' : 'off');
      if (this.enabled) this.unlock();
      setStatus();
    },
  };

  /* ---------------- tiles ---------------- */
  class Tile {
    constructor(el) {
      this.el = el;
      this.top = el.querySelector('.top:not(.flap) span');
      this.bottom = el.querySelector('.bottom:not(.flap) span');
      this.flapTop = el.querySelector('.flap.top');
      this.flapBottom = el.querySelector('.flap.bottom');
      this.flapTopSpan = this.flapTop.querySelector('span');
      this.flapBottomSpan = this.flapBottom.querySelector('span');
      this.current = ' ';
      this.target = ' ';
      this.accent = false;
      this.queue = [];          // characters still to flip through
      this.running = false;
      this.seq = 0;             // bumps on every flip; stale animation callbacks check it and bail
    }

    setTarget(ch, accent, forceFullCycle) {
      ch = caps.charset.includes(ch) ? ch : ' ';
      this.accent = !!accent;
      this.target = ch;
      const path = this.pathTo(ch, forceFullCycle);
      if (path.length === 0) { this.settle(); return; }
      this.queue = path;
      if (!this.running) {
        this.running = true;
        setTimeout(() => this.tick(), Math.random() * STAGGER_MS);
      }
    }

    pathTo(ch, forceFullCycle) {
      const cs = caps.charset;
      const from = cs.indexOf(this.current);
      const to = cs.indexOf(ch);
      if (from === to && !forceFullCycle) return [];
      const path = [];
      let i = from;
      const steps = forceFullCycle && from === to ? cs.length : ((to - from + cs.length) % cs.length);
      for (let n = 0; n < Math.min(steps, MAX_STEPS); n++) { i = (i + 1) % cs.length; path.push(cs[i]); }
      if (path[path.length - 1] !== ch) path.push(ch);   // capped: jump straight to the target
      return path;
    }

    tick() {
      const next = this.queue.shift();
      if (next === undefined) { this.running = false; this.settle(); return; }
      const prev = this.current;
      this.current = next;
      if (document.visibilityState === 'hidden') {
        // Background tab: timers are throttled and animations stall, so just set the tiles directly.
        this.settle();
      } else {
        this.flip(prev, next);
      }
      if (this.queue.length === 0) this.el.classList.toggle('accent', this.accent);
      setTimeout(() => this.tick(), STEP_MS);
    }

    /* Both static halves show the current character and no flap is mid-air. The single source of truth. */
    settle() {
      this.seq++;
      this.cancelFlaps();
      this.top.textContent = this.current;
      this.bottom.textContent = this.current;
      this.el.classList.toggle('accent', this.accent);
    }

    cancelFlaps() {
      for (const el of [this.flapTop, this.flapBottom]) {
        for (const a of el.getAnimations()) a.cancel();
        el.style.display = 'none';
      }
    }

    flip(oldCh, newCh) {
      const seq = ++this.seq;
      this.cancelFlaps();
      audio.click(this.accent);
      // Static halves: top already shows the new char (revealed as the old flap falls);
      // bottom keeps the old char until the new bottom flap has landed.
      this.top.textContent = newCh;
      this.bottom.textContent = oldCh;
      this.flapTopSpan.textContent = oldCh;
      this.flapBottomSpan.textContent = newCh;
      this.flapTop.style.display = 'block';
      const half = STEP_MS * 0.45;
      const a1 = this.flapTop.animate(
        [{ transform: 'rotateX(0deg)' }, { transform: 'rotateX(-90deg)' }],
        { duration: half, easing: 'ease-in', fill: 'forwards' });
      a1.onfinish = () => {
        if (seq !== this.seq) return;                 // a newer flip or settle has taken over
        this.flapTop.style.display = 'none';
        this.flapBottom.style.display = 'block';
        const a2 = this.flapBottom.animate(
          [{ transform: 'rotateX(90deg)' }, { transform: 'rotateX(0deg)' }],
          { duration: half, easing: 'ease-out', fill: 'forwards' });
        a2.onfinish = () => {
          if (seq !== this.seq) return;
          this.bottom.textContent = newCh;
          this.flapBottom.style.display = 'none';
        };
      };
    }
  }

  function build(c) {
    caps = c;
    document.documentElement.style.setProperty('--cols', c.cols);
    document.documentElement.style.setProperty('--rows', c.rows);
    boardEl.innerHTML = '';
    tiles = [];
    for (let r = 0; r < c.rows; r++) {
      const row = document.createElement('div');
      row.className = 'row';
      const rowTiles = [];
      for (let col = 0; col < c.cols; col++) {
        const t = document.createElement('div');
        t.className = 'tile';
        t.innerHTML = '<div class="half top"><span> </span></div>' +
                      '<div class="half bottom"><span> </span></div>' +
                      '<div class="half top flap"><span> </span></div>' +
                      '<div class="half bottom flap"><span> </span></div>';
        row.appendChild(t);
        rowTiles.push(new Tile(t));
      }
      boardEl.appendChild(row);
      tiles.push(rowTiles);
    }
    fit();
    if (pendingFrame) { show(pendingFrame); pendingFrame = null; }
  }

  function show(frame) {
    if (!tiles.length) { pendingFrame = frame; return; }
    const attract = frame.kind === 'attract';
    for (let r = 0; r < caps.rows; r++) {
      const text = (frame.rows && frame.rows[r]) || '';
      for (let c = 0; c < caps.cols; c++) {
        const ch = (text[c] || ' ').toUpperCase();
        const accent = caps.colour && frame.accent && frame.accent[r] && frame.accent[r][c];
        tiles[r][c].setTarget(ch, accent, attract);
      }
    }
  }

  /* size the tiles so the board fills the width (or height) available */
  function fit() {
    const vw = window.innerWidth, vh = window.innerHeight;
    const byWidth = (vw * 0.94) / (caps.cols + (caps.cols - 1) * 0.09 + 1.0);
    const byHeight = (vh * 0.80) / ((caps.rows * 1.45) + (caps.rows - 1) * 0.22 + 0.9) ;
    const tw = Math.max(12, Math.min(byWidth, byHeight, 96));
    document.documentElement.style.setProperty('--tw', tw.toFixed(2) + 'px');
  }
  window.addEventListener('resize', fit);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') tiles.flat().forEach(t => { if (!t.running) t.settle(); });
  });

  /* ---------------- SSE ---------------- */
  let es = null;
  function connect() {
    es = new EventSource('/api/stream');
    es.addEventListener('caps', e => { build(JSON.parse(e.data)); setConn(true); });
    es.addEventListener('frame', e => { show(JSON.parse(e.data)); replay.onLiveFrame(); });
    es.onopen = () => setConn(true);
    es.onerror = () => setConn(false);      // EventSource reconnects by itself
  }
  function setConn(on) { connEl.className = 'dot ' + (on ? 'on' : 'off'); setStatus(); }

  /* ---------------- status line ---------------- */
  let lastState = null;
  async function pollState() {
    try {
      const r = await fetch('/api/state', { cache: 'no-store' });
      if (r.ok) lastState = await r.json();
    } catch { /* ignore */ }
    setStatus();
  }
  function setStatus() {
    const bits = [];
    if (lastState) {
      bits.push(`${lastState.sourceName}`);
      bits.push(`${lastState.trackedCount} tracked`);
      const inbound = (lastState.flights || []).filter(f => f.phase === 'Approaching' || f.phase === 'Overhead').length;
      if (inbound) bits.push(`${inbound} inbound`);
      if (lastState.quiet) bits.push('quiet hours');
    }
    bits.push(audio.enabled ? (audio.ctx && audio.ctx.state === 'running' ? 'sound on' : 'sound: tap') : 'sound off');
    statusEl.textContent = bits.join(' · ');
  }
  setInterval(pollState, 5000);
  pollState();

  /* ---------------- history replay ---------------- */
  const replay = {
    items: [], index: -1, armedAt: 0,
    async load() {
      try { const r = await fetch('/api/history?limit=200', { cache: 'no-store' }); if (r.ok) this.items = await r.json(); } catch { /* ignore */ }
    },
    async step(delta) {
      if (!this.items.length) await this.load();
      if (!this.items.length) return;
      // items[0] is the most recent; "back" means a higher index.
      const next = this.index < 0 ? (delta > 0 ? 0 : 0) : this.index + delta;
      if (next < 0) { this.live(); return; }
      if (next >= this.items.length) return;
      this.index = next;
      this.armedAt = Date.now();
      const s = this.items[next];
      await fetch(`/api/history/${s.id}/replay`, { method: 'POST' });
      this.render();
    },
    live() {
      this.index = -1;
      this.render();
      if (this.items.length) { this.armedAt = Date.now(); fetch(`/api/history/${this.items[0].id}/replay`, { method: 'POST' }); }
    },
    onLiveFrame() {
      // A frame that we did not ask for means a real flight arrived: drop out of replay mode.
      if (this.index >= 0 && Date.now() - this.armedAt > 3000) { this.index = -1; this.items = []; this.render(); }
    },
    render() {
      const label = document.getElementById('replayLabel');
      if (this.index < 0) { label.textContent = ''; return; }
      const s = this.items[this.index];
      const t = new Date(s.seenAt);
      label.textContent = `replay ${this.index + 1}/${this.items.length} · ${t.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    },
  };
  document.getElementById('prevBtn').addEventListener('click', () => replay.step(+1));
  document.getElementById('nextBtn').addEventListener('click', () => replay.step(-1));
  document.getElementById('liveBtn').addEventListener('click', () => replay.live());

  /* ---------------- keys & gestures ---------------- */
  function toggleFullscreen() {
    if (!document.fullscreenElement) document.documentElement.requestFullscreen?.();
    else document.exitFullscreen?.();
  }
  document.addEventListener('keydown', e => {
    if (e.key === 'f' || e.key === 'F') toggleFullscreen();
    if (e.key === 'h' || e.key === 'H') document.body.classList.toggle('hide-chrome');
    if (e.key === 's' || e.key === 'S') audio.toggle();
    if (e.key === 'ArrowLeft') { e.preventDefault(); replay.step(+1); }
    if (e.key === 'ArrowRight') { e.preventDefault(); replay.step(-1); }
    if (e.key === 'Escape') replay.live();
    if (e.key === 't' || e.key === 'T') fetch('/api/simulate', { method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ flight: 'EZY 8123', airline: 'easyJet', origin: 'Alicante', type: 'A320', tag: e.shiftKey ? 'A380' : null, attract: e.altKey }) });
  });
  boardEl.addEventListener('dblclick', toggleFullscreen);

  // Browsers will not play audio until the user has interacted with the page.
  function armUnlock() {
    if (!audio.enabled) return;
    unlockEl.hidden = false;
    const go = () => { audio.unlock(); unlockEl.hidden = true; setStatus(); document.removeEventListener('pointerdown', go); document.removeEventListener('keydown', go); };
    unlockBtn.addEventListener('click', go, { once: true });
    document.addEventListener('pointerdown', go);
    document.addEventListener('keydown', go);
  }

  armUnlock();
  fit();
  connect();
})();

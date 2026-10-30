/* Cursor effects on one canvas:
   1. "Flashlight": a static background layout (waveguide routes from Lunima's own example
      designs, see circuit.json) is faint by default and revealed around the cursor.
   2. A connected photonic circuit is drawn along the mouse path and fades out.
   Off for reduced motion and touch-only devices. Needs circuit-parts.js. */
(function () {
  "use strict";
  var mq = window.matchMedia;
  if (!mq || mq("(prefers-reduced-motion: reduce)").matches || mq("(hover: none)").matches) return;
  var canvas = document.getElementById("wg-fx");
  var ctx = canvas && canvas.getContext("2d");
  var parts = window.LunimaParts;
  if (!ctx || !parts) return;

  var MAX_PARTS = 60;
  var LIFE_MS = 2200;
  var FADE_IN = 0.1;
  var SPAWN_DISTANCE = 80;
  var RESTART_DISTANCE = 420;
  var MAX_HEADS = 3;
  var GRID = 12;
  var TORCH_RADIUS = 230;
  var LENS_ZOOM = 1.18;           // 1 switches the magnifying lens off
  var BG_BASE_ALPHA = 0.045;      // background visibility without the torch
  var BG_LINE_ALPHA = 0.6;
  var BG_TILE_GAP = 56;
  var COLOR = "122, 163, 255";
  var CIRCUIT_URL = "site/circuit.json";

  var dpr = 1, width = 0, height = 0, rafId = 0, resizeTimer = 0;
  var bg = null, bgScale = 1, tiles = null;
  var scratch = document.createElement("canvas");
  var sctx = scratch.getContext("2d");
  var mask = null;
  var mouse = { x: 0, y: 0, tx: 0, ty: 0, active: false, vx: 1, vy: 0 };
  var lastSpawn = null;
  var circuit = [];   // placed parts: { lines (world coords), born }
  var heads = [];     // open ports: { x, y, dir, born }

  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }
  function snap(v) { return Math.round(v / GRID) * GRID; }

  /* ---------- connected circuit ---------- */

  var COS = [1, 0, -1, 0], SIN = [0, 1, 0, -1];

  function place(head, part, s, now) {
    var d = head.dir, c = COS[d], sn = SIN[d];
    function tx(x, y) { return head.x + c * x - sn * s * y; }
    function ty(x, y) { return head.y + sn * x + c * s * y; }
    var lines = part.lines.map(function (l) {
      var o = [];
      for (var i = 0; i < l.length; i += 2) o.push(tx(l[i], l[i + 1]), ty(l[i], l[i + 1]));
      return o;
    });
    circuit.push({ lines: lines, born: now });
    return part.outs.map(function (p) {
      return { x: tx(p.x, p.y), y: ty(p.x, p.y), dir: (d + s * p.a + 4) % 4, born: now };
    });
  }

  function nearestHead(x, y) {
    var best = -1, bd = Infinity;
    heads.forEach(function (h, i) {
      var d = (h.x - x) * (h.x - x) + (h.y - y) * (h.y - y);
      if (d < bd) { bd = d; best = i; }
    });
    return best;
  }

  function startCircuit(x, y, now) {
    var dir = Math.abs(mouse.vx) > Math.abs(mouse.vy) ? (mouse.vx > 0 ? 0 : 2) : (mouse.vy > 0 ? 1 : 3);
    var start = { x: snap(x), y: snap(y), dir: dir, born: now };
    heads = place(start, parts.gratingIn(), 1, now);
  }

  function chooseStep(head, x, y) {
    var target = Math.round(Math.atan2(y - head.y, x - head.x) / (Math.PI / 2));
    var rel = ((target - head.dir) % 4 + 4) % 4;
    if (rel === 1) return { part: parts.bend(1), s: 1 };
    if (rel === 3) return { part: parts.bend(-1), s: -1 };
    if (rel === 2) { var k = pick([-1, 1]); return { part: parts.bend(k), s: k }; }
    var kind = pick(["straight", "sbend", "mmi1x2", "mmi1x2", "mzi", "ring", "coupler", "mmi2x2"]);
    var side = pick([-1, 1]);
    return { part: parts[kind](side), s: side };
  }

  function capLooseEnds(now) {
    while (heads.length > MAX_HEADS) {
      var far = heads.shift();
      place(far, parts.grating(), 1, now);
    }
  }

  function extend(x, y, now) {
    heads = heads.filter(function (h) { return now - h.born < LIFE_MS; });
    var i = nearestHead(x, y);
    var h = i >= 0 ? heads[i] : null;
    var gap = h ? Math.hypot(h.x - x, h.y - y) : Infinity;
    if (!h || gap > RESTART_DISTANCE) { startCircuit(x, y, now); return; }
    heads.splice(i, 1);
    var step = chooseStep(h, x, y);
    heads = heads.concat(place(h, step.part, step.s, now));
    capLooseEnds(now);
    if (circuit.length > MAX_PARTS) circuit.splice(0, circuit.length - MAX_PARTS);
  }

  /* ---------- flashlight background ---------- */

  function rng(seed) {
    return function () {
      seed |= 0; seed = seed + 0x6D2B79F5 | 0;
      var t = Math.imul(seed ^ seed >>> 15, 1 | seed);
      t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
      return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
  }

  function drawTile(g, tile, ox, oy, k, flip) {
    function X(x) { return ox + (flip ? tile.w - x : x) * k; }
    function Y(y) { return oy + y * k; }
    g.beginPath();
    tile.paths.forEach(function (p) {
      g.moveTo(X(p[0]), Y(p[1]));
      for (var i = 2; i < p.length; i += 2) g.lineTo(X(p[i]), Y(p[i + 1]));
    });
    tile.parts.forEach(function (q) {
      var w = 22 * k, h = 11 * k;
      g.rect(X(q[0]) - w / 2, Y(q[1]) - h / 2, w, h);
    });
    g.stroke();
  }

  function buildBackground() {
    if (!tiles || !tiles.length) { bg = null; return; }
    bgScale = Math.min(dpr, 2) * LENS_ZOOM;
    bg = document.createElement("canvas");
    bg.width = Math.round(width * bgScale); bg.height = Math.round(height * bgScale);
    var g = bg.getContext("2d");
    g.scale(bgScale, bgScale);
    g.strokeStyle = "rgba(" + COLOR + ", " + BG_LINE_ALPHA + ")";
    g.lineWidth = 0.9; g.lineJoin = "round";
    var rand = rng(7), order = tiles.slice().sort(function () { return rand() - 0.5; });
    var k = Math.max(0.34, Math.min(0.56, width / 2800));
    var x = -BG_TILE_GAP / 2, y = BG_TILE_GAP / 2, rowH = 0, i = 0;
    while (y < height && i < 60) {
      var t = order[i++ % order.length], w = t.w * k, h = t.h * k;
      if (x + w > width + BG_TILE_GAP && x > 0) { x = -BG_TILE_GAP / 2; y += rowH + BG_TILE_GAP; rowH = 0; }
      drawTile(g, t, x, y, k, rand() < 0.5);
      x += w + BG_TILE_GAP; rowH = Math.max(rowH, h);
    }
  }

  function buildMask() {
    var size = Math.round(TORCH_RADIUS * 2 * dpr);
    scratch.width = size; scratch.height = size;
    mask = sctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    mask.addColorStop(0, "rgba(0,0,0,1)");
    mask.addColorStop(0.45, "rgba(0,0,0,0.85)");
    mask.addColorStop(1, "rgba(0,0,0,0)");
  }

  function drawTorch() {
    var size = scratch.width, srcSpan = (TORCH_RADIUS * 2 / LENS_ZOOM) * bgScale;
    sctx.globalCompositeOperation = "copy";
    sctx.drawImage(bg, (mouse.x - TORCH_RADIUS / LENS_ZOOM) * bgScale, (mouse.y - TORCH_RADIUS / LENS_ZOOM) * bgScale,
      srcSpan, srcSpan, 0, 0, size, size);
    sctx.globalCompositeOperation = "destination-in";
    sctx.fillStyle = mask;
    sctx.fillRect(0, 0, size, size);
    ctx.globalAlpha = 1;
    ctx.drawImage(scratch, mouse.x - TORCH_RADIUS, mouse.y - TORCH_RADIUS, TORCH_RADIUS * 2, TORCH_RADIUS * 2);
  }

  /* ---------- render loop ---------- */

  function drawCircuit(now) {
    ctx.lineWidth = 1.3; ctx.lineJoin = "round"; ctx.lineCap = "round";
    ctx.strokeStyle = "rgba(" + COLOR + ", 1)";
    ctx.shadowColor = "rgba(" + COLOR + ", 0.9)"; ctx.shadowBlur = 6;
    circuit = circuit.filter(function (part) {
      var t = (now - part.born) / LIFE_MS;
      if (t >= 1) return false;
      var a = t < FADE_IN ? t / FADE_IN : 1 - (t - FADE_IN) / (1 - FADE_IN);
      ctx.globalAlpha = a * a * (3 - 2 * a) * 0.55;
      ctx.beginPath();
      part.lines.forEach(function (l) {
        ctx.moveTo(l[0], l[1]);
        for (var i = 2; i < l.length; i += 2) ctx.lineTo(l[i], l[i + 1]);
      });
      ctx.stroke();
      return true;
    });
    ctx.shadowBlur = 0; ctx.globalAlpha = 1;
  }

  function render(now) {
    ctx.clearRect(0, 0, width, height);
    if (bg) { ctx.globalAlpha = BG_BASE_ALPHA / BG_LINE_ALPHA; ctx.drawImage(bg, 0, 0, width, height); ctx.globalAlpha = 1; }
    if (mouse.active) {
      if (bg) drawTorch();
      var g = ctx.createRadialGradient(mouse.x, mouse.y, 0, mouse.x, mouse.y, TORCH_RADIUS);
      g.addColorStop(0, "rgba(" + COLOR + ", 0.09)");
      g.addColorStop(1, "rgba(" + COLOR + ", 0)");
      ctx.fillStyle = g;
      ctx.fillRect(mouse.x - TORCH_RADIUS, mouse.y - TORCH_RADIUS, TORCH_RADIUS * 2, TORCH_RADIUS * 2);
    }
    drawCircuit(now);
  }

  function frame(now) {
    rafId = 0;
    if (document.hidden) return;
    mouse.x += (mouse.tx - mouse.x) * 0.25;
    mouse.y += (mouse.ty - mouse.y) * 0.25;
    render(now);
    var settling = Math.abs(mouse.tx - mouse.x) + Math.abs(mouse.ty - mouse.y) > 0.5;
    if (circuit.length || (mouse.active && settling)) rafId = requestAnimationFrame(frame);
  }

  function start() { if (!rafId && !document.hidden) rafId = requestAnimationFrame(frame); }

  function onMove(e) {
    var dx = e.clientX - mouse.tx, dy = e.clientY - mouse.ty;
    if (dx || dy) { mouse.vx = dx; mouse.vy = dy; }
    mouse.tx = e.clientX; mouse.ty = e.clientY;
    if (!mouse.active) { mouse.x = mouse.tx; mouse.y = mouse.ty; mouse.active = true; }
    if (!lastSpawn) lastSpawn = { x: e.clientX, y: e.clientY };
    var sx = e.clientX - lastSpawn.x, sy = e.clientY - lastSpawn.y;
    if (sx * sx + sy * sy >= SPAWN_DISTANCE * SPAWN_DISTANCE) {
      lastSpawn.x = e.clientX; lastSpawn.y = e.clientY;
      extend(e.clientX, e.clientY, performance.now());
    }
    start();
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    width = window.innerWidth; height = window.innerHeight;
    canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    buildMask();
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () { buildBackground(); render(performance.now()); }, 150);
  }

  resize();
  window.addEventListener("resize", resize);
  window.addEventListener("mousemove", onMove, { passive: true });
  document.addEventListener("mouseleave", function () { mouse.active = false; start(); });
  document.addEventListener("visibilitychange", function () {
    if (document.hidden) { if (rafId) cancelAnimationFrame(rafId); rafId = 0; } else start();
  });

  if (window.fetch) {
    fetch(CIRCUIT_URL).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
      if (!data || !data.tiles) return;
      tiles = data.tiles; buildBackground(); render(performance.now());
    }).catch(function () { /* without the layout only the live circuit is drawn */ });
  }
})();

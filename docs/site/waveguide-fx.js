/* Cursor effects on one canvas:
   1. "Flashlight": a static background layout (waveguide routes from Lunima's own example
      designs, see circuit.json) is faint by default and revealed around the cursor.
   2. One connected photonic circuit at a time grows along the mouse path and fades out.
   Everything is anchored in page coordinates and scrolls with the content. The canvas is fixed
   to the viewport; the background is cached once per resize and only composited per frame.
   Off for reduced motion and touch-only devices. Needs circuit-parts.js. */
(function () {
  "use strict";
  var mq = window.matchMedia;
  if (!mq || mq("(prefers-reduced-motion: reduce)").matches || mq("(hover: none)").matches) return;
  var canvas = document.getElementById("wg-fx");
  var ctx = canvas && canvas.getContext("2d");
  var parts = window.LunimaParts;
  if (!ctx || !parts) return;

  var MAX_CHAIN_PARTS = 14;       // parts in one growing circuit
  var MAX_POINTS_PER_FRAME = 900; // drawing budget for the live circuit
  var LIFE_MS = 2600;
  var FADE_IN = 0.1;
  var SPAWN_DISTANCE = 80;
  var RESTART_DISTANCE = 420;
  var GRID = 12;
  var TORCH_RADIUS = 230;
  var LENS_ZOOM = 1;              // > 1 switches a magnifying lens on (costs frame time)
  var BG_BASE_ALPHA = 0.045;      // background visibility without the torch
  var BG_LINE_ALPHA = 0.6;
  var BG_TILE_GAP = 56;
  var BG_STRIP_MIN_HEIGHT = 900;
  var MAX_DPR = 2;
  var IDLE_TIMEOUT_MS = 2500;
  var FALLBACK_DELAY_MS = 1200;
  var GLOW_WIDTH = 4.5, GLOW_ALPHA = 0.12, LINE_WIDTH = 1.3, PEAK_ALPHA = 0.55;
  var COLOR = "122, 163, 255";
  var CIRCUIT_URL = "site/circuit.json";

  var dpr = 1, width = 0, height = 0, rafId = 0, resizeTimer = 0;
  var tiles = null, strip = null, stripH = 0;
  var scratch = document.createElement("canvas");
  var sctx = scratch.getContext("2d");
  var mask = null;
  var scrollY = window.pageYOffset || 0;
  var mouse = { x: 0, y: 0, active: false, vx: 1, vy: 0 };
  var lastSpawn = null;
  var chain = [];     // parts of the single live circuit: { lines (page coords), minY, maxY, born }
  var head = null;    // open port of the live circuit: { x, y, dir }
  var headBorn = 0;

  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }
  function snap(v) { return Math.round(v / GRID) * GRID; }

  /* ---------- connected circuit (page coordinates) ---------- */

  var COS = [1, 0, -1, 0], SIN = [0, 1, 0, -1];

  function place(from, part, s, now) {
    var d = from.dir, c = COS[d], sn = SIN[d];
    function tx(x, y) { return from.x + c * x - sn * s * y; }
    function ty(x, y) { return from.y + sn * x + c * s * y; }
    var minY = Infinity, maxY = -Infinity;
    var lines = part.lines.map(function (l) {
      var o = [];
      for (var i = 0; i < l.length; i += 2) {
        var y = ty(l[i], l[i + 1]);
        minY = Math.min(minY, y); maxY = Math.max(maxY, y);
        o.push(tx(l[i], l[i + 1]), y);
      }
      return o;
    });
    chain.push({ lines: lines, minY: minY, maxY: maxY, born: now });
    return part.outs.map(function (p) {
      return { x: tx(p.x, p.y), y: ty(p.x, p.y), dir: (d + s * p.a + 4) % 4 };
    });
  }

  function nearest(ports, x, y) {
    var best = null, bd = Infinity;
    ports.forEach(function (p) {
      var d = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
      if (d < bd) { bd = d; best = p; }
    });
    return best;
  }

  function startCircuit(x, y, now) {
    var dir = Math.abs(mouse.vx) > Math.abs(mouse.vy) ? (mouse.vx > 0 ? 0 : 2) : (mouse.vy > 0 ? 1 : 3);
    head = nearest(place({ x: snap(x), y: snap(y), dir: dir }, parts.gratingIn(), 1, now), x, y);
    headBorn = now;
  }

  function chooseStep(from, x, y) {
    var target = Math.round(Math.atan2(y - from.y, x - from.x) / (Math.PI / 2));
    var rel = ((target - from.dir) % 4 + 4) % 4;
    if (rel === 1) return { part: parts.bend(1), s: 1 };
    if (rel === 3) return { part: parts.bend(-1), s: -1 };
    if (rel === 2) { var k = pick([-1, 1]); return { part: parts.bend(k), s: k }; }
    var kind = pick(["straight", "sbend", "mmi1x2", "mzi", "ring", "coupler", "mmi2x2"]);
    var side = pick([-1, 1]);
    return { part: parts[kind](side), s: side };
  }

  function finishChain(now) {
    if (head) place(head, parts.grating(), 1, now);
    head = null;
  }

  /* Grows the live circuit towards (x, y); a new one only starts once the previous has faded. */
  function extend(x, y, now) {
    if (!chain.length) { startCircuit(x, y, now); return; }
    if (!head || now - headBorn > LIFE_MS) { head = null; return; }
    if (Math.hypot(head.x - x, head.y - y) > RESTART_DISTANCE) return;
    var step = chooseStep(head, x, y);
    head = nearest(place(head, step.part, step.s, now), x, y);
    headBorn = now;
    if (chain.length >= MAX_CHAIN_PARTS - 1) finishChain(now);
  }

  /* ---------- flashlight background (cached strip, tiled along the page) ---------- */

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

  function buildStrip() {
    if (!tiles || !tiles.length) { strip = null; return; }
    stripH = Math.max(BG_STRIP_MIN_HEIGHT, height);
    strip = document.createElement("canvas");
    strip.width = Math.round(width * dpr); strip.height = Math.round(stripH * dpr);
    var g = strip.getContext("2d");
    g.scale(dpr, dpr);
    g.strokeStyle = "rgba(" + COLOR + ", " + BG_LINE_ALPHA + ")";
    g.lineWidth = 0.9; g.lineJoin = "round";
    var rand = rng(7), order = tiles.slice().sort(function () { return rand() - 0.5; });
    var k = Math.max(0.34, Math.min(0.56, width / 2800));
    var x = -BG_TILE_GAP / 2, y = BG_TILE_GAP / 2, rowH = 0, i = 0;
    while (y < stripH && i < 60) {
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

  /* Calls draw(topInPage) for every repeat of the strip that overlaps [top, top + span). */
  function eachStrip(top, span, draw) {
    var first = Math.floor(top / stripH), last = Math.floor((top + span) / stripH);
    for (var n = Math.max(first, 0); n <= last; n++) draw(n * stripH);
  }

  function drawBase() {
    ctx.globalAlpha = BG_BASE_ALPHA / BG_LINE_ALPHA;
    eachStrip(scrollY, height, function (top) { ctx.drawImage(strip, 0, top - scrollY, width, stripH); });
    ctx.globalAlpha = 1;
  }

  function drawTorch() {
    var z = LENS_ZOOM, pageY = mouse.y + scrollY, c = TORCH_RADIUS;
    sctx.setTransform(1, 0, 0, 1, 0, 0);
    sctx.globalCompositeOperation = "source-over";
    sctx.clearRect(0, 0, scratch.width, scratch.height);
    sctx.setTransform(dpr * z, 0, 0, dpr * z, c * dpr, c * dpr);
    sctx.translate(-mouse.x, -pageY);
    eachStrip(pageY - c / z, 2 * c / z, function (top) { sctx.drawImage(strip, 0, top, width, stripH); });
    sctx.setTransform(1, 0, 0, 1, 0, 0);
    sctx.globalCompositeOperation = "destination-in";
    sctx.fillStyle = mask;
    sctx.fillRect(0, 0, scratch.width, scratch.height);
    ctx.drawImage(scratch, mouse.x - c, mouse.y - c, c * 2, c * 2);
  }

  /* ---------- render loop ---------- */

  function partAlpha(part, now) {
    var t = (now - part.born) / LIFE_MS;
    if (t >= 1) return 0;
    var a = t < FADE_IN ? t / FADE_IN : 1 - (t - FADE_IN) / (1 - FADE_IN);
    return a * a * (3 - 2 * a);
  }

  function strokePart(part, alpha, lineWidth) {
    ctx.lineWidth = lineWidth; ctx.globalAlpha = alpha;
    ctx.beginPath();
    part.lines.forEach(function (l) {
      ctx.moveTo(l[0], l[1]);
      for (var i = 2; i < l.length; i += 2) ctx.lineTo(l[i], l[i + 1]);
    });
    ctx.stroke();
  }

  function drawCircuit(now) {
    chain = chain.filter(function (p) { return partAlpha(p, now) > 0; });
    if (!chain.length) { head = null; return; }
    ctx.save();
    ctx.translate(0, -scrollY);
    ctx.lineJoin = "round"; ctx.lineCap = "round";
    ctx.strokeStyle = "rgba(" + COLOR + ", 1)";
    var budget = MAX_POINTS_PER_FRAME;
    for (var i = chain.length - 1; i >= 0 && budget > 0; i--) {
      var p = chain[i];
      if (p.maxY < scrollY || p.minY > scrollY + height) continue;
      var a = partAlpha(p, now);
      strokePart(p, a * GLOW_ALPHA, GLOW_WIDTH);
      strokePart(p, a * PEAK_ALPHA, LINE_WIDTH);
      p.lines.forEach(function (l) { budget -= l.length / 2; });
    }
    ctx.restore();
    ctx.globalAlpha = 1;
  }

  function render(now) {
    ctx.clearRect(0, 0, width, height);
    if (strip) drawBase();
    if (mouse.active) {
      if (strip) drawTorch();
      var g = ctx.createRadialGradient(mouse.x, mouse.y, 0, mouse.x, mouse.y, TORCH_RADIUS);
      g.addColorStop(0, "rgba(" + COLOR + ", 0.09)");
      g.addColorStop(1, "rgba(" + COLOR + ", 0)");
      ctx.fillStyle = g;
      ctx.fillRect(mouse.x - TORCH_RADIUS, mouse.y - TORCH_RADIUS, TORCH_RADIUS * 2, TORCH_RADIUS * 2);
    }
    drawCircuit(now);
  }

  /* Redraws only while something changes: pointer or scroll moved, or circuit parts are fading. */
  function frame(now) {
    rafId = 0;
    if (document.hidden) return;
    render(now);
    if (chain.length) rafId = requestAnimationFrame(frame);
  }

  function schedule() { if (!rafId && !document.hidden) rafId = requestAnimationFrame(frame); }

  function onMove(e) {
    var dx = e.clientX - mouse.x, dy = e.clientY - mouse.y;
    if (mouse.active && (dx || dy)) { mouse.vx = dx; mouse.vy = dy; }
    mouse.x = e.clientX; mouse.y = e.clientY; mouse.active = true;
    var px = e.clientX, py = e.clientY + scrollY;
    if (!lastSpawn) lastSpawn = { x: px, y: py };
    var sx = px - lastSpawn.x, sy = py - lastSpawn.y;
    if (sx * sx + sy * sy >= SPAWN_DISTANCE * SPAWN_DISTANCE) {
      lastSpawn.x = px; lastSpawn.y = py;
      extend(px, py, performance.now());
    }
    schedule();
  }

  function onScroll() { scrollY = window.pageYOffset || 0; schedule(); }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR);
    width = window.innerWidth; height = window.innerHeight;
    canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    buildMask();
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () { buildStrip(); render(performance.now()); }, 150);
  }

  function loadCircuit() {
    if (!window.fetch) return;
    fetch(CIRCUIT_URL).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
      if (!data || !data.tiles) return;
      tiles = data.tiles; buildStrip(); schedule();
    }).catch(function () { /* without the layout only the live circuit is drawn */ });
  }

  function loadWhenIdle() {
    if (window.requestIdleCallback) requestIdleCallback(loadCircuit, { timeout: IDLE_TIMEOUT_MS });
    else setTimeout(loadCircuit, FALLBACK_DELAY_MS);
  }

  resize();
  window.addEventListener("resize", resize, { passive: true });
  window.addEventListener("scroll", onScroll, { passive: true });
  window.addEventListener("pointermove", onMove, { passive: true });
  document.addEventListener("mouseleave", function () { mouse.active = false; schedule(); });
  document.addEventListener("visibilitychange", function () {
    if (document.hidden) { if (rafId) cancelAnimationFrame(rafId); rafId = 0; } else schedule();
  });
  if (document.readyState === "complete") loadWhenIdle();
  else window.addEventListener("load", loadWhenIdle);
})();

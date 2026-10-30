/* Cursor effect: moving the mouse sketches small photonic-circuit motifs (waveguides with
   90-degree bends, MMIs, directional couplers, ring resonators) that glow blue and fade out. */
(function () {
  "use strict";
  var mq = window.matchMedia;
  if (!mq || mq("(prefers-reduced-motion: reduce)").matches || mq("(hover: none)").matches) return;
  var canvas = document.getElementById("wg-fx");
  var ctx = canvas && canvas.getContext("2d");
  if (!ctx) return;

  var MAX_SHAPES = 60;
  var LIFE_MS = 1700;
  var SPAWN_DISTANCE = 90;
  var GRID = 12;
  var GLOW_RADIUS = 260;
  var COLOR = "122, 163, 255";
  var shapes = [];
  var dpr = 1, width = 0, height = 0, rafId = 0;
  var mouse = { x: 0, y: 0, tx: 0, ty: 0, active: false };
  var lastSpawn = null;

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    width = window.innerWidth; height = window.innerHeight;
    canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  function snap(v) { return Math.round(v / GRID) * GRID; }
  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }

  function arc(cx, cy, r, a0, a1) {
    var pts = [], n = 10;
    for (var i = 0; i <= n; i++) {
      var a = a0 + (a1 - a0) * i / n;
      pts.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]);
    }
    return pts;
  }

  /* Smooth S-bend from (x, y) to (x + dx, y + dy), as two opposite quarter-ish arcs. */
  function sBend(x, y, dx, dy) {
    var pts = [], n = 12;
    for (var i = 0; i <= n; i++) {
      var t = i / n, k = t * t * (3 - 2 * t);
      pts.push([x + dx * t, y + dy * k]);
    }
    return pts;
  }

  /* Waveguide with a true 90-degree bend (quarter arc) between straight stubs. */
  function buildRoute() {
    var s = GRID * 2, r = GRID * 2, d = pick([-1, 1]);
    var bend = arc(s, d * r, r, -d * Math.PI / 2, 0);
    return [[[0, 0]].concat(bend, [[s + r, d * (r + s)]])];
  }

  /* Multi-mode interference coupler: box with tapers, 1x2 or 2x2. */
  function buildMmi() {
    var w = GRID * 2.4, h = GRID * 1.6, t = GRID * 1.4, two = Math.random() < 0.5;
    var box = [[t, -h / 2], [t + w, -h / 2], [t + w, h / 2], [t, h / 2], [t, -h / 2]];
    var o = h / 4;
    var lines = [box];
    if (two) {
      lines.push([[0, -o], [t, -o]], [[0, o], [t, o]]);
    } else {
      lines.push([[0, 0], [t, 0]]);
    }
    lines.push([[t + w, -o], [t * 2 + w, -o]], [[t + w, o], [t * 2 + w, o]]);
    return lines;
  }

  /* Directional coupler: two guides approach, run parallel, and separate again. */
  function buildCoupler() {
    var gap = GRID * 0.7, drop = GRID * 1.6, bendLen = GRID * 2.2, run = GRID * 2.4, lines = [];
    [-1, 1].forEach(function (k) {
      var y = k * gap / 2, far = k * (gap / 2 + drop);
      var line = [[0, far]].concat(
        sBend(0, far, bendLen, y - far),
        [[bendLen + run, y]],
        sBend(bendLen + run, y, bendLen, far - y)
      );
      lines.push(line);
    });
    return lines;
  }

  /* Ring resonator coupled to a bus waveguide. */
  function buildRing() {
    var r = GRID * 1.3, gap = GRID * 0.35, len = GRID * 4.4;
    return [[[0, 0], [len, 0]], arc(len / 2, -(r + gap), r, 0, Math.PI * 2)];
  }

  var BUILDERS = [buildRoute, buildRoute, buildMmi, buildCoupler, buildRing];

  function spawn(x, y) {
    if (shapes.length >= MAX_SHAPES) shapes.shift();
    var rot = pick([0, 1, 2, 3]) * Math.PI / 2;
    var jitter = GRID * 5;
    shapes.push({
      lines: pick(BUILDERS)(),
      x: snap(x + (Math.random() - 0.5) * jitter),
      y: snap(y + (Math.random() - 0.5) * jitter),
      cos: Math.cos(rot), sin: Math.sin(rot),
      flip: Math.random() < 0.5 ? -1 : 1,
      born: performance.now()
    });
  }

  function onMove(e) {
    mouse.tx = e.clientX; mouse.ty = e.clientY;
    if (!mouse.active) { mouse.x = mouse.tx; mouse.y = mouse.ty; mouse.active = true; }
    if (!lastSpawn) lastSpawn = { x: e.clientX, y: e.clientY };
    var dx = e.clientX - lastSpawn.x, dy = e.clientY - lastSpawn.y;
    if (dx * dx + dy * dy >= SPAWN_DISTANCE * SPAWN_DISTANCE) {
      lastSpawn.x = e.clientX; lastSpawn.y = e.clientY;
      spawn(e.clientX, e.clientY);
    }
    start();
  }

  function drawShape(s, now) {
    var t = (now - s.born) / LIFE_MS;
    if (t >= 1) return false;
    var a = t < 0.12 ? t / 0.12 : 1 - (t - 0.12) / 0.88;
    ctx.globalAlpha = a * a * (3 - 2 * a) * 0.5;
    ctx.beginPath();
    for (var i = 0; i < s.lines.length; i++) {
      var ln = s.lines[i];
      for (var j = 0; j < ln.length; j++) {
        var px = ln[j][0], py = ln[j][1] * s.flip;
        var X = s.x + px * s.cos - py * s.sin, Y = s.y + px * s.sin + py * s.cos;
        if (j === 0) ctx.moveTo(X, Y); else ctx.lineTo(X, Y);
      }
    }
    ctx.stroke();
    return true;
  }

  function frame(now) {
    rafId = 0;
    if (document.hidden) return;
    ctx.clearRect(0, 0, width, height);

    mouse.x += (mouse.tx - mouse.x) * 0.18;
    mouse.y += (mouse.ty - mouse.y) * 0.18;
    if (mouse.active) {
      var g = ctx.createRadialGradient(mouse.x, mouse.y, 0, mouse.x, mouse.y, GLOW_RADIUS);
      g.addColorStop(0, "rgba(" + COLOR + ", 0.10)");
      g.addColorStop(1, "rgba(" + COLOR + ", 0)");
      ctx.globalAlpha = 1;
      ctx.fillStyle = g;
      ctx.fillRect(mouse.x - GLOW_RADIUS, mouse.y - GLOW_RADIUS, GLOW_RADIUS * 2, GLOW_RADIUS * 2);
    }

    ctx.lineWidth = 1.25; ctx.lineJoin = "round"; ctx.lineCap = "round";
    ctx.strokeStyle = "rgba(" + COLOR + ", 1)";
    ctx.shadowColor = "rgba(" + COLOR + ", 0.9)"; ctx.shadowBlur = 6;
    shapes = shapes.filter(function (s) { return drawShape(s, now); });
    ctx.shadowBlur = 0; ctx.globalAlpha = 1;

    var settling = Math.abs(mouse.tx - mouse.x) + Math.abs(mouse.ty - mouse.y) > 0.5;
    if (shapes.length || (mouse.active && settling)) rafId = requestAnimationFrame(frame);
    else ctx.clearRect(0, 0, width, height);
  }

  function start() { if (!rafId && !document.hidden) rafId = requestAnimationFrame(frame); }

  resize();
  window.addEventListener("resize", resize);
  window.addEventListener("mousemove", onMove, { passive: true });
  document.addEventListener("mouseleave", function () { mouse.active = false; start(); });
  document.addEventListener("visibilitychange", function () {
    if (document.hidden) { if (rafId) cancelAnimationFrame(rafId); rafId = 0; } else start();
  });
})();

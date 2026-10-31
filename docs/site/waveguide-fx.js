/* Cursor "flashlight": a background layout (waveguide routes from Lunima's own example designs,
   see circuit.json) is faint by default and revealed around the cursor.
   The layout is anchored in page coordinates and scrolls with the content. Scrolling must not
   repaint a viewport-sized canvas (2D canvas is often software-rendered), so the faint layout is
   a CSS background on #wg-bg that the browser scrolls itself, and the torch is a small canvas
   moved with a transform.
   Off for reduced motion and touch-only devices. */
(function () {
  "use strict";
  var mq = window.matchMedia;
  if (!mq || mq("(prefers-reduced-motion: reduce)").matches || mq("(hover: none)").matches) return;
  var torch = document.getElementById("wg-torch");
  var bg = document.getElementById("wg-bg");
  var tctx = torch && torch.getContext("2d");
  if (!tctx || !bg) return;

  var TORCH_RADIUS = 260;
  var BG_BASE_ALPHA = 0.045;      // background visibility without the torch
  var BG_LINE_ALPHA = 0.95;
  var BG_TILE_GAP = 56;
  var BG_STRIP_MIN_HEIGHT = 900;
  var GLOW_ALPHA = 0.08;
  var MAX_DPR = 2;
  var IDLE_TIMEOUT_MS = 2500;
  var FALLBACK_DELAY_MS = 1200;
  var COLOR = "122, 163, 255";
  var CIRCUIT_URL = "site/circuit.json";

  var dpr = 1, width = 0, height = 0, rafId = 0, resizeTimer = 0;
  var tiles = null, strip = null, stripH = 0, bgUrl = null;
  var mask = null, glow = null, torchShown = false;
  var scrollY = window.pageYOffset || 0;
  var mouse = { x: 0, y: 0, active: false };

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
      g.rect(flip ? X(q[0] + q[2]) : X(q[0]), Y(q[1]), q[2] * k, q[3] * k);
    });
    g.stroke();
  }

  /* One strip of tiled example layouts, repeated down the page. */
  function buildStrip() {
    if (!tiles || !tiles.length) { strip = null; return; }
    stripH = Math.max(BG_STRIP_MIN_HEIGHT, height);
    strip = document.createElement("canvas");
    strip.width = Math.round(width * dpr); strip.height = Math.round(stripH * dpr);
    var g = strip.getContext("2d");
    g.scale(dpr, dpr);
    g.strokeStyle = "rgba(" + COLOR + ", " + BG_LINE_ALPHA + ")";
    g.lineWidth = 1.25; g.lineJoin = "round";
    var rand = rng(7), order = tiles.slice().sort(function () { return rand() - 0.5; });
    var k = Math.max(0.34, Math.min(0.56, width / 2800));
    var x = -BG_TILE_GAP / 2, y = BG_TILE_GAP / 2, rowH = 0, i = 0;
    while (y < stripH && i < 60) {
      var t = order[i++ % order.length], w = t.w * k, h = t.h * k;
      if (x + w > width + BG_TILE_GAP && x > 0) { x = -BG_TILE_GAP / 2; y += rowH + BG_TILE_GAP; rowH = 0; }
      drawTile(g, t, x, y, k, rand() < 0.5);
      x += w + BG_TILE_GAP; rowH = Math.max(rowH, h);
    }
    publishBackground(strip, width, stripH);
  }

  /* The faint, always-visible layout: the strip as a repeating CSS background, scrolled by the browser. */
  function publishBackground(source, w, h) {
    if (!source.toBlob) return;
    source.toBlob(function (blob) {
      if (!blob || source !== strip) return;
      if (bgUrl) URL.revokeObjectURL(bgUrl);
      bgUrl = URL.createObjectURL(blob);
      bg.style.backgroundImage = "url(" + bgUrl + ")";
      bg.style.backgroundSize = w + "px " + h + "px";
      bg.style.opacity = String(BG_BASE_ALPHA / BG_LINE_ALPHA);
    });
  }

  function buildTorch() {
    var size = Math.round(TORCH_RADIUS * 2 * dpr), c = size / 2;
    torch.width = size; torch.height = size;
    torch.style.width = torch.style.height = TORCH_RADIUS * 2 + "px";
    mask = tctx.createRadialGradient(c, c, 0, c, c, c);
    mask.addColorStop(0, "rgba(0,0,0,1)");
    mask.addColorStop(0.45, "rgba(0,0,0,0.85)");
    mask.addColorStop(1, "rgba(0,0,0,0)");
    glow = tctx.createRadialGradient(c, c, 0, c, c, c);
    glow.addColorStop(0, "rgba(" + COLOR + ", " + GLOW_ALPHA + ")");
    glow.addColorStop(1, "rgba(" + COLOR + ", 0)");
  }

  /* Calls draw(topInPage) for every repeat of the strip that overlaps [top, top + span). */
  function eachStrip(top, span, draw) {
    var first = Math.floor(top / stripH), last = Math.floor((top + span) / stripH);
    for (var n = Math.max(first, 0); n <= last; n++) draw(n * stripH);
  }

  function drawTorch() {
    var pageY = mouse.y + scrollY, c = TORCH_RADIUS, size = torch.width;
    tctx.setTransform(1, 0, 0, 1, 0, 0);
    tctx.globalCompositeOperation = "source-over";
    tctx.clearRect(0, 0, size, size);
    if (strip) {
      tctx.setTransform(dpr, 0, 0, dpr, c * dpr, c * dpr);
      tctx.translate(-mouse.x, -pageY);
      eachStrip(pageY - c, 2 * c, function (top) { tctx.drawImage(strip, 0, top, width, stripH); });
      tctx.setTransform(1, 0, 0, 1, 0, 0);
      tctx.globalCompositeOperation = "destination-in";
      tctx.fillStyle = mask;
      tctx.fillRect(0, 0, size, size);
      tctx.globalCompositeOperation = "source-over";
    }
    tctx.fillStyle = glow;
    tctx.fillRect(0, 0, size, size);
    torch.style.transform = "translate3d(" + (mouse.x - c) + "px," + (mouse.y - c) + "px,0)";
  }

  function showTorch(show) {
    if (show === torchShown) return;
    torchShown = show;
    torch.style.visibility = show ? "visible" : "hidden";
  }

  /* Redraws only when the pointer or the scroll position moved. */
  function frame() {
    rafId = 0;
    if (document.hidden) return;
    showTorch(mouse.active);
    if (mouse.active) drawTorch();
  }

  function schedule() { if (!rafId && !document.hidden) rafId = requestAnimationFrame(frame); }

  function onMove(e) {
    mouse.x = e.clientX; mouse.y = e.clientY; mouse.active = true;
    schedule();
  }

  function onScroll() {
    scrollY = window.pageYOffset || 0;
    if (mouse.active) schedule();
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR);
    width = window.innerWidth; height = window.innerHeight;
    buildTorch();
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () { buildStrip(); schedule(); }, 150);
  }

  function loadCircuit() {
    if (!window.fetch) return;
    fetch(CIRCUIT_URL).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
      if (!data || !data.tiles) return;
      tiles = data.tiles; buildStrip(); schedule();
    }).catch(function () { /* without the layout the torch shows only its glow */ });
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

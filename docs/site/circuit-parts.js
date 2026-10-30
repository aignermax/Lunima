/* Photonic building blocks for the cursor circuit. Every part is drawn in a local frame
   (input at 0,0, light travelling +x, +y to the side) and returns polylines plus output
   ports, so parts can be chained by their ports. Exposed as window.LunimaParts. */
(function () {
  "use strict";

  var P = 24;          // port pitch shared by every multi-port part
  var R = 24;          // bend radius
  var STUB = 10;       // straight lead-in / lead-out
  var ARC_STEPS = 10;

  function arc(cx, cy, r, a0, a1) {
    var pts = [];
    for (var i = 0; i <= ARC_STEPS; i++) {
      var a = a0 + (a1 - a0) * i / ARC_STEPS;
      pts.push(cx + r * Math.cos(a), cy + r * Math.sin(a));
    }
    return pts;
  }

  function sbend(x, y, dx, dy) {
    var pts = [];
    for (var i = 0; i <= ARC_STEPS; i++) {
      var t = i / ARC_STEPS;
      pts.push(x + dx * t, y + dy * (1 - Math.cos(Math.PI * t)) / 2);
    }
    return pts;
  }

  function rect(x0, y0, x1, y1) { return [x0, y0, x1, y0, x1, y1, x0, y1, x0, y0]; }

  function mirrorX(lines, width) {
    return lines.map(function (l) {
      var o = [];
      for (var i = 0; i < l.length; i += 2) o.push(width - l[i], l[i + 1]);
      return o;
    });
  }

  function shiftX(lines, dx) {
    return lines.map(function (l) {
      return l.map(function (v, i) { return i % 2 ? v : v + dx; });
    });
  }

  function shiftY(lines, dy) {
    return lines.map(function (l) {
      return l.map(function (v, i) { return i % 2 ? v + dy : v; });
    });
  }

  /* 1x2 MMI: tapers in/out, body box, both outputs on the shared pitch. */
  var MMI_BODY = [24, 60], MMI_H = 1.5 * P, MMI_END = 84;
  function mmi1x2Lines() {
    var h = MMI_H / 2, c = P / 2, lines = [];
    lines.push([0, 0, STUB, 0], [STUB, -2, 24, -7, 24, 7, STUB, 2, STUB, -2]);
    lines.push(rect(MMI_BODY[0], -h, MMI_BODY[1], h));
    [-1, 1].forEach(function (k) {
      lines.push([60, k * c - 7, 74, k * c - 2, 74, k * c + 2, 60, k * c + 7]);
      lines.push([74, k * c, MMI_END, k * c]);
    });
    return lines;
  }

  /* Grating coupler fan starting at (x, y); dir -1 extends it towards -x. */
  function grating(lines, x, y, dir) {
    var d = dir || 1;
    lines.push([x, y, x + d * STUB, y], [x + d * STUB, y, x + d * 34, y - 12, x + d * 34, y + 12, x + d * STUB, y]);
    [18, 24, 30].forEach(function (t) { lines.push([x + d * t, y - 5, x + d * t, y + 5]); });
  }

  var parts = {
    straight: function () {
      return { lines: [[0, 0, 36, 0]], outs: [{ x: 36, y: 0, a: 0 }] };
    },
    bend: function (s) {
      var pts = arc(0, s * R, R, -s * Math.PI / 2, 0);
      return { lines: [pts], outs: [{ x: R, y: s * R, a: s }] };
    },
    sbend: function (s) {
      return { lines: [sbend(0, 0, 48, s * P)], outs: [{ x: 48, y: s * P, a: 0 }] };
    },
    mmi1x2: function () {
      return { lines: mmi1x2Lines(), outs: [{ x: MMI_END, y: -P / 2, a: 0 }, { x: MMI_END, y: P / 2, a: 0 }] };
    },
    mmi2x2: function () {
      var lines = shiftY(mmi1x2Lines(), P / 2);
      lines.push([0, P, STUB, P], [STUB, P - 2, 24, P - 7, 24, P + 7, STUB, P + 2, STUB, P - 2]);
      lines.push([0, P, -4, P]);
      grating(lines, -4, P, -1);
      return { lines: lines, outs: [{ x: MMI_END, y: 0, a: 0 }, { x: MMI_END, y: P, a: 0 }] };
    },
    mzi: function () {
      var lines = mmi1x2Lines(), dy = 18, c = P / 2, x0 = MMI_END, arm = 40, run = 56;
      [-1, 1].forEach(function (k) {
        lines.push(sbend(x0, k * c, arm, k * dy));
        lines.push([x0 + arm, k * (c + dy), x0 + arm + run, k * (c + dy)]);
        lines.push(sbend(x0 + arm + run, k * (c + dy), arm, -k * dy));
      });
      lines.push(rect(x0 + arm + 14, -(c + dy) - 4, x0 + arm + run - 14, -(c + dy) + 4));
      var xe = x0 + 2 * arm + run;
      lines = lines.concat(mirrorX(mmi1x2Lines(), xe + MMI_END));
      return { lines: lines, outs: [{ x: xe + MMI_END, y: 0, a: 0 }] };
    },
    ring: function (s) {
      var r = 22, gap = 5, lines = [[0, 0, 72, 0]];
      lines.push(arc(36, -s * (r + gap), r, 0, Math.PI * 2));
      return { lines: lines, outs: [{ x: 72, y: 0, a: 0 }] };
    },
    coupler: function () {
      var gap = 6, d = (P - gap) / 2, L = 30, run = 48, lines = [], x = L + run;
      lines.push(sbend(0, 0, L, d), [L, d, x, d], sbend(x, d, L, -d));
      lines.push(sbend(0, P, L, -d), [L, P - d, x, P - d], sbend(x, P - d, L, d));
      lines.push([0, P, -4, P]);
      grating(lines, -4, P, -1);
      return { lines: lines, outs: [{ x: x + L, y: 0, a: 0 }, { x: x + L, y: P, a: 0 }] };
    },
    grating: function () {
      var lines = [];
      grating(lines, 0, 0, 1);
      return { lines: lines, outs: [] };
    },
    gratingIn: function () {
      var lines = [];
      grating(lines, 0, 0, -1);
      return { lines: shiftX(lines, 34 + STUB), outs: [{ x: 34 + STUB, y: 0, a: 0 }] };
    }
  };

  window.LunimaParts = parts;
})();

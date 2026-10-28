// Vector-authored upgrade icons rasterized to PNG (white on transparent, so the UI tints them).
// usage: node make-upgrade-icons.js <repoRoot>
// Shapes live in a 100x100 space. Each icon is an ordered list of ops: {fill|cut, shape}.
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

// ------------------------------------------------------------------ PNG
function crc32(buf) {
  let c, crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) {
    c = (crc ^ buf[n]) & 0xff;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crc = (crc >>> 8) ^ c;
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function png(width, height, rgba) {
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 4 + 1)] = 0;
    rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0)),
  ]);
}

// ------------------------------------------------------------------ shapes (point tests)
const circle = (cx, cy, r) => ({ test: (x, y) => (x - cx) ** 2 + (y - cy) ** 2 <= r * r });
function polygon(points) {
  return {
    test: (x, y) => {
      let inside = false;
      for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
        const [xi, yi] = points[i], [xj, yj] = points[j];
        if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
      }
      return inside;
    },
  };
}
// thick segment with round caps
function line(x1, y1, x2, y2, w) {
  const r = w / 2;
  return {
    test: (x, y) => {
      const dx = x2 - x1, dy = y2 - y1;
      const len2 = dx * dx + dy * dy || 1;
      let t = ((x - x1) * dx + (y - y1) * dy) / len2;
      t = Math.max(0, Math.min(1, t));
      const px = x1 + t * dx, py = y1 + t * dy;
      return (x - px) ** 2 + (y - py) ** 2 <= r * r;
    },
  };
}
const polyline = (pts, w) => ({ test: (x, y) => pts.some((p, i) => i > 0 && line(pts[i - 1][0], pts[i - 1][1], p[0], p[1], w).test(x, y)) });
function ring(cx, cy, rOuter, rInner) { return { test: (x, y) => { const d = (x - cx) ** 2 + (y - cy) ** 2; return d <= rOuter * rOuter && d >= rInner * rInner; } }; }
// arc of a ring between angles (degrees, clockwise from +x, y down)
function arc(cx, cy, rOuter, rInner, a0, a1) {
  return {
    test: (x, y) => {
      const d = (x - cx) ** 2 + (y - cy) ** 2;
      if (d > rOuter * rOuter || d < rInner * rInner) return false;
      let a = (Math.atan2(y - cy, x - cx) * 180) / Math.PI; if (a < 0) a += 360;
      let s = ((a0 % 360) + 360) % 360, e = ((a1 % 360) + 360) % 360;
      return s <= e ? a >= s && a <= e : a >= s || a <= e;
    },
  };
}
function hexagon(cx, cy, r, pointyTop = true) {
  const pts = [];
  for (let i = 0; i < 6; i++) {
    const a = ((pointyTop ? -90 : 0) + i * 60) * (Math.PI / 180);
    pts.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]);
  }
  return polygon(pts);
}
function rotate(points, cx, cy, deg) {
  const a = (deg * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a);
  return points.map(([x, y]) => [cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c]);
}
const chevronUp = (cx, cy, w, h, t) => polyline([[cx - w / 2, cy + h / 2], [cx, cy - h / 2], [cx + w / 2, cy + h / 2]], t);
const F = (shape) => ({ op: 'fill', shape });
const C = (shape) => ({ op: 'cut', shape });

// ------------------------------------------------------------------ rasterize
function rasterize(ops, size, ss = 4) {
  const rgba = Buffer.alloc(size * size * 4);
  const scale = 100 / size, step = 1 / ss;
  for (let py = 0; py < size; py++) {
    for (let px = 0; px < size; px++) {
      let covered = 0;
      for (let sy = 0; sy < ss; sy++) for (let sx = 0; sx < ss; sx++) {
        const x = (px + (sx + 0.5) * step) * scale, y = (py + (sy + 0.5) * step) * scale;
        let inside = false;
        for (const { op, shape } of ops) {
          if (shape.test(x, y)) inside = op === 'fill';
        }
        if (inside) covered++;
      }
      const a = Math.round((covered / (ss * ss)) * 255);
      const i = (py * size + px) * 4;
      rgba[i] = 255; rgba[i + 1] = 255; rgba[i + 2] = 255; rgba[i + 3] = a;
    }
  }
  return rgba;
}

// ------------------------------------------------------------------ icon art (100x100, white = ink)
const sword = (ox = 0, oy = 0, deg = -45) => {
  // blade with a notch, crossguard and pommel, drawn upright then rotated about centre
  const pts = [[50, 8], [57, 20], [57, 62], [43, 62], [43, 20]];
  const guard = [[32, 62], [68, 62], [68, 69], [32, 69]];
  const grip = [[45, 69], [55, 69], [55, 86], [45, 86]];
  const pommel = circle(50, 90, 6);
  const r = (p) => rotate(p.map(([x, y]) => [x + ox, y + oy]), 50, 50, deg);
  return [F(polygon(r(pts))), F(polygon(r(guard))), F(polygon(r(grip))), F({ test: (x, y) => { const [[cx, cy]] = r([[50, 90]]); return (x - cx) ** 2 + (y - cy) ** 2 <= 36; } })];
};

const icons = {
  // Blades
  'blades.honed': [...sword(), F(line(30, 22, 40, 12, 4)), F(line(22, 34, 28, 28, 4))],
  'blades.quick': [...sword(0, 0, -45), F(line(12, 70, 34, 70, 5)), F(line(8, 82, 40, 82, 5)), F(line(16, 94, 30, 94, 5))],
  'blades.finisher': [...sword(-6, 6, -45), F(circle(72, 28, 15)), C(circle(72, 28, 9)), F(line(72, 4, 72, 12, 4)), F(line(90, 10, 84, 16, 4)), F(line(96, 28, 88, 28, 4)), F(line(90, 46, 84, 40, 4))],
  // Vitality: heart + plus, shield + plates
  'vitality.frame1': [F(circle(36, 36, 18)), F(circle(64, 36, 18)), F(polygon([[18, 44], [82, 44], [50, 88]])), C(polygon([[46, 36], [54, 36], [54, 68], [46, 68]])), C(polygon([[34, 48], [66, 48], [66, 56], [34, 56]]))],
  'vitality.frame2': [F(circle(36, 36, 18)), F(circle(64, 36, 18)), F(polygon([[18, 44], [82, 44], [50, 88]])), C(polygon([[46, 36], [54, 36], [54, 68], [46, 68]])), C(polygon([[34, 48], [66, 48], [66, 56], [34, 56]])), F(polygon([[8, 8], [14, 8], [14, 26], [8, 26]])), F(polygon([[18, 8], [24, 8], [24, 26], [18, 26]]))],
  'vitality.plating1': [F(polygon([[50, 8], [84, 20], [84, 52], [50, 92], [16, 52], [16, 20]])), C(polygon([[50, 20], [74, 29], [74, 50], [50, 78], [26, 50], [26, 29]])), F(polygon([[50, 30], [66, 36], [66, 50], [50, 68], [34, 50], [34, 36]]))],
  'vitality.plating2': [F(polygon([[50, 8], [84, 20], [84, 52], [50, 92], [16, 52], [16, 20]])), C(polygon([[50, 20], [74, 29], [74, 50], [50, 78], [26, 50], [26, 29]])), F(polygon([[50, 30], [66, 36], [66, 50], [50, 68], [34, 50], [34, 36]])), F(polygon([[8, 70], [14, 70], [14, 92], [8, 92]])), F(polygon([[18, 70], [24, 70], [24, 92], [18, 92]]))],
  // Jump
  'jump.spring': [F(chevronUp(50, 34, 44, 26, 9)), F(polygon([[22, 70], [78, 70], [78, 78], [22, 78]])), F(polygon([[30, 84], [70, 84], [70, 92], [30, 92]]))],
  'jump.double': [F(chevronUp(50, 26, 44, 24, 9)), F(chevronUp(50, 56, 44, 24, 9)), F(polygon([[22, 80], [78, 80], [78, 88], [22, 88]]))],
  'jump.thrusters': [F(polygon([[50, 6], [64, 26], [66, 56], [34, 56], [36, 26]])), F(polygon([[34, 40], [20, 62], [34, 58]])), F(polygon([[66, 40], [80, 62], [66, 58]])), F(polygon([[42, 60], [58, 60], [50, 94]])), C(polygon([[46, 60], [54, 60], [50, 78]])), F(circle(50, 30, 6)), C(circle(50, 30, 3))],
  // Dash
  'dash.unlock': [F(polygon([[34, 20], [70, 50], [34, 80]])), F(polygon([[56, 20], [92, 50], [56, 80]])), C(polygon([[56, 34], [70, 50], [56, 66]])), F(line(8, 36, 24, 36, 6)), F(line(4, 50, 20, 50, 6)), F(line(8, 64, 24, 64, 6))],
  'dash.strike': [F(polygon([[18, 22], [56, 50], [18, 78]])), F(line(4, 40, 14, 40, 5)), F(line(4, 60, 14, 60, 5)), F(circle(74, 50, 16)), C(circle(74, 50, 8)), F(polygon([[74, 18], [78, 30], [70, 30]])), F(polygon([[74, 82], [78, 70], [70, 70]])), F(polygon([[96, 50], [88, 54], [88, 46]])), F(polygon([[52, 50], [60, 54], [60, 46]]))],
  'dash.afterburner': [F(polygon([[14, 30], [48, 50], [14, 70]])), F(line(4, 44, 12, 44, 5)), F(line(4, 56, 12, 56, 5)), F(arc(70, 50, 24, 16, 300, 240)), F(polygon([[70, 14], [84, 26], [70, 38]])), F(line(70, 50, 70, 38, 5)), F(line(70, 50, 80, 56, 5))],
  // Mods: chip with pips
  ...Object.fromEntries([1, 2, 3, 4].map((n) => [
    `mods.slot${n}`,
    [
      F(polygon([[26, 26], [74, 26], [74, 74], [26, 74]])),
      C(polygon([[34, 34], [66, 34], [66, 66], [34, 66]])),
      ...[-12, 0, 12].flatMap((o) => [F(line(50 + o, 8, 50 + o, 26, 5)), F(line(50 + o, 74, 50 + o, 92, 5)), F(line(8, 50 + o, 26, 50 + o, 5)), F(line(74, 50 + o, 92, 50 + o, 5))]),
      ...Array.from({ length: n }, (_, i) => { const cols = n <= 2 ? n : 2; const row = Math.floor(i / cols), col = i % cols; const rows = Math.ceil(n / cols); const x = 50 + (col - (cols - 1) / 2) * 14, y = 50 + (row - (rows - 1) / 2) * 14; return F(circle(x, y, 4.5)); }),
    ],
  ])),
};

// Hexagon frame (pointy top) and fill, for the node cards.
const frames = {
  'hex-frame': [F(hexagon(50, 50, 47)), C(hexagon(50, 50, 42))],
  'hex-fill': [F(hexagon(50, 50, 43))],
  'hex-frame-bold': [F(hexagon(50, 50, 47)), C(hexagon(50, 50, 39))],
};

const root = process.argv[2];
const outDir = path.join(root, 'Assets/Game/New UI/Icons/Upgrades');
fs.mkdirSync(outDir, { recursive: true });
let written = 0;
for (const [name, ops] of Object.entries(icons)) { fs.writeFileSync(path.join(outDir, name + '.png'), png(128, 128, rasterize(ops, 128))); written++; }
for (const [name, ops] of Object.entries(frames)) { fs.writeFileSync(path.join(outDir, name + '.png'), png(256, 256, rasterize(ops, 256))); written++; }
console.log('wrote', written, 'icons to', outDir);

// Contact sheet: every generated icon inside the hexagon frame on the UI's navy, for review.
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const dir = path.join(process.argv[2], 'Assets/Game/New UI/Icons/Upgrades');

function inflatePng(buf) {
  let pos = 8; const idat = []; let w = 0, h = 0;
  while (pos < buf.length) {
    const len = buf.readUInt32BE(pos); const type = buf.toString('ascii', pos + 4, pos + 8);
    const data = buf.subarray(pos + 8, pos + 8 + len);
    if (type === 'IHDR') { w = data.readUInt32BE(0); h = data.readUInt32BE(4); }
    if (type === 'IDAT') idat.push(data);
    pos += 12 + len;
  }
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const px = Buffer.alloc(w * h * 4);
  for (let y = 0; y < h; y++) raw.copy(px, y * w * 4, y * (w * 4 + 1) + 1, (y + 1) * (w * 4 + 1));
  return { w, h, px };
}
function crc32(b) { let c, crc = 0xffffffff; for (let n = 0; n < b.length; n++) { c = (crc ^ b[n]) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; } return (crc ^ 0xffffffff) >>> 0; }
function chunk(t, d) { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc32(td)); return Buffer.concat([l, td, c]); }
function png(w, h, rgba) { const raw = Buffer.alloc((w * 4 + 1) * h); for (let y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4); } const ih = Buffer.alloc(13); ih.writeUInt32BE(w, 0); ih.writeUInt32BE(h, 4); ih[8] = 8; ih[9] = 6; return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ih), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]); }

const names = fs.readdirSync(dir).filter((f) => f.endsWith('.png') && !f.startsWith('hex-')).sort();
const frame = inflatePng(fs.readFileSync(path.join(dir, 'hex-frame.png')));
const fill = inflatePng(fs.readFileSync(path.join(dir, 'hex-fill.png')));
const cell = 150, cols = 6, rows = Math.ceil(names.length / cols);
const W = cols * cell, H = rows * cell;
const out = Buffer.alloc(W * H * 4);
const bg = [14, 19, 32]; const well = [6, 9, 18]; const cyan = [61, 232, 250];
for (let i = 0; i < W * H; i++) { out[i * 4] = bg[0]; out[i * 4 + 1] = bg[1]; out[i * 4 + 2] = bg[2]; out[i * 4 + 3] = 255; }
function blit(img, ox, oy, size, color) {
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const sx = Math.floor((x / size) * img.w), sy = Math.floor((y / size) * img.h);
    const a = img.px[(sy * img.w + sx) * 4 + 3] / 255; if (a <= 0) continue;
    const di = ((oy + y) * W + (ox + x)) * 4;
    for (let c = 0; c < 3; c++) out[di + c] = Math.round(out[di + c] * (1 - a) + color[c] * a);
  }
}
names.forEach((name, i) => {
  const ox = (i % cols) * cell + 15, oy = Math.floor(i / cols) * cell + 15;
  blit(fill, ox, oy, 120, well);
  blit(frame, ox, oy, 120, cyan);
  const icon = inflatePng(fs.readFileSync(path.join(dir, name)));
  blit(icon, ox + 27, oy + 27, 66, [230, 240, 255]);
});
const outPath = path.join(process.argv[3], 'upgrade-icon-sheet.png');
fs.writeFileSync(outPath, png(W, H, out));
console.log(names.map((n, i) => `${i}:${n.replace('.png', '')}`).join('  '));
console.log(outPath);

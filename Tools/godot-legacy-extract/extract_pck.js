// Extract the embedded GDPC pack from a Godot 4.x self-contained .exe
const fs = require('fs');
const path = require('path');

const exePath = process.argv[2];
const outDir = process.argv[3];

const buf = fs.readFileSync(exePath);
console.log('exe size', buf.length);

// Footer: [int64 pck_size][uint32 "GDPC"]
const tailMagic = buf.readUInt32LE(buf.length - 4);
const GDPC = 0x43504447;
if (tailMagic !== GDPC) throw new Error('no GDPC footer');
const pckSize = Number(buf.readBigUInt64LE(buf.length - 12));
const pckStart = buf.length - 12 - pckSize;
console.log('pck size', pckSize, 'start offset', pckStart);

let o = pckStart;
const headMagic = buf.readUInt32LE(o); o += 4;
if (headMagic !== GDPC) throw new Error('pck head magic mismatch at ' + pckStart);

const formatVersion = buf.readUInt32LE(o); o += 4;
const verMajor = buf.readUInt32LE(o); o += 4;
const verMinor = buf.readUInt32LE(o); o += 4;
const verPatch = buf.readUInt32LE(o); o += 4;
console.log('pack format', formatVersion, 'godot', verMajor + '.' + verMinor + '.' + verPatch);

let packFlags = 0;
let fileBase = 0;
if (formatVersion >= 2) {
  packFlags = buf.readUInt32LE(o); o += 4;
  fileBase = Number(buf.readBigUInt64LE(o)); o += 8;
}
console.log('pack flags', packFlags, 'file base', fileBase);
const PACK_DIR_ENCRYPTED = 1;
if (packFlags & PACK_DIR_ENCRYPTED) throw new Error('encrypted directory - needs key');

o += 16 * 4; // reserved

const fileCount = buf.readUInt32LE(o); o += 4;
console.log('file count', fileCount);

const entries = [];
for (let i = 0; i < fileCount; i++) {
  const pathLen = buf.readUInt32LE(o); o += 4;
  let p = buf.slice(o, o + pathLen).toString('utf8'); o += pathLen;
  p = p.replace(/\0+$/, '');
  const offset = Number(buf.readBigUInt64LE(o)); o += 8;
  const size = Number(buf.readBigUInt64LE(o)); o += 8;
  const md5 = buf.slice(o, o + 16).toString('hex'); o += 16;
  let flags = 0;
  if (formatVersion >= 2) { flags = buf.readUInt32LE(o); o += 4; }
  entries.push({ path: p, offset, size, md5, flags });
}

fs.mkdirSync(outDir, { recursive: true });
const manifest = [];
for (const e of entries) {
  // PACK_REL_FILEBASE (flag 2): file_base is relative to the pack start inside the exe.
  const base = (packFlags & 2) ? pckStart + fileBase : fileBase;
  const start = base + e.offset;
  const data = buf.slice(start, start + e.size);
  const rel = e.path.replace(/^res:\/\//, '');
  const dest = path.join(outDir, rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, data);
  manifest.push(`${e.path}\t${e.size}\tflags=${e.flags}`);
}
fs.writeFileSync(path.join(outDir, '_manifest.txt'), manifest.join('\n'), 'utf8');
console.log('extracted', entries.length, 'files to', outDir);

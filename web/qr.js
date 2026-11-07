// 用途：纯 JS 的 QR 码编码（字节模式 / 版本自动选择 / Reed-Solomon 纠错 / 掩码择优）。
// 状态：现行。不依赖任何 CDN 或第三方库，页面里 import 后调 qrDrawCanvas() 即可。
// 说明：表全部按 QR 标准（ISO/IEC 18004）硬编码，纠错等级默认 M（URL 这种短文本足够稳，且扫描器兼容）。

/* ── 常量表 ── */

// 每版本（1..40）的 [每块纠错码字数, [[块数, 每块数据码字数], ...]]，纠错等级 M
const ECC_M = [null,
  [10, [[1, 16]]], [16, [[1, 28]]], [26, [[1, 44]]], [18, [[2, 32]]], [24, [[2, 43]]],
  [16, [[4, 27]]], [18, [[4, 31]]], [22, [[2, 38], [2, 39]]], [22, [[3, 36], [2, 37]]],
  [26, [[4, 43], [1, 44]]], [30, [[1, 50], [4, 51]]], [22, [[6, 36], [2, 37]]],
  [22, [[8, 37], [1, 38]]], [24, [[4, 40], [5, 41]]], [24, [[5, 41], [5, 42]]],
  [28, [[7, 45], [3, 46]]], [28, [[10, 46], [1, 47]]], [26, [[9, 43], [4, 44]]],
  [26, [[3, 44], [11, 45]]], [26, [[3, 41], [13, 42]]], [26, [[17, 42]]],
  [28, [[17, 46]]], [28, [[4, 47], [14, 48]]], [28, [[6, 45], [14, 46]]],
  [28, [[8, 47], [13, 48]]], [28, [[19, 46], [4, 47]]], [28, [[22, 45], [3, 46]]],
  [28, [[3, 45], [23, 46]]], [28, [[21, 45], [7, 46]]], [28, [[19, 47], [10, 48]]],
  [28, [[2, 46], [29, 47]]], [28, [[10, 46], [23, 47]]], [28, [[14, 46], [21, 47]]],
  [28, [[14, 46], [23, 47]]], [28, [[12, 47], [26, 48]]], [28, [[6, 47], [34, 48]]],
  [28, [[29, 46], [14, 47]]], [28, [[13, 46], [32, 47]]], [28, [[40, 47], [7, 48]]],
  [28, [[18, 47], [31, 48]]]];

// 对齐图形中心坐标（版本 1..40，6 已含在每行里）
const ALIGN = [null,
  [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46],
  [6, 28, 50], [6, 30, 54], [6, 32, 58], [6, 34, 62], [6, 26, 46, 66], [6, 26, 48, 70],
  [6, 26, 50, 74], [6, 30, 54, 78], [6, 30, 56, 82], [6, 30, 58, 86], [6, 34, 62, 90],
  [6, 28, 50, 72, 94], [6, 26, 50, 74, 98], [6, 30, 54, 78, 102], [6, 28, 54, 80, 106],
  [6, 32, 58, 84, 110], [6, 30, 58, 86, 114], [6, 34, 62, 90, 118], [6, 26, 50, 74, 98, 122],
  [6, 30, 54, 78, 102, 126], [6, 26, 52, 78, 104, 130], [6, 30, 56, 82, 108, 134],
  [6, 34, 60, 86, 112, 138], [6, 30, 58, 86, 114, 142], [6, 34, 62, 90, 118, 146],
  [6, 30, 54, 78, 102, 126, 150], [6, 24, 50, 76, 102, 128, 154], [6, 28, 54, 80, 106, 132, 158],
  [6, 32, 58, 84, 110, 136, 162], [6, 26, 54, 82, 110, 138, 166], [6, 30, 58, 86, 114, 142, 170]];

// 15 位格式信息（BCH 掩码后），索引 = 纠错等级序号 * 16 + 掩码号；等级序号：M=0
const FORMAT_INFO = [
  21522, 20773, 24188, 23371, 17913, 16590, 20375, 19104, 30660, 29427, 32170, 30877, 26159, 25368, 27713, 26998,
  5769, 5054, 7399, 6608, 1890, 597, 3340, 2107, 13663, 12392, 16177, 14854, 9396, 8579, 11994, 11245];

// 18 位版本信息，索引 = 版本 - 7
const VERSION_INFO = [
  31892, 34236, 39577, 42195, 48118, 51042, 55367, 58893, 63784, 68472, 70749, 76311, 79154, 84390, 87683, 92361,
  96236, 102084, 102881, 110507, 110734, 117786, 119615, 126325, 127568, 133589, 136944, 141498, 145311, 150283,
  152622, 158308, 161089, 167017];

const ECC_LEVEL_M = 0;

/* ── GF(256) 与 RS 纠错 ── */

const GF_EXP = new Uint8Array(512);
const GF_LOG = new Uint8Array(256);
(function initGf() {
  let x = 1;
  for (let i = 0; i < 255; i++) {
    GF_EXP[i] = x;
    GF_LOG[x] = i;
    x <<= 1;
    if (x & 0x100) x ^= 0x11d;
  }
  for (let i = 255; i < 512; i++) GF_EXP[i] = GF_EXP[i - 255];
})();

function gfMul(a, b) {
  if (a === 0 || b === 0) return 0;
  return GF_EXP[GF_LOG[a] + GF_LOG[b]];
}

// 生成多项式（次数 = 纠错码字数）
function genPoly(degree) {
  let poly = [1];
  for (let i = 0; i < degree; i++) {
    const next = new Array(poly.length + 1).fill(0);
    for (let j = 0; j < poly.length; j++) {
      next[j] ^= poly[j];
      next[j + 1] ^= gfMul(poly[j], GF_EXP[i]);
    }
    poly = next;
  }
  return poly;
}

function rsRemainder(data, poly) {
  // poly 的长度是次数 + 1（首项系数为 1），rem 的长度是次数，
  // 所以取系数时从 1 开始：poly[i + 1] 才对应 rem[i] 所在的幂次。
  const rem = new Uint8Array(poly.length - 1);
  for (const byte of data) {
    const factor = byte ^ rem[0];
    rem.copyWithin(0, 1);
    rem[rem.length - 1] = 0;
    if (factor !== 0) {
      for (let i = 0; i < rem.length; i++) rem[i] ^= gfMul(poly[i + 1], factor);
    }
  }
  return rem;
}

/* ── 编码 ── */

function utf8(text) {
  if (typeof TextEncoder !== "undefined") return new TextEncoder().encode(text);
  const out = [];
  for (const ch of String(text)) {
    let cp = ch.codePointAt(0);
    if (cp < 0x80) out.push(cp);
    else if (cp < 0x800) out.push(0xc0 | (cp >> 6), 0x80 | (cp & 63));
    else if (cp < 0x10000) out.push(0xe0 | (cp >> 12), 0x80 | ((cp >> 6) & 63), 0x80 | (cp & 63));
    else out.push(0xf0 | (cp >> 18), 0x80 | ((cp >> 12) & 63), 0x80 | ((cp >> 6) & 63), 0x80 | (cp & 63));
  }
  return Uint8Array.from(out);
}

function charCountBits(version) {
  return version < 10 ? 8 : 16;
}

function dataCapacityBytes(version) {
  const table = ECC_M[version];
  let data = 0;
  for (const [blocks, perBlock] of table[1]) data += blocks * perBlock;
  return data;
}

function pickVersion(byteLength) {
  for (let v = 1; v <= 40; v++) {
    const needBits = 4 + charCountBits(v) + byteLength * 8;
    if (needBits <= dataCapacityBytes(v) * 8) return v;
  }
  throw new Error("内容太长，塞不进 QR 码");
}

function buildCodewords(bytes, version) {
  const table = ECC_M[version];
  const ecPerBlock = table[0];
  const capacity = dataCapacityBytes(version);
  const bits = [];
  const pushBits = (value, count) => { for (let i = count - 1; i >= 0; i--) bits.push((value >>> i) & 1); };
  pushBits(4, 4);                                   // 字节模式
  pushBits(bytes.length, charCountBits(version));
  for (const b of bytes) pushBits(b, 8);
  // 结束符最多 4 位，然后补到字节边界上的 0（已对齐时也补满 8 位，和主流实现一致）
  const capacityBits = capacity * 8;
  pushBits(0, Math.min(4, capacityBits - bits.length));
  const padBits = Math.min(capacityBits - bits.length, 8 - (bits.length % 8));
  if (padBits > 0) pushBits(0, padBits);
  const data = [];
  for (let i = 0; i < bits.length; i += 8) {
    let v = 0;
    for (let j = 0; j < 8; j++) v = (v << 1) | bits[i + j];
    data.push(v);
  }
  for (let pad = 0xec; data.length < capacity; pad ^= 0xec ^ 0x11) data.push(pad);

  // 分块 + 每块算纠错
  const blocks = [];
  let offset = 0;
  for (const [count, perBlock] of table[1]) {
    for (let i = 0; i < count; i++) {
      const chunk = data.slice(offset, offset + perBlock);
      offset += perBlock;
      const poly = genPoly(ecPerBlock);
      blocks.push({ data: chunk, ec: rsRemainder(chunk, poly) });
    }
  }
  // 交错
  const out = [];
  const maxData = Math.max(...blocks.map((b) => b.data.length));
  for (let i = 0; i < maxData; i++) {
    for (const b of blocks) if (i < b.data.length) out.push(b.data[i]);
  }
  for (let i = 0; i < ecPerBlock; i++) {
    for (const b of blocks) out.push(b.ec[i]);
  }
  return out;
}

/* ── 矩阵 ── */

const MASK_FN = [
  (x, y) => (x + y) % 2 === 0,
  (x, y) => y % 2 === 0,
  (x, y) => x % 3 === 0,
  (x, y) => (x + y) % 3 === 0,
  (x, y) => (Math.floor(y / 2) + Math.floor(x / 3)) % 2 === 0,
  (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
];

function buildMatrix(codewords, version, mask) {
  const size = version * 4 + 17;
  const mod = new Uint8Array(size * size);      // 1 = 深色
  const fn = new Uint8Array(size * size);       // 1 = 功能图形（不参与掩码/数据）
  const at = (x, y) => y * size + x;
  const set = (x, y, dark, isFn) => {
    mod[at(x, y)] = dark ? 1 : 0;
    if (isFn) fn[at(x, y)] = 1;
  };

  // 定位图形 + 分隔符
  const placeFinder = (cx, cy) => {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const x = cx + dx, y = cy + dy;
        if (x < 0 || y < 0 || x >= size || y >= size) continue;
        const dist = Math.max(Math.abs(dx), Math.abs(dy));
        set(x, y, dist !== 2 && dist !== 4, true);
      }
    }
  };
  placeFinder(3, 3);
  placeFinder(size - 4, 3);
  placeFinder(3, size - 4);

  // 时序图形
  for (let i = 8; i < size - 8; i++) {
    set(6, i, i % 2 === 0, true);
    set(i, 6, i % 2 === 0, true);
  }

  // 对齐图形
  const pos = ALIGN[version];
  for (let i = 0; i < pos.length; i++) {
    for (let j = 0; j < pos.length; j++) {
      if ((i === 0 && j === 0) || (i === 0 && j === pos.length - 1) || (i === pos.length - 1 && j === 0)) continue;
      const cx = pos[j], cy = pos[i];
      for (let dy = -2; dy <= 2; dy++) {
        for (let dx = -2; dx <= 2; dx++) {
          set(cx + dx, cy + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1, true);
        }
      }
    }
  }

  // 格式信息：两份 15 位副本（低 8 位在前，高位另一处），第 6 行/列是时序图形要跳过。
  // 摆放按标准：左上竖列 bit0..7、左下行 bit8..14；左上横行 bit8..14、右上横 bit0..7。
  const fmt = FORMAT_INFO[ECC_LEVEL_M * 16 + mask];
  const fmtBit = (i) => ((fmt >>> i) & 1) === 1;
  const skip6 = (i) => (i < 6 ? i : i + 1);
  for (let i = 0; i < 8; i++) {
    set(8, skip6(i), fmtBit(i), true);              // 左上竖列（bit0..7）
    set(skip6(i), 8, fmtBit(14 - i), true);         // 左上横行（bit14..7）
    set(size - 1 - i, 8, fmtBit(i), true);          // 右上横行（bit0..7）
    set(8, size - 1 - i, fmtBit(14 - i), true);     // 左下竖列（bit14..7）
  }
  for (let i = 8; i < 15; i++) {
    set(8, size - 15 + i, fmtBit(i), true);         // 左下竖列剩下的 bit8..14
    if (i > 8) set(14 - i, 8, fmtBit(i), true);     // 左上横行剩下的 bit9..14（bit8 落在 x=7）
  }
  set(7, 8, fmtBit(8), true);
  set(8, size - 8, true, true);                    // 固定深色模块

  // 版本信息（v7 起）
  if (version >= 7) {
    const ver = VERSION_INFO[version - 7];
    for (let i = 0; i < 18; i++) {
      const bit = ((ver >>> i) & 1) === 1;
      const a = size - 11 + (i % 3), b = Math.floor(i / 3);
      set(a, b, bit, true);
      set(b, a, bit, true);
    }
  }

  // 数据：之字形从右下往上；跳过第 6 列（竖向时序）后左右列方向会互换，按标准逐列判断
  const bits = [];
  for (const b of codewords) for (let i = 7; i >= 0; i--) bits.push((b >>> i) & 1);
  let idx = 0;
  for (let col = size - 1; col >= 1; col -= 2) {
    const right = col <= 6 ? col - 1 : col;      // 跳过第 6 列后，后面的列对整体左移一位
    for (let vert = 0; vert < size; vert++) {
      for (let z = 0; z < 2; z++) {
        const x = right - z;
        const upward = (((right & 2) === 0) !== (x < 6));
        const y = upward ? size - 1 - vert : vert;
        if (fn[at(x, y)]) continue;
        const raw = idx < bits.length ? bits[idx] : 0;
        idx += 1;
        mod[at(x, y)] = raw ^ (MASK_FN[mask](x, y) ? 1 : 0);
      }
    }
  }
  return { size, mod };
}

/* ── 掩码择优（标准 4 条罚分） ── */

function penalty(modules, size) {
  let score = 0;
  const at = (x, y) => modules[y * size + x];
  // 规则 1：同色连续 5 个以上
  for (const horizontal of [true, false]) {
    for (let a = 0; a < size; a++) {
      let run = 1;
      for (let b = 1; b < size; b++) {
        const cur = horizontal ? at(b, a) : at(a, b);
        const prev = horizontal ? at(b - 1, a) : at(a, b - 1);
        if (cur === prev) {
          run += 1;
        } else {
          if (run >= 5) score += 3 + (run - 5);
          run = 1;
        }
      }
      if (run >= 5) score += 3 + (run - 5);
    }
  }
  // 规则 2：2×2 同色块
  for (let y = 0; y + 1 < size; y++) {
    for (let x = 0; x + 1 < size; x++) {
      const v = at(x, y);
      if (v === at(x + 1, y) && v === at(x, y + 1) && v === at(x + 1, y + 1)) score += 3;
    }
  }
  // 规则 3：6 长假冒定位图形
  const PATTERN_A = [1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0];
  const PATTERN_B = [0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1];
  const hits = (getter, max) => {
    let hit = 0;
    for (let i = 0; i + 11 <= max; i++) {
      let okA = true, okB = true;
      for (let k = 0; k < 11; k++) {
        const v = getter(i + k);
        if (v !== PATTERN_A[k]) okA = false;
        if (v !== PATTERN_B[k]) okB = false;
        if (!okA && !okB) break;
      }
      if (okA) hit += 1;
      if (okB) hit += 1;
    }
    return hit;
  };
  for (let y = 0; y < size; y++) score += 40 * hits((x) => at(x, y), size);
  for (let x = 0; x < size; x++) score += 40 * hits((y) => at(x, y), size);
  // 规则 4：深浅比例偏离 50%
  let dark = 0;
  for (let i = 0; i < modules.length; i++) dark += modules[i];
  const ratio = (dark * 100) / (size * size);
  score += Math.floor(Math.abs(ratio - 50) / 5) * 10;
  return score;
}

/**
 * 生成二维码矩阵。
 * @param {string} text 内容（按 UTF-8 字节编码）
 * @param {{mask?: number, version?: number}} [opts] 指定掩码 / 版本（不指定则自动择优）
 * @returns {{size:number, modules:Uint8Array, version:number, mask:number}}
 */
export function qrMatrix(text, opts) {
  const options = opts || {};
  const bytes = utf8(text == null ? "" : String(text));
  if (!bytes.length) throw new Error("内容是空的");
  const version = options.version || pickVersion(bytes.length);
  const codewords = buildCodewords(bytes, version);
  if (options.mask != null) {
    const only = buildMatrix(codewords, version, options.mask);
    return { size: only.size, modules: only.mod, version, mask: options.mask };
  }
  let best = null;
  for (let mask = 0; mask < 8; mask++) {
    const built = buildMatrix(codewords, version, mask);
    const score = penalty(built.mod, built.size);
    if (!best || score < best.score) best = { mask, size: built.size, mod: built.mod, score };
  }
  return { size: best.size, modules: best.mod, version, mask: best.mask };
}

/** 把二维码画进 canvas（就地设置尺寸；边框 4 个模块的静区） */
export function qrDrawCanvas(text, canvas, opts) {
  const options = opts || {};
  const quiet = options.quiet == null ? 4 : options.quiet;
  const target = options.size || 220;
  const m = qrMatrix(text, options);
  const total = m.size + quiet * 2;
  const scale = Math.max(1, Math.floor(target / total));
  const pixels = total * scale;
  canvas.width = pixels;
  canvas.height = pixels;
  canvas.style.imageRendering = "pixelated";
  const ctx = canvas.getContext("2d");
  ctx.fillStyle = options.light || "#ffffff";
  ctx.fillRect(0, 0, pixels, pixels);
  ctx.fillStyle = options.dark || "#12141a";
  for (let y = 0; y < m.size; y++) {
    for (let x = 0; x < m.size; x++) {
      if (m.modules[y * m.size + x]) {
        ctx.fillRect((x + quiet) * scale, (y + quiet) * scale, scale, scale);
      }
    }
  }
  return m;
}

/** 返回 SVG 字符串（不想用 canvas 时的备选） */
export function qrSvg(text, opts) {
  const options = opts || {};
  const quiet = options.quiet == null ? 4 : options.quiet;
  const m = qrMatrix(text, options);
  const total = m.size + quiet * 2;
  let path = "";
  for (let y = 0; y < m.size; y++) {
    for (let x = 0; x < m.size; x++) {
      if (m.modules[y * m.size + x]) path += "M" + (x + quiet) + " " + (y + quiet) + "h1v1h-1z";
    }
  }
  return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + total + " " + total
    + '" shape-rendering="crispEdges"><rect width="' + total + '" height="' + total
    + '" fill="' + (options.light || "#fff") + '"/><path fill="' + (options.dark || "#12141a")
    + '" d="' + path + '"/></svg>';
}

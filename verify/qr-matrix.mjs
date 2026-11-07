// QR 编码器回归：把 web/qr.js 生成的矩阵与 OpenCV 解码结果对齐。
// 只依赖 OpenCV（pip install opencv-python-headless），跑法：
//   node verify/qr-matrix.mjs        # 生成矩阵到 verify/_run/qr.json
//   python verify/qr-check.py        # 逐位对比 + 实际解码
import fs from "node:fs";
import path from "node:path";

import { fileURLToPath } from "node:url";
const ROOT = fileURLToPath(new URL("../", import.meta.url));
const src = fs.readFileSync(path.join(ROOT, "web", "qr.js"), "utf8");
const mod = await import("data:text/javascript;base64," + Buffer.from(src).toString("base64"));

const cases = [
  "a",
  "http://192.168.1.9:8790/scan",
  "http://10.0.0.123:8790/scan",
  "http://127.0.0.1:8790/scan",
  "https://example.com/a/b?c=d&e=f#g",
  "中文测试：机台局域网地址",
  "mai mai maimai 打歌点歌台",
  "x".repeat(60),
  "x".repeat(120),
  "x".repeat(300),
  "x".repeat(700),
  "http://192.168.1.9:8790/scan?hint=" + "点歌".repeat(30),
];

const out = [];
for (const text of cases) {
  const m = mod.qrMatrix(text);
  out.push({
    text, version: m.version, mask: m.mask, size: m.size,
    rows: Array.from({ length: m.size }, (_, y) =>
      Array.from({ length: m.size }, (_, x) => (m.modules[y * m.size + x] ? "1" : "0")).join("")),
  });
}
// 8 个掩码全走一遍
for (let mask = 0; mask < 8; mask++) {
  const text = "http://192.168.3.7:8790/scan";
  const m = mod.qrMatrix(text, { mask });
  out.push({
    text, version: m.version, mask, size: m.size,
    rows: Array.from({ length: m.size }, (_, y) =>
      Array.from({ length: m.size }, (_, x) => (m.modules[y * m.size + x] ? "1" : "0")).join("")),
  });
}

const dir = path.join(ROOT, "verify", "_run");
fs.mkdirSync(dir, { recursive: true });
fs.writeFileSync(path.join(dir, "qr.json"), JSON.stringify(out), "utf8");
console.log("已生成 " + out.length + " 个矩阵；版本：" + [...new Set(out.map((o) => o.version))].join(","));

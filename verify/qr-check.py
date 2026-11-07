"""QR 编码器验收：逐位与 segno 对比（同版本/掩码/字节模式），再用 OpenCV 实际解码。

跑法：
  node verify/qr-matrix.mjs
  python verify/qr-check.py
依赖：pip install segno opencv-python-headless
"""
import json
import os
import sys

import cv2
import numpy as np
import segno

HERE = os.path.dirname(os.path.abspath(__file__))
CASES = json.load(open(os.path.join(HERE, "_run", "qr.json"), encoding="utf-8"))

ok = True

print("── 逐位对比 segno（同 version / mask / 字节模式）──")
for c in CASES:
    q = segno.make_qr(c["text"], error="m", version=c["version"], mask=c["mask"],
                      mode="byte", encoding="utf-8", boost_error=False)
    ref = [[bool(v) for v in row] for row in q.matrix]
    n = len(ref)
    js = [[ch == "1" for ch in row] for row in c["rows"]]
    diff = sum(1 for y in range(n) for x in range(n) if ref[y][x] != js[y][x])
    if diff:
        ok = False
        print("  差异 v%-3d mask%d n=%3d diff=%d" % (c["version"], c["mask"], n, diff))
print("  全部一致" if ok else "  有差异")

print("── OpenCV 实际解码 ──")
fails = []
for c in CASES:
    n = c["size"]
    quiet, scale = 4, 8
    img = np.full(((n + 2 * quiet) * scale,) * 2, 255, dtype=np.uint8)
    for y in range(n):
        for x in range(n):
            if c["rows"][y][x] == "1":
                img[(y + quiet) * scale:(y + quiet + 1) * scale,
                    (x + quiet) * scale:(x + quiet + 1) * scale] = 0
    data, _, _ = cv2.QRCodeDetector().detectAndDecode(img)
    if data != c["text"]:
        fails.append(c)

if fails:
    # 逐位已经和 segno 一致，还扫不出来只可能是 OpenCV 自己的检测器挑食：
    # 把同样的内容交给 segno 直接出 PNG 再解一次，segno 也解不出就算 OpenCV 的锅。
    real = []
    for c in fails:
        q = segno.make_qr(c["text"], error="m", version=c["version"], mask=c["mask"],
                          mode="byte", encoding="utf-8", boost_error=False)
        n = q.symbol_size(border=4)[0]
        p = os.path.join(HERE, "_run", "probe.png")
        q.save(p, scale=8, border=4)
        data, _, _ = cv2.QRCodeDetector().detectAndDecode(cv2.imread(p, cv2.IMREAD_GRAYSCALE))
        print("  v%d mask%d：我们的码扫不出，segno 自己的 PNG %s"
              % (c["version"], c["mask"], "也扫不出（OpenCV 的锅）" if data != c["text"] else "扫得出（我们的锅）"))
        if data == c["text"]:
            real.append(c)
            ok = False
    if not real:
        print("  %d 个由 OpenCV 检测器导致，矩阵本身逐位正确" % len(fails))
else:
    print("  全部扫得出来")

sys.exit(0 if ok else 1)

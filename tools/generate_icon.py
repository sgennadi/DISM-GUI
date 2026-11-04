#!/usr/bin/env python3
"""Create the DISM GUI Windows icon (multi-resolution ICO), no third-party dependencies."""
from __future__ import annotations
import math
from pathlib import Path
import struct
import sys
import zlib

SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)


def _blend(a: float, b: float, t: float) -> float:
    return a * (1.0 - t) + b * t


def _rounded_box(x: float, y: float, cx: float, cy: float, hx: float, hy: float, radius: float) -> bool:
    qx = abs(x - cx) - hx + radius
    qy = abs(y - cy) - hy + radius
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) < radius


def _shade(x: float, y: float):
    # Transparent outside the squircle: works on light and dark taskbars.
    if not _rounded_box(x, y, .5, .5, .46, .46, .197):
        return (0, 0, 0, 0)

    # Navy, royal blue, cyan edge and purple upper-right glow.
    t = min(1.0, max(0.0, .64 * x + .36 * y))
    red = _blend(13, 42, t)
    green = _blend(29, 107, t)
    blue = _blend(79, 223, t)
    glow = max(0, 1.0 - math.hypot(x - .87, y - .18) / .53) ** 1.45
    red += glow * 53
    green -= glow * 18
    blue += glow * 18
    cyan = max(0, 1.0 - math.hypot(x - .15, y - .87) / .63) ** 2
    green += cyan * 56
    blue += cyan * 18

    # Crisp white D monogram, legible even at 16x16.
    stem = .266 <= x <= .413 and .238 <= y <= .762
    outer = ((x - .457) / .325) ** 2 + ((y - .5) / .278) ** 2 <= 1.0
    inner = ((x - .457) / .202) ** 2 + ((y - .5) / .159) ** 2 <= 1.0
    mark = stem or (outer and not inner and x > .349)
    if mark:
        red, green, blue = 248, 252, 255

    # Cyan image-layer markers inside the monogram.
    for cy, width in ((.446, .135), (.505, .168), (.564, .125)):
        if _rounded_box(x, y, .502, cy, width / 2, .010, .009):
            red, green, blue = 58, 214, 236

    if .20 <= y <= .21 and .26 < x < .58:
        red, green, blue = (min(255, red + 8), min(255, green + 8), min(255, blue + 8))

    return (int(max(0, min(255, red))),
            int(max(0, min(255, green))),
            int(max(0, min(255, blue))), 255)


def _render(size: int) -> bytes:
    # Supersampled vector-style rasterization for crisp small Windows icons.
    sub = 3
    rows = bytearray()
    for j in range(size):
        rows.append(0)  # PNG filter: None
        for i in range(size):
            r = g = b = a = 0
            for sy in range(sub):
                for sx in range(sub):
                    x = (i + (sx + .5) / sub) / size
                    y = (j + (sy + .5) / sub) / size
                    p = _shade(x, y)
                    # Premultiply before averaging, avoid dark transparent edges.
                    r += p[0] * p[3]
                    g += p[1] * p[3]
                    b += p[2] * p[3]
                    a += p[3]
            samples = sub * sub
            aa = int(round(a / samples))
            if a:
                rows.extend((int(round(r / a)), int(round(g / a)),
                             int(round(b / a)), aa))
            else:
                rows.extend((0, 0, 0, 0))
    return _png(size, size, bytes(rows))


def _chunk(tag: bytes, data: bytes) -> bytes:
    return (struct.pack('>I', len(data)) + tag + data
            + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff))


def _png(w: int, h: int, pixel_rows: bytes) -> bytes:
    return (b'\x89PNG\r\n\x1a\n'
            + _chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
            + _chunk(b'IDAT', zlib.compress(pixel_rows, level=9))
            + _chunk(b'IEND', b''))


def create_ico(target: Path):
    images = [_render(s) for s in SIZES]
    header = struct.pack('<HHH', 0, 1, len(SIZES))
    offset = 6 + len(SIZES) * 16
    directory = bytearray()
    for size, frame in zip(SIZES, images):
        directory += struct.pack('<BBBBHHII',
                                 0 if size == 256 else size,
                                 0 if size == 256 else size,
                                 0, 0, 1, 32, len(frame), offset)
        offset += len(frame)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(header + directory + b''.join(images))
    if target.stat().st_size < 5000:
        raise ValueError('Unexpectedly small icon resource')
    print(f'Generated {target} ({target.stat().st_size:,} bytes; sizes: {SIZES})')


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: generate_icon.py OUTPUT.ico')
    create_ico(Path(sys.argv[1]))

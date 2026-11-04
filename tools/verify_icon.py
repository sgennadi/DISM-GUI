#!/usr/bin/env python3
"""Fail CI if the Windows EXE is missing the actual multi-resolution application icon.

The 256px PNG payload is stored verbatim as an RT_ICON resource in a PE
executable. Verifying it avoids treating a title-bar-only or generic EXE
icon as a successful application icon.
"""
from pathlib import Path
import struct
import sys

PNG = b"\x89PNG\r\n\x1a\n"


def find_image_payloads(ico_file: Path) -> dict[int, bytes]:
    ico = ico_file.read_bytes()
    if len(ico) < 6:
        raise ValueError("ICO header missing")
    zero, kind, count = struct.unpack_from("<HHH", ico)
    if zero != 0 or kind != 1 or count < 5:
        raise ValueError("Not a valid multi-resolution ICO")
    payloads = {}
    for index in range(count):
        offset = 6 + index * 16
        if offset + 16 > len(ico):
            raise ValueError("ICO directory is truncated")
        w, h, colors, reserved, planes, bits, nbytes, start = struct.unpack_from(
            "<BBBBHHII", ico, offset
        )
        width = w or 256
        height = h or 256
        if width != height or nbytes < 100 or start + nbytes > len(ico):
            raise ValueError(f"Invalid ICO image at index {index}")
        data = ico[start: start + nbytes]
        if width in payloads:
            raise ValueError(f"Duplicate image size {width}")
        if not data.startswith(PNG):
            raise ValueError(f"Image at {width}px is not PNG-encoded")
        payloads[width] = data
    if {16, 32, 48, 256} - set(payloads):
        raise ValueError("ICO is missing required Windows icon sizes")
    return payloads


def verify(icon_path: Path, exe_path: Path) -> None:
    images = find_image_payloads(icon_path)
    exe = exe_path.read_bytes()
    if not exe.startswith(b"MZ") or len(exe) < 100_000:
        raise ValueError("Not a published Windows executable")
    # The .ico directory itself is not in the PE. The PNG frames are copied
    # into separate RT_ICON resources; verify several independently.
    for size in (16, 32, 48, 256):
        frame = images[size]
        if frame not in exe:
            raise ValueError(
                f"The {size}px App.ico frame was not embedded in the EXE "
                "(missing native ApplicationIcon)."
            )
    print(f"PASS: {exe_path.name} includes App.ico images at 16, 32, 48 and 256 px.")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: verify_icon.py APP.ico PUBLISHED.exe")
    verify(Path(sys.argv[1]), Path(sys.argv[2]))

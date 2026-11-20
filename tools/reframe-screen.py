"""Cut the island's camera out of a real-screen recording: screen.mp4 + camera.json -> stage.mp4 (1000x860, 60 fps).

    DynamicIsland.exe --tiktok-screen <folder>
    python tools/reframe-screen.py <folder> [--fixed]

--fixed: no camera moves, one 1:1 view at the top centre of the screen (where the island lives).

camera.json holds, per rendered frame, [t, x, y, w, h]: the camera's view in monitor pixels. Only the area all
views cover is decoded; each output frame is that view, eased between log entries, scaled to the stage.
"""
import bisect
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image

folder = sys.argv[1]
OUT_W, OUT_H, FPS = 1000, 860, 60
cam = json.load(open(os.path.join(folder, "camera.json"), encoding="utf-8"))
frames = cam["frames"]
mon_w, mon_h = (int(v) for v in cam["monitor"])
src = os.path.join(folder, "screen.mp4")
vw, vh = (int(v) for v in subprocess.run(
    ["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", src],
    capture_output=True, text=True, check=True).stdout.strip().split(","))
sx, sy = vw / mon_w, vh / mon_h   # the recording may be scaled from the monitor


def clamp_view(x, y, w, h):
    w, h = min(w * sx, vw), min(h * sy, vh)
    x, y = min(max(x * sx, 0), vw - w), min(max(y * sy, 0), vh - h)
    return x, y, w, h


if "--fixed" in sys.argv:
    frames = [[f[0], mon_w / 2 - OUT_W / 2, 0, OUT_W, OUT_H] for f in frames]
views = [clamp_view(*f[1:]) for f in frames]
times = [f[0] for f in frames]
x0 = int(max(0, min(v[0] for v in views))) & ~1
y0 = int(max(0, min(v[1] for v in views))) & ~1
x1 = min(vw, int(max(v[0] + v[2] for v in views)) + 2)
y1 = min(vh, int(max(v[1] + v[3] for v in views)) + 2)
cw, ch = (x1 - x0) & ~1, (y1 - y0) & ~1
print(f"screen {vw}x{vh}, decoding {cw}x{ch} at {x0},{y0}; {len(frames)} camera frames")


def view_at(t):
    i = bisect.bisect_left(times, t)
    if i <= 0:
        return views[0]
    if i >= len(times):
        return views[-1]
    a, b = views[i - 1], views[i]
    f = (t - times[i - 1]) / max(times[i] - times[i - 1], 1e-6)
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(4))


dec = subprocess.Popen(["ffmpeg", "-v", "error", "-i", src, "-vf", f"crop={cw}:{ch}:{x0}:{y0}", "-r", str(FPS),
                        "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE)
enc = subprocess.Popen(["ffmpeg", "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{OUT_W}x{OUT_H}",
                        "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "medium", "-crf", "14",
                        "-pix_fmt", "yuv420p", os.path.join(folder, "stage.mp4")], stdin=subprocess.PIPE)
size = cw * ch * 3
n = 0
while True:
    buf = dec.stdout.read(size)
    if len(buf) < size:
        break
    x, y, w, h = view_at(n / FPS)
    img = Image.frombuffer("RGB", (cw, ch), buf, "raw", "RGB", 0, 1)
    box = (x - x0, y - y0, x - x0 + w, y - y0 + h)
    enc.stdin.write(img.resize((OUT_W, OUT_H), Image.LANCZOS, box=box).tobytes())
    n += 1
enc.stdin.close()
enc.wait()
dec.wait()
print("ok", n, "frames")

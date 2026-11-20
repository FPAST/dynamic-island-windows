"""A TikTok from a filmed stage: background + stage (rounded) + scene titles + word-by-word subtitles + the voice.

    DynamicIsland.exe --tiktok <folder> --cover
    python transcribe.py <voice.mp3> <words.json>       (faster-whisper: each word with its time)
    python tools/compose-tiktok.py <folder> <voice.mp3> <words.json> <out.mp4> [link]

A link (the Telegram channel) sits under the stage the whole time and grows big on the "end" title.

The subtitles take the recogniser's times but the script's spelling (FIX / JOIN below).
"""
import json
import os
import subprocess
import sys

folder, voice, words_path, out = sys.argv[1:5]
link = sys.argv[5] if len(sys.argv) > 5 else ""
meta = json.load(open(os.path.join(folder, "captions.json"), encoding="utf-8"))
words = json.load(open(words_path, encoding="utf-8"))["words"]
caps, total = meta["captions"], meta["total"]
dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", voice],
                           capture_output=True, text=True, check=True).stdout)
length = min(total, dur + 0.6)

# what the recogniser heard -> how the script spells it
FIX = {"Атол,": "Атолл —", "Атол": "Атолл", "бебунок,": "бегунок,", "строгать.": "трогать.", "Тяните": "Тянете"}
JOIN = [("2", ".0"), ("по", "-настоящему.")]

toks = []
for w in words:
    t = FIX.get(w["w"], w["w"])
    if toks and any(toks[-1]["w"] == a and t == b for a, b in JOIN):
        toks[-1] = {"w": toks[-1]["w"] + t, "s": toks[-1]["s"], "e": w["e"]}
    else:
        toks.append({"w": t, "s": w["s"], "e": w["e"]})

# lines of up to 3 words / 20 letters, broken after punctuation
lines, cur = [], []
for t in toks:
    cur.append(t)
    if t["w"][-1] in ".,!?—" or len(cur) == 3 or sum(len(x["w"]) + 1 for x in cur) > 20:
        lines.append(cur)
        cur = []
if cur:
    lines.append(cur)


def ts(sec):
    h, rem = divmod(max(sec, 0), 3600)
    m, s = divmod(rem, 60)
    return f"{int(h)}:{int(m):02d}:{s:05.2f}"


ACCENT = "&H007AA2F0&"   # #F0A27A, the brand's warm orange (ASS is BGR)
ass = [
    "[Script Info]", "ScriptType: v4.00+", "PlayResX: 1080", "PlayResY: 1920", "WrapStyle: 2", "",
    "[V4+ Styles]",
    "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, "
    "ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
    "Style: Sub,Segoe UI Black,82,&H00FFFFFF,&H00FFFFFF,&H00101010,&H80000000,0,0,0,0,100,100,0,0,1,7,2,2,60,60,520,1",
    "Style: Link,Segoe UI Semibold,42,&H00E6DCDC,&H00FFFFFF,&H00101010,&H00000000,0,0,0,0,100,100,0,0,1,3,0,2,60,60,352,1",
    "Style: LinkBig,Segoe UI Black,70,&H007AA2F0,&H00FFFFFF,&H00101010,&H00000000,0,0,0,0,100,100,0,0,1,6,0,5,60,60,0,1",
    "",
    "[Events]", "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text",
]
for li, line in enumerate(lines):
    line_end = lines[li + 1][0]["s"] if li + 1 < len(lines) else line[-1]["e"] + 0.6
    line_end = min(line_end, line[-1]["e"] + 0.9)
    for wi, w in enumerate(line):
        start = w["s"]
        end = line[wi + 1]["s"] if wi + 1 < len(line) else line_end
        text = " ".join(
            ("{\\c" + ACCENT + "\\fscx112\\fscy112}" + x["w"].upper() + "{\\c&H00FFFFFF&\\fscx100\\fscy100}") if j == wi else x["w"].upper()
            for j, x in enumerate(line))
        # the line pops in with its first word
        pop = "{\\fad(60,0)\\t(0,90,\\fscx104\\fscy104)\\t(90,180,\\fscx100\\fscy100)}" if wi == 0 else ""
        ass.append(f"Dialogue: 0,{ts(start)},{ts(end)},Sub,,0,0,0,,{pop}{text}")
if link:
    end_t = next((c["t"] for c in caps if c["key"] == "end"), length)
    ass.append(f"Dialogue: 1,{ts(0)},{ts(end_t)},Link,,0,0,0,,Telegram  ·  {link}")
    # the end: big in the middle of the stage, popping in
    big = r"{\pos(540,1180)\fad(200,0)\fscx70\fscy70\t(0,260,\fscx108\fscy108)\t(260,420,\fscx100\fscy100)}TELEGRAM\N"
    ass.append(f"Dialogue: 1,{ts(end_t)},{ts(length + 1)},LinkBig,,0,0,0,," + big + link)
open(os.path.join(folder, "subs.ass"), "w", encoding="utf-8-sig").write("\n".join(ass) + "\n")

STAGE_X, STAGE_Y, CAP_Y = 40, 640, 268
args = ["ffmpeg", "-y", "-loglevel", "error",
        "-loop", "1", "-framerate", "60", "-i", "bg.png",
        "-i", "stage.mp4",
        "-loop", "1", "-framerate", "60", "-i", "mask.png",
        "-i", os.path.abspath(voice)]
for c in caps:
    args += ["-loop", "1", "-framerate", "60", "-i", f"cap_{c['key']}.png"]

f = ["[1:v]format=rgba[sv]", "[2:v]format=gray[m]", "[sv][m]alphamerge[st]",
     f"[0:v][st]overlay={STAGE_X}:{STAGE_Y}:shortest=1[b0]"]
for i, c in enumerate(caps):
    t0 = c["t"]
    t1 = caps[i + 1]["t"] if i + 1 < len(caps) else length + 1
    f.append(f"[{4 + i}:v]format=rgba,fade=t=in:st={t0:.3f}:d=0.35:alpha=1,"
             f"fade=t=out:st={max(t0, t1 - 0.18):.3f}:d=0.18:alpha=1[c{i}]")
    y = f"{CAP_Y}+44*pow(max(0\\,1-(t-{t0:.3f})/0.4)\\,3)"
    f.append(f"[b{i}][c{i}]overlay=0:'{y}':enable='between(t,{t0:.3f},{t1:.3f})'[b{i + 1}]")
f.append(f"[b{len(caps)}]ass=subs.ass[v]")
open(os.path.join(folder, "filter-tiktok.txt"), "w", encoding="utf-8").write(";\n".join(f))
args += ["-/filter_complex", "filter-tiktok.txt", "-map", "[v]", "-map", "3:a",
         "-t", f"{length:.2f}", "-r", "60", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
         "-pix_fmt", "yuv420p", "-profile:v", "high", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart",
         os.path.abspath(out)]
subprocess.run(args, check=True, cwd=folder)
print("ok", out, f"{length:.2f} s,", len(lines), "subtitle lines")

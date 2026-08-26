from __future__ import annotations
from pathlib import Path
import json, subprocess, tempfile
from PIL import Image, ImageDraw
from .discover import collect


def main():
    trends = collect()
    if len(trends) < 5:
        raise RuntimeError(f"Discovery smoke failed: only {len(trends)} candidates")
    print(f"DISCOVERY_OK candidates={len(trends)} first={trends[0]['title'][:120]}")

    root = Path(__file__).resolve().parents[1]
    smoke = root / "output" / "smoke"
    smoke.mkdir(parents=True, exist_ok=True)

    frame = smoke / "frame.png"
    img = Image.new("RGB", (1080, 1920), (7, 10, 18))
    d = ImageDraw.Draw(img)
    d.rectangle((80, 180, 1000, 1740), outline=(100, 150, 255), width=8)
    d.text((120, 820), "D7 MEDIA CLOUD\nRENDER SMOKE TEST", fill="white", spacing=20)
    img.save(frame)

    audio = smoke / "voice.wav"
    subprocess.run([
        "ffmpeg", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
        "-c:a", "pcm_s16le", str(audio)
    ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    output = smoke / "final.mp4"
    manifest = {
        "job_id": "smoke",
        "title": "D7 CLOUD RENDER TEST",
        "script": "render smoke test",
        "description": "",
        "hashtags": [],
        "ai_generated": False,
        "voice": str(audio.resolve()),
        "assets": [{"type": "image", "path": str(frame.resolve()), "duration": 3}],
        "output": str(output.resolve()),
    }
    manifest_path = smoke / "manifest.json"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

    subprocess.run(["node", "render/render.mjs", str(manifest_path)], cwd=root, check=True)
    if not output.exists() or output.stat().st_size < 50_000:
        raise RuntimeError("Render smoke failed: final.mp4 missing or too small")

    probe = subprocess.check_output([
        "ffprobe", "-v", "error", "-show_entries", "stream=codec_type,codec_name,width,height",
        "-of", "json", str(output)
    ], text=True)
    print("RENDER_OK", output.stat().st_size, probe)


if __name__ == "__main__":
    main()

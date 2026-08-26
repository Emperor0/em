from __future__ import annotations
from pathlib import Path
import json, subprocess

MIN_VIDEO_BITRATE = 2_500_000
MIN_AUDIO_BITRATE = 96_000
MIN_FPS = 24.0
MIN_DURATION = 20.0
MAX_DURATION = 60.0
MIN_ASSETS = 10
MAX_STILL_SHARE = 0.30
MAX_SINGLE_SHOT = 4.0


def _probe(path: Path) -> dict:
    raw = subprocess.check_output([
        "ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)
    ], text=True)
    return json.loads(raw)


def _fps(value: str) -> float:
    try:
        a, b = value.split("/")
        return float(a) / float(b)
    except Exception:
        return 0.0


def validate(final: Path, assets: list[dict]) -> dict:
    if not final.exists() or final.stat().st_size < 1_000_000:
        raise RuntimeError("QUALITY_GATE: final file missing or suspiciously small")

    p = _probe(final)
    streams = p.get("streams", [])
    v = next((x for x in streams if x.get("codec_type") == "video"), None)
    a = next((x for x in streams if x.get("codec_type") == "audio"), None)
    if not v:
        raise RuntimeError("QUALITY_GATE: no video stream")
    if not a:
        raise RuntimeError("QUALITY_GATE: no audio stream")

    width, height = int(v.get("width", 0)), int(v.get("height", 0))
    if width < 1080 or height < 1920:
        raise RuntimeError(f"QUALITY_GATE: resolution too low {width}x{height}")
    if v.get("codec_name") != "h264":
        raise RuntimeError(f"QUALITY_GATE: expected H.264, got {v.get('codec_name')}")

    fps = _fps(v.get("avg_frame_rate", "0/1"))
    if fps < MIN_FPS:
        raise RuntimeError(f"QUALITY_GATE: fps too low {fps:.2f}")

    duration = float(p.get("format", {}).get("duration") or 0)
    if not MIN_DURATION <= duration <= MAX_DURATION:
        raise RuntimeError(f"QUALITY_GATE: duration {duration:.2f}s outside {MIN_DURATION}-{MAX_DURATION}s")

    vbr = int(v.get("bit_rate") or p.get("format", {}).get("bit_rate") or 0)
    abr = int(a.get("bit_rate") or 0)
    if vbr and vbr < MIN_VIDEO_BITRATE:
        raise RuntimeError(f"QUALITY_GATE: video bitrate too low {vbr}")
    if abr and abr < MIN_AUDIO_BITRATE:
        raise RuntimeError(f"QUALITY_GATE: audio bitrate too low {abr}")

    if len(assets) < MIN_ASSETS:
        raise RuntimeError(f"QUALITY_GATE: only {len(assets)} visual beats; need at least {MIN_ASSETS}")
    total = sum(float(x.get("duration", 0)) for x in assets) or 1
    still = sum(float(x.get("duration", 0)) for x in assets if x.get("type") == "image")
    if still / total > MAX_STILL_SHARE:
        raise RuntimeError(f"QUALITY_GATE: static-image share {still/total:.0%} exceeds {MAX_STILL_SHARE:.0%}")
    longest = max((float(x.get("duration", 0)) for x in assets), default=999)
    if longest > MAX_SINGLE_SHOT:
        raise RuntimeError(f"QUALITY_GATE: shot too long {longest:.2f}s")

    # Reject near-silent audio. The exact loudness target is normalized later by the renderer.
    vol = subprocess.run([
        "ffmpeg", "-i", str(final), "-af", "volumedetect", "-f", "null", "-"
    ], capture_output=True, text=True)
    stderr = vol.stderr or ""
    if "mean_volume: -91.0 dB" in stderr or "max_volume: -91.0 dB" in stderr:
        raise RuntimeError("QUALITY_GATE: audio is effectively silent")

    return {
        "passed": True,
        "resolution": f"{width}x{height}",
        "fps": round(fps, 2),
        "duration": round(duration, 2),
        "video_bitrate": vbr,
        "audio_bitrate": abr,
        "visual_beats": len(assets),
        "still_share": round(still / total, 3),
        "file_bytes": final.stat().st_size,
    }

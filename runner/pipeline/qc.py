from __future__ import annotations
from pathlib import Path
import json, subprocess, re
from . import config

MIN_DURATION = 25.0
MAX_DURATION = 55.0
MIN_ASSETS = 14
MAX_SINGLE_SHOT = 2.8
MAX_MOTION_GRAPHIC_SHARE = 0.40


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


def _db(stderr:str, key:str):
    m=re.search(rf"{re.escape(key)}:\s*(-?\d+(?:\.\d+)?) dB",stderr)
    return float(m.group(1)) if m else None


def validate(final: Path, assets: list[dict]) -> dict:
    if not final.exists() or final.stat().st_size < 1_500_000:
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
    if width < config.MIN_FINAL_WIDTH or height < config.MIN_FINAL_HEIGHT:
        raise RuntimeError(f"QUALITY_GATE: resolution too low {width}x{height}")
    if v.get("codec_name") != "h264":
        raise RuntimeError(f"QUALITY_GATE: expected H.264, got {v.get('codec_name')}")

    fps = _fps(v.get("avg_frame_rate", "0/1"))
    if fps < config.MIN_FINAL_FPS:
        raise RuntimeError(f"QUALITY_GATE: fps too low {fps:.2f}")

    duration = float(p.get("format", {}).get("duration") or 0)
    if not MIN_DURATION <= duration <= MAX_DURATION:
        raise RuntimeError(f"QUALITY_GATE: duration {duration:.2f}s outside {MIN_DURATION}-{MAX_DURATION}s")

    vbr = int(v.get("bit_rate") or p.get("format", {}).get("bit_rate") or 0)
    abr = int(a.get("bit_rate") or 0)
    if vbr and vbr < config.MIN_FINAL_BITRATE:
        raise RuntimeError(f"QUALITY_GATE: video bitrate too low {vbr}; need {config.MIN_FINAL_BITRATE}+")
    if abr and abr < 180_000:
        raise RuntimeError(f"QUALITY_GATE: audio bitrate too low {abr}")

    if len(assets) < MIN_ASSETS:
        raise RuntimeError(f"QUALITY_GATE: only {len(assets)} visual beats; need at least {MIN_ASSETS}")

    total = sum(float(x.get("duration", 0)) for x in assets) or 1.0
    video_time = sum(float(x.get("duration", 0)) for x in assets if x.get("type") == "video")
    still_time = sum(float(x.get("duration", 0)) for x in assets if x.get("type") == "image")
    motion_time = sum(float(x.get("duration", 0)) for x in assets if x.get("type") == "motion")

    video_ratio=video_time/total
    still_ratio=still_time/total
    motion_ratio=motion_time/total
    if video_ratio < config.MIN_VIDEO_ASSET_RATIO:
        raise RuntimeError(f"QUALITY_GATE: real/AI moving footage share {video_ratio:.0%} below {config.MIN_VIDEO_ASSET_RATIO:.0%}")
    if still_ratio > config.MAX_STILL_RATIO:
        raise RuntimeError(f"QUALITY_GATE: static-image share {still_ratio:.0%} exceeds {config.MAX_STILL_RATIO:.0%}")
    if motion_ratio > MAX_MOTION_GRAPHIC_SHARE:
        raise RuntimeError(f"QUALITY_GATE: motion-graphic share {motion_ratio:.0%} is too high; video would feel synthetic")

    longest = max((float(x.get("duration", 0)) for x in assets), default=999)
    if longest > MAX_SINGLE_SHOT:
        raise RuntimeError(f"QUALITY_GATE: shot too long {longest:.2f}s")

    vol = subprocess.run([
        "ffmpeg", "-i", str(final), "-af", "volumedetect", "-f", "null", "-"
    ], capture_output=True, text=True)
    stderr = vol.stderr or ""
    mean_db=_db(stderr,"mean_volume")
    max_db=_db(stderr,"max_volume")
    if mean_db is None or max_db is None:
        raise RuntimeError("QUALITY_GATE: could not measure audio loudness")
    if mean_db < -24:
        raise RuntimeError(f"QUALITY_GATE: narration too quiet ({mean_db:.1f} dB mean)")
    if max_db < -8:
        raise RuntimeError(f"QUALITY_GATE: audio lacks usable peak level ({max_db:.1f} dB)")

    return {
        "passed": True,
        "resolution": f"{width}x{height}",
        "fps": round(fps, 2),
        "duration": round(duration, 2),
        "video_bitrate": vbr,
        "audio_bitrate": abr,
        "visual_beats": len(assets),
        "video_share": round(video_ratio,3),
        "motion_graphic_share": round(motion_ratio,3),
        "still_share": round(still_ratio,3),
        "mean_volume_db": mean_db,
        "max_volume_db": max_db,
        "file_bytes": final.stat().st_size,
    }

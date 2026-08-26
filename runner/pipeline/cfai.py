from __future__ import annotations
import requests, json, subprocess
from pathlib import Path
from . import config

def _url(model):
    return f"https://api.cloudflare.com/client/v4/accounts/{config.CF_ACCOUNT_ID}/ai/run/{model}"

def _headers():
    return {"Authorization":f"Bearer {config.CF_AI_TOKEN}","Content-Type":"application/json"}

def run_text(messages, temperature=0.5, max_tokens=1800):
    if not config.CF_ACCOUNT_ID or not config.CF_AI_TOKEN:
        raise RuntimeError("Cloudflare Workers AI credentials are required for research planning/script generation")
    r=requests.post(_url(config.CF_TEXT_MODEL),headers=_headers(),json={
        "messages":messages,"temperature":temperature,"max_tokens":max_tokens
    },timeout=120)
    r.raise_for_status()
    data=r.json()
    result=data.get("result",data)
    if isinstance(result,dict):
        return result.get("response") or result.get("text") or json.dumps(result)
    return str(result)

def _edge_tts(text:str, out_path:Path):
    cmd=[
        "edge-tts",
        "--voice",config.EDGE_VOICE,
        "--rate",config.EDGE_RATE,
        "--pitch",config.EDGE_PITCH,
        "--text",text,
        "--write-media",str(out_path),
    ]
    subprocess.run(cmd,check=True,timeout=180)
    if not out_path.exists() or out_path.stat().st_size < 20000:
        raise RuntimeError("Edge TTS output missing or too small")
    return out_path

def tts(text, out_path):
    out_path=Path(out_path)
    # Quality-first cloud stack: natural neural voice without requiring the user's PC.
    if config.VOICE_ENGINE.lower()=="edge":
        return _edge_tts(text,out_path)

    if config.CF_ACCOUNT_ID and config.CF_AI_TOKEN:
        r=requests.post(_url(config.CF_TTS_MODEL),headers=_headers(),json={"prompt":text,"lang":"en"},timeout=120)
        r.raise_for_status()
        ctype=r.headers.get("content-type","")
        if "audio/" in ctype:
            out_path.write_bytes(r.content)
            if out_path.stat().st_size >= 20000:
                return out_path
        else:
            data=r.json(); result=data.get("result",{})
            if isinstance(result,str):
                import base64
                out_path.write_bytes(base64.b64decode(result))
                if out_path.stat().st_size >= 20000:
                    return out_path

    # Never publish a silent/cheap fallback. Use premium neural fallback instead.
    return _edge_tts(text,out_path)

def image(prompt, out_path):
    if not config.CF_ACCOUNT_ID or not config.CF_AI_TOKEN:
        raise RuntimeError("Cloudflare image generation credentials missing")
    r=requests.post(_url(config.CF_IMAGE_MODEL),headers=_headers(),json={
        "prompt":prompt,"num_steps":4
    },timeout=120)
    r.raise_for_status()
    if "image/" in r.headers.get("content-type",""):
        out_path.write_bytes(r.content); return out_path
    data=r.json()
    if "result" in data and isinstance(data["result"],dict) and data["result"].get("image"):
        import base64
        out_path.write_bytes(base64.b64decode(data["result"]["image"])); return out_path
    raise RuntimeError("Unexpected image response")

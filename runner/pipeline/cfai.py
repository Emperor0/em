from __future__ import annotations
import requests, json
from . import config

def _url(model):
    return f"https://api.cloudflare.com/client/v4/accounts/{config.CF_ACCOUNT_ID}/ai/run/{model}"

def _headers():
    return {"Authorization":f"Bearer {config.CF_AI_TOKEN}","Content-Type":"application/json"}

def run_text(messages, temperature=0.5, max_tokens=1800):
    r=requests.post(_url(config.CF_TEXT_MODEL),headers=_headers(),json={
        "messages":messages,"temperature":temperature,"max_tokens":max_tokens
    },timeout=120)
    r.raise_for_status()
    data=r.json()
    result=data.get("result",data)
    if isinstance(result,dict):
        return result.get("response") or result.get("text") or json.dumps(result)
    return str(result)

def tts(text, out_path):
    r=requests.post(_url(config.CF_TTS_MODEL),headers=_headers(),json={"prompt":text,"lang":"en"},timeout=120)
    r.raise_for_status()
    ctype=r.headers.get("content-type","")
    if "audio/" in ctype:
        out_path.write_bytes(r.content); return out_path
    data=r.json(); result=data.get("result",{})
    if isinstance(result,str):
        import base64
        out_path.write_bytes(base64.b64decode(result)); return out_path
    raise RuntimeError("Unexpected TTS response")

def image(prompt, out_path):
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

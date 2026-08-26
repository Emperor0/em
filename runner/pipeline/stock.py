from __future__ import annotations
import requests, random
from pathlib import Path
from . import config

def pexels(query, outdir, limit=3):
    if not config.PEXELS_API_KEY: return []
    outdir.mkdir(parents=True,exist_ok=True)
    r=requests.get("https://api.pexels.com/videos/search",
        headers={"Authorization":config.PEXELS_API_KEY},
        params={"query":query,"per_page":limit,"orientation":"portrait"},timeout=30)
    r.raise_for_status()
    paths=[]
    for i,v in enumerate(r.json().get("videos",[])):
        files=sorted(v.get("video_files",[]),key=lambda x:(x.get("width",0)*x.get("height",0)),reverse=True)
        if not files: continue
        u=files[0]["link"]
        p=outdir/f"pexels_{i:02}.mp4"
        data=requests.get(u,timeout=120).content
        p.write_bytes(data); paths.append(p)
    return paths

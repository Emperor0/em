from __future__ import annotations
from pathlib import Path
import json, shutil
from gradio_client import Client, handle_file
from . import config

def generate(prompt:str, outdir:Path, image_path:Path|None=None, index:int=0):
    """Generic HF Space adapter. API args are configurable because community Space schemas can change."""
    if not config.HF_TOKEN:
        raise RuntimeError("HF_TOKEN missing")
    client=Client(config.HF_VIDEO_SPACE, token=config.HF_TOKEN)
    base=json.loads(config.HF_VIDEO_ARGS_JSON or "{}")
    # Common parameter names used by Gradio video spaces.
    base.setdefault("prompt",prompt)
    if image_path:
        for k in ("image","input_image","start_image"):
            if k in base:
                base[k]=handle_file(str(image_path))
                break

    if config.HF_VIDEO_API_NAME:
        result=client.predict(api_name=config.HF_VIDEO_API_NAME, **base)
    else:
        # Use the first named API endpoint exposed by the Space.
        info=client.view_api(return_format="dict")
        named=list((info or {}).get("named_endpoints",{}).keys())
        if not named:
            raise RuntimeError("No named Gradio API endpoint discovered")
        result=client.predict(api_name=named[0], **base)

    candidate=None
    if isinstance(result,str): candidate=result
    elif isinstance(result,(list,tuple)):
        for x in result:
            if isinstance(x,str) and (x.endswith(".mp4") or x.endswith(".webm")):
                candidate=x; break
            if isinstance(x,dict) and x.get("path"):
                candidate=x["path"]; break
    elif isinstance(result,dict):
        candidate=result.get("path") or result.get("video")

    if not candidate:
        raise RuntimeError(f"Could not locate video in HF result: {type(result)}")
    p=Path(candidate)
    out=outdir/f"ai_{index:02}{p.suffix or '.mp4'}"
    shutil.copy2(p,out)
    return out

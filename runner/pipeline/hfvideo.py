from __future__ import annotations
from pathlib import Path
import json, shutil
from gradio_client import Client, handle_file
from . import config

H3_SPACE = "MiniMaxAI/MiniMax-H3-Turbo-Lora"
H3_CANVAS = "768x1344 · 9:16 full"


def _find_video(value):
    if value is None:
        return None
    if isinstance(value, str):
        low=value.lower().split("?")[0]
        if low.endswith((".mp4",".webm",".mov",".mkv")):
            return value
        return None
    if isinstance(value, dict):
        for key in ("path","video","url","name"):
            if key in value:
                found=_find_video(value[key])
                if found: return found
        for x in value.values():
            found=_find_video(x)
            if found: return found
        return None
    if isinstance(value,(list,tuple)):
        for x in value:
            found=_find_video(x)
            if found: return found
    path=getattr(value,"path",None)
    if path:
        return _find_video(str(path))
    return None


def _save_result(result, outdir:Path, index:int):
    candidate=_find_video(result)
    if not candidate:
        raise RuntimeError(f"Could not locate video in ZeroGPU result: {type(result)}")
    p=Path(candidate)
    if not p.exists():
        raise RuntimeError(f"ZeroGPU client returned unavailable local video path: {candidate}")
    outdir.mkdir(parents=True,exist_ok=True)
    out=outdir/f"ai_{index:02}{p.suffix or '.mp4'}"
    shutil.copy2(p,out)
    if out.stat().st_size < 250_000:
        raise RuntimeError("ZeroGPU video is suspiciously small")
    return out


def _h3(prompt:str, outdir:Path, image_path:Path|None, index:int):
    """Premium hero-shot adapter: MiniMax H3 Turbo on public Hugging Face ZeroGPU."""
    client=Client(H3_SPACE, token=config.HF_TOKEN or None, verbose=False)
    polished=(
        prompt.strip()+
        ". Premium cinematic vertical social video, physically plausible motion, detailed lighting, "
        "clean composition, no subtitles, no visible logos. No dialogue, no speech, no copyrighted music; "
        "subtle natural ambience and foley only."
    )
    image=handle_file(str(image_path)) if image_path else None

    # H3's public API has kept all original inputs positional and appends optional prompt upsampling last.
    attempts=[
        [polished,image,None,H3_CANVAS,5,4,42,False],
        [polished,image,None,H3_CANVAS,5,10,42,False],
        [polished,image,None,H3_CANVAS,5,10,42],
    ]
    errors=[]
    for args in attempts:
        try:
            result=client.predict(*args,api_name="/generate")
            return _save_result(result,outdir,index)
        except Exception as e:
            errors.append(f"{type(e).__name__}: {e}")
    raise RuntimeError("MiniMax H3 ZeroGPU failed: "+" | ".join(errors[-3:]))


def _generic(prompt:str, outdir:Path, image_path:Path|None, index:int):
    if not config.HF_VIDEO_SPACE or config.HF_VIDEO_SPACE==H3_SPACE:
        raise RuntimeError("No secondary ZeroGPU Space configured")
    client=Client(config.HF_VIDEO_SPACE, token=config.HF_TOKEN or None, verbose=False)
    base=json.loads(config.HF_VIDEO_ARGS_JSON or "{}")
    base.setdefault("prompt",prompt)
    if image_path:
        for k in ("image","input_image","start_image"):
            if k in base:
                base[k]=handle_file(str(image_path))
                break

    if config.HF_VIDEO_API_NAME:
        result=client.predict(api_name=config.HF_VIDEO_API_NAME, **base)
    else:
        info=client.view_api(return_format="dict")
        named=list((info or {}).get("named_endpoints",{}).keys())
        if not named:
            raise RuntimeError("No named Gradio API endpoint discovered")
        # Prefer generation-like endpoints over helpers/status endpoints.
        named.sort(key=lambda x:("generate" not in x.lower(),"predict" not in x.lower(),x))
        result=client.predict(api_name=named[0], **base)
    return _save_result(result,outdir,index)


def generate(prompt:str, outdir:Path, image_path:Path|None=None, index:int=0):
    """Quality router: H3 Turbo first, then a configured ZeroGPU provider."""
    errors=[]
    try:
        return _h3(prompt,outdir,image_path,index)
    except Exception as e:
        errors.append(f"H3: {e}")
    try:
        return _generic(prompt,outdir,image_path,index)
    except Exception as e:
        errors.append(f"generic: {e}")
    raise RuntimeError("All ZeroGPU video providers failed: "+" || ".join(errors))

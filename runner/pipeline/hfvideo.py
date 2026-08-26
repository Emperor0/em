from __future__ import annotations
from pathlib import Path
import json, shutil
from gradio_client import Client, handle_file
from . import config

# Current public, HF-maintained H3 demo. Supports text-to-video and 9:16 with synchronized soundtrack.
H3_SPACE = "multimodalart/minimax-h3"
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
    """Premium hero-shot adapter: unquantized MiniMax H3 on public Hugging Face ZeroGPU."""
    client=Client(H3_SPACE, token=config.HF_TOKEN or None, verbose=False)
    polished=(
        prompt.strip()+
        ". Premium cinematic vertical social video, physically plausible motion, detailed lighting, "
        "clean composition, no subtitles, no visible logos. No dialogue, no speech, no copyrighted music; "
        "subtle natural ambience and foley only."
    )
    image=handle_file(str(image_path)) if image_path else None

    # Current public Space API: prompt, first frame, last frame, canvas, duration, steps, seed, upsample.
    attempts=[
        [polished,image,None,H3_CANVAS,5,28,42,False],
        [polished,image,None,H3_CANVAS,4,20,42,False],
        [polished,image,None,H3_CANVAS,3,14,42,False],
    ]
    errors=[]
    for args in attempts:
        try:
            result=client.predict(*args,api_name="/generate")
            return _save_result(result,outdir,index)
        except Exception as e:
            errors.append(f"{type(e).__name__}: {e}")
            # Quota/auth errors won't improve by changing steps.
            low=str(e).lower()
            if any(k in low for k in ("quota","gpu quota","login","sign in","authentication","token")):
                break
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
        named.sort(key=lambda x:("generate" not in x.lower(),"predict" not in x.lower(),x))
        endpoint=named[0]
        # Do not blindly call an image-to-video endpoint without a source frame.
        if image_path is None and "image" in str((info or {}).get("named_endpoints",{}).get(endpoint,{})).lower():
            raise RuntimeError(f"Secondary provider {endpoint} requires an input image")
        result=client.predict(api_name=endpoint, **base)
    return _save_result(result,outdir,index)


def generate(prompt:str, outdir:Path, image_path:Path|None=None, index:int=0):
    """Quality router: H3 first, then a configured ZeroGPU provider."""
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

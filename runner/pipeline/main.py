from __future__ import annotations
from pathlib import Path
import json, subprocess, traceback
from . import config
from .discover import collect
from .planner import rank, script
from .research import research
from .cfai import tts
from .stock import pexels
from .hfvideo import generate as hf_generate
from .publish import youtube, tiktok
from .callback import send
from .qc import validate as quality_validate

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/"output"/config.JOB_ID
OUT.mkdir(parents=True,exist_ok=True)


def media_duration(path:Path)->float:
    raw=subprocess.check_output([
        "ffprobe","-v","error","-show_entries","format=duration","-of","default=nw=1:nk=1",str(path)
    ],text=True).strip()
    return float(raw)


def has_audio_stream(path:Path)->bool:
    try:
        raw=subprocess.check_output([
            "ffprobe","-v","error","-select_streams","a:0","-show_entries","stream=codec_type","-of","csv=p=0",str(path)
        ],text=True).strip()
        return raw=="audio"
    except Exception:
        return False


def normalize_scene_durations(scenes:list[dict], target:float)->list[dict]:
    if not scenes:
        raise RuntimeError("QUALITY_GATE: no storyboard scenes")
    if len(scenes) < config.MIN_SCENES:
        raise RuntimeError(f"QUALITY_GATE: storyboard has {len(scenes)} beats; need {config.MIN_SCENES}+")
    weights=[max(.8,min(config.MAX_SCENE_SECONDS,float(s.get("duration",1.7)))) for s in scenes]
    total=sum(weights)
    scaled=[w*target/total for w in weights]
    if max(scaled)>config.MAX_SCENE_SECONDS+0.20:
        raise RuntimeError(f"QUALITY_GATE: pacing too slow; longest beat {max(scaled):.2f}s")
    out=[]
    for scene,d in zip(scenes,scaled):
        item=dict(scene)
        item["duration"]=round(max(.70,d),3)
        item.setdefault("caption",item.get("voice","")[:48])
        item.setdefault("motion","push_in")
        item.setdefault("transition","cut")
        out.append(item)
    drift=target-sum(float(x["duration"]) for x in out)
    out[-1]["duration"]=round(max(.70,float(out[-1]["duration"])+drift),3)
    return out


def make_asset(sc:dict, *, kind:str, path:Path|None=None, use_audio:bool=False, source:str="")->dict:
    item={
        "type":kind,
        "duration":float(sc["duration"]),
        "caption":sc.get("caption") or sc.get("voice","")[:48],
        "voice":sc.get("voice","") ,
        "motion":sc.get("motion","push_in"),
        "transition":sc.get("transition","cut"),
        "visual_prompt":sc.get("visual_prompt",""),
        "use_audio":bool(use_audio),
        "source":source,
    }
    if path is not None:
        item["path"]=str(path.resolve())
    return item


def run():
    result={"job_id":config.JOB_ID,"status":"RUNNING","stage":"DISCOVERY","score":0}
    try:
        items=collect()
        if not items: raise RuntimeError("No discovery candidates")
        plan=rank(items)
        result["score"]=float(plan.get("score",0))
        if plan.get("reject") or result["score"]<config.MIN_SCORE:
            result.update(status="REJECTED",stage="SCORING",plan=plan)
            send(result); return result

        result["stage"]="RESEARCH"
        res=research(plan.get("research_queries") or [plan["source_title"]])
        if not res["verified"]:
            result.update(status="REJECTED",error="Insufficient independent research sources",research=res)
            send(result); return result

        result["stage"]="SCRIPT"
        content=script(plan,res)
        if not content.get("evidence_ok",False):
            result.update(status="REJECTED",error="Evidence gate failed")
            send(result); return result

        (OUT/"plan.json").write_text(json.dumps(plan,indent=2,ensure_ascii=False),encoding="utf-8")
        (OUT/"research.json").write_text(json.dumps(res,indent=2,ensure_ascii=False),encoding="utf-8")
        (OUT/"content.json").write_text(json.dumps(content,indent=2,ensure_ascii=False),encoding="utf-8")

        result["stage"]="VOICE"
        voice=Path(tts(content["script"],OUT/"voice.mp3"))
        if not voice.exists() or voice.stat().st_size < 30_000:
            raise RuntimeError("QUALITY_GATE: narration output missing or too small")
        voice_duration=media_duration(voice)
        if not 25 <= voice_duration <= 55:
            raise RuntimeError(f"QUALITY_GATE: narration duration {voice_duration:.2f}s outside premium Short range")

        scenes=normalize_scene_durations(content.get("scenes") or [],voice_duration)
        content["scenes"]=scenes

        result["stage"]="ASSETS"
        assets=[]
        ai_used=0
        asset_errors=[]
        for i,sc in enumerate(scenes):
            requested=sc.get("asset_type","stock")

            # Use scarce free ZeroGPU only for the highest-value hero shot.
            if requested=="ai_video" and ai_used < config.HF_MAX_GPU_JOBS:
                try:
                    p=Path(hf_generate(sc["visual_prompt"],OUT,None,i))
                    if p.exists() and p.stat().st_size>250_000:
                        assets.append(make_asset(
                            sc,kind="video",path=p,
                            use_audio=has_audio_stream(p),
                            source="zerogpu_ai"
                        ))
                        ai_used+=1
                        continue
                except Exception as e:
                    asset_errors.append(f"ai_video[{i}]: {e}")

            # Motion graphics are true animations, not slides.
            if requested=="motion_graphic":
                assets.append(make_asset(sc,kind="motion",source="remotion_graphic"))
                continue

            q=sc.get("stock_query") or sc.get("visual_prompt") or "technology"
            try:
                clips=pexels(q,OUT/"stock"/f"{i}",1)
            except Exception as e:
                clips=[]
                asset_errors.append(f"stock[{i}]: {e}")
            if clips:
                p=Path(clips[0])
                if p.exists() and p.stat().st_size>250_000:
                    # Stock audio is intentionally muted to avoid unknown music/dialogue rights.
                    assets.append(make_asset(sc,kind="video",path=p,use_audio=False,source="pexels"))
                    continue

            assets.append(make_asset(sc,kind="motion",source="remotion_fallback"))
            asset_errors.append(f"motion_fallback[{i}]: no premium moving source acquired")

        if len(assets) != len(scenes):
            raise RuntimeError(f"QUALITY_GATE: acquired {len(assets)}/{len(scenes)} visual beats")

        raw=OUT/"render_raw.mp4"
        final=OUT/"final.mp4"
        manifest={
          "job_id":config.JOB_ID,
          "title":content["title"],
          "script":content["script"],
          "description":content.get("description",""),
          "hashtags":content.get("hashtags",[]),
          "ai_generated":content.get("ai_generated",True),
          "voice":str(voice.resolve()),
          "assets":assets,
          "output":str(raw.resolve())
        }
        (OUT/"manifest.json").write_text(json.dumps(manifest,indent=2,ensure_ascii=False),encoding="utf-8")

        result["stage"]="RENDER"
        subprocess.run(["node","render/render.mjs",str(OUT/"manifest.json")],cwd=ROOT,check=True)
        if not raw.exists() or raw.stat().st_size<1_500_000:
            raise RuntimeError("QUALITY_GATE: raw render missing or suspiciously small")

        # Platform-ready audio mastering. AI ambience/foley (if present) is already mixed quietly under narration.
        subprocess.run([
            "ffmpeg","-y","-i",str(raw),
            "-af","loudnorm=I=-14:TP=-1.5:LRA=8",
            "-c:v","copy","-c:a","aac","-b:a","256k","-movflags","+faststart",str(final)
        ],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)

        result["stage"]="QUALITY_GATE"
        qc=quality_validate(final,assets)
        (OUT/"quality_report.json").write_text(json.dumps(qc,indent=2),encoding="utf-8")

        # Publishing is impossible until the finished file passes every quality gate.
        result["stage"]="PUBLISH"
        yt=youtube(final,content)
        tt=tiktok(final,content)
        result.update(
            status="PUBLISHED" if yt.get("status")=="PUBLISHED" or tt.get("status")=="SUBMITTED" else "READY",
            stage="DONE",
            youtube=yt,
            tiktok=tt,
            youtube_id=yt.get("id"),
            tiktok_publish_id=tt.get("publish_id"),
            ai_video_jobs=ai_used,
            quality=qc,
            asset_errors=asset_errors[-20:],
        )
        send(result)
        return result
    except Exception as e:
        message=f"{type(e).__name__}: {e}"
        status="REJECTED_QUALITY" if "QUALITY_GATE" in message else "FAILED"
        result.update(status=status,error=message,traceback=traceback.format_exc())
        try: send(result)
        except Exception: pass
        return result


if __name__=="__main__":
    print(json.dumps(run(),indent=2,ensure_ascii=False))

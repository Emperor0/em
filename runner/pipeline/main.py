from __future__ import annotations
from pathlib import Path
import json, subprocess, traceback, math
from . import config
from .discover import collect
from .planner import rank, script
from .research import research
from .cfai import tts, image
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


def normalize_scene_durations(scenes:list[dict], target:float)->list[dict]:
    if not scenes:
        raise RuntimeError("No storyboard scenes")
    if len(scenes) < math.ceil(target/3.2):
        raise RuntimeError(f"Storyboard too sparse for {target:.1f}s narration: {len(scenes)} beats")
    weights=[max(.8,min(3.2,float(s.get("duration",2.0)))) for s in scenes]
    total=sum(weights)
    scaled=[w*target/total for w in weights]
    if max(scaled)>3.4:
        raise RuntimeError(f"Storyboard pacing too slow: longest beat {max(scaled):.2f}s")
    out=[]
    for scene,d in zip(scenes,scaled):
        item=dict(scene)
        item["duration"]=round(max(.75,d),3)
        out.append(item)
    # Correct rounding drift on the final beat.
    drift=target-sum(float(x["duration"]) for x in out)
    out[-1]["duration"]=round(max(.75,float(out[-1]["duration"])+drift),3)
    return out


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
        voice=tts(content["script"],OUT/"voice.mp3")
        voice=Path(voice)
        if not voice.exists() or voice.stat().st_size < 20_000:
            raise RuntimeError("VOICE_GATE: TTS output missing or too small")
        voice_duration=media_duration(voice)
        if not 20 <= voice_duration <= 58:
            raise RuntimeError(f"VOICE_GATE: narration duration {voice_duration:.2f}s outside production range")

        scenes=normalize_scene_durations(content.get("scenes") or [],voice_duration)
        content["scenes"]=scenes

        result["stage"]="ASSETS"
        assets=[]
        ai_used=0
        asset_errors=[]
        for i,sc in enumerate(scenes):
            typ=sc.get("asset_type","stock")
            if typ=="ai_video" and ai_used < config.HF_MAX_GPU_JOBS:
                try:
                    p=Path(hf_generate(sc["visual_prompt"],OUT,None,i))
                    if p.exists() and p.stat().st_size>100_000:
                        assets.append({"type":"video","path":str(p.resolve()),"duration":sc["duration"]})
                        ai_used+=1
                        continue
                except Exception as e:
                    asset_errors.append(f"ai_video[{i}]: {e}")

            q=sc.get("visual_prompt","technology")
            try:
                clips=pexels(q,OUT/"stock"/f"{i}",1)
            except Exception as e:
                clips=[]
                asset_errors.append(f"stock[{i}]: {e}")
            if clips:
                assets.append({"type":"video","path":str(Path(clips[0]).resolve()),"duration":sc["duration"]})
                continue

            # Still fallback is allowed only sparingly; the QC gate rejects slideshow-heavy output.
            try:
                still=OUT/f"scene_{i:02}.png"
                image("premium cinematic vertical editorial image, strong depth and composition, "+q+", no text, no logos",still)
                assets.append({"type":"image","path":str(still.resolve()),"duration":sc["duration"]})
            except Exception as e:
                asset_errors.append(f"image[{i}]: {e}")

        if len(assets) != len(scenes):
            raise RuntimeError(f"ASSET_GATE: acquired {len(assets)}/{len(scenes)} visual beats")

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
        if not raw.exists() or raw.stat().st_size<1_000_000:
            raise RuntimeError("RENDER_GATE: raw render missing or too small")

        # Normalize speech loudness to a platform-friendly target without re-encoding video.
        subprocess.run([
            "ffmpeg","-y","-i",str(raw),
            "-af","loudnorm=I=-14:TP=-1.5:LRA=11",
            "-c:v","copy","-c:a","aac","-b:a","192k","-movflags","+faststart",str(final)
        ],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)

        result["stage"]="QUALITY_GATE"
        qc=quality_validate(final,assets)
        (OUT/"quality_report.json").write_text(json.dumps(qc,indent=2),encoding="utf-8")

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
            asset_errors=asset_errors[-10:],
        )
        send(result)
        return result
    except Exception as e:
        result.update(status="FAILED",error=f"{type(e).__name__}: {e}",traceback=traceback.format_exc())
        try: send(result)
        except Exception: pass
        return result


if __name__=="__main__":
    print(json.dumps(run(),indent=2,ensure_ascii=False))

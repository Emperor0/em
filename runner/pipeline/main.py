from __future__ import annotations
from pathlib import Path
import json, os, subprocess, traceback, shutil
from . import config
from .discover import collect
from .planner import rank, script
from .research import research
from .cfai import tts, image
from .stock import pexels
from .hfvideo import generate as hf_generate
from .publish import youtube, tiktok
from .callback import send

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/"output"/config.JOB_ID
OUT.mkdir(parents=True,exist_ok=True)

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

        result["stage"]="ASSETS"
        assets=[]
        ai_used=0
        for i,sc in enumerate(content.get("scenes",[])):
            typ=sc.get("asset_type","stock")
            if typ=="ai_video" and ai_used < config.HF_MAX_GPU_JOBS:
                try:
                    assets.append({"type":"video","path":str(hf_generate(sc["visual_prompt"],OUT,None,i)),"duration":sc.get("duration",2)})
                    ai_used+=1
                    continue
                except Exception as e:
                    # ZeroGPU quota/queue/schema failures must never stop publishing.
                    pass
            q=sc.get("visual_prompt","technology")
            clips=pexels(q,OUT/"stock"/f"{i}",1)
            if clips:
                assets.append({"type":"video","path":str(clips[0]),"duration":sc.get("duration",2)})
            else:
                still=OUT/f"scene_{i:02}.png"
                image("cinematic vertical 9:16 editorial visual, "+q+", no text, no logos, premium lighting",still)
                assets.append({"type":"image","path":str(still),"duration":sc.get("duration",2)})

        manifest={
          "job_id":config.JOB_ID,
          "title":content["title"],
          "script":content["script"],
          "description":content.get("description",""),
          "hashtags":content.get("hashtags",[]),
          "ai_generated":content.get("ai_generated",True),
          "voice":str(voice),
          "assets":assets,
          "output":str(OUT/"final.mp4")
        }
        (OUT/"manifest.json").write_text(json.dumps(manifest,indent=2,ensure_ascii=False),encoding="utf-8")

        result["stage"]="RENDER"
        subprocess.run(["node","render/render.mjs",str(OUT/"manifest.json")],cwd=ROOT,check=True)

        final=OUT/"final.mp4"
        if not final.exists() or final.stat().st_size < 100_000:
            raise RuntimeError("Final render missing/too small")

        result["stage"]="PUBLISH"
        yt=youtube(final,content)
        tt=tiktok(final,content)
        result.update(status="PUBLISHED" if yt.get("status")=="PUBLISHED" or tt.get("status")=="SUBMITTED" else "READY",
                      stage="DONE",
                      youtube=yt,tiktok=tt,
                      youtube_id=yt.get("id"),
                      tiktok_publish_id=tt.get("publish_id"),
                      ai_video_jobs=ai_used)
        send(result)
        return result
    except Exception as e:
        result.update(status="FAILED",error=f"{type(e).__name__}: {e}",traceback=traceback.format_exc())
        try: send(result)
        except Exception: pass
        return result

if __name__=="__main__":
    print(json.dumps(run(),indent=2,ensure_ascii=False))

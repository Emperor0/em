from __future__ import annotations
import json, re
from .cfai import run_text

SYSTEM="""You are D7 Media Cloud's senior viral video producer.
Target: global English audience. Optimize for retention, originality, monetization and cinematic production quality.
Hard rules: original content, no fake claims, no fake tests, no copied scripts, no misinformation,
no unlicensed media, no deceptive title/thumbnail, no filler, no static slideshow pacing.
Every accepted Short must be designed for rapid visual change and premium narration.
Return strict JSON only."""

def rank(items):
    compact=[{"source":x["source"],"title":x["title"],"url":x["url"],"signal":x.get("signal",0)} for x in items[:100]]
    prompt=f"""Score these current candidates and choose the single strongest idea.
JSON candidates:
{json.dumps(compact,ensure_ascii=False)}

Return:
{{
 "score": 0-100,
 "source_title": "...",
 "source_url": "...",
 "video_title": "English viral title",
 "hook": "first 1.0 seconds",
 "reason": "...",
 "monetization": ["ads","affiliate","sponsor","digital_product"],
 "format": "short",
 "research_queries": ["...","...","..."],
 "visual_style": "specific premium visual direction",
 "reject": false
}}
Reject if score <84 or if the idea cannot support strong visuals."""
    raw=run_text([{"role":"system","content":SYSTEM},{"role":"user","content":prompt}],0.2,1400)
    m=re.search(r"\{.*\}",raw,re.S)
    if not m: raise ValueError("Planner did not return JSON")
    return json.loads(m.group(0))

def script(plan, research):
    prompt=f"""Plan:
{json.dumps(plan,ensure_ascii=False)}
Research:
{json.dumps(research,ensure_ascii=False)[:18000]}

Write a premium 32-48 second English Short.
Requirements:
- First visual/audio payoff begins in the first 0.5 second.
- No greeting and no generic intro.
- 14-24 micro-scenes.
- Each scene 0.8-2.6 seconds; default 1.4-2.0 seconds.
- New visual information, camera move, graphic beat, or proof point every 1-2 seconds.
- At least 60% of runtime must be real video footage or AI-generated video, not still images.
- Motion graphics are allowed for diagrams/UI/explanations but must animate.
- Prefer AI video for 1-2 hero scenes, premium stock for supporting scenes, motion graphics for explanations.
- Never instruct the system to show a copyrighted logo unless necessary for factual editorial context.
- Use only claims supported by the research.
- Voice must be punchy, conversational, confident and natural, not list-like.
- Every scene must include a short caption phrase suitable for kinetic captions.

Return JSON:
{{
 "title":"...",
 "hook":"...",
 "script":"full narration",
 "scenes":[
   {{
     "duration":1.6,
     "voice":"narration for this beat",
     "caption":"2-7 word caption",
     "visual_prompt":"specific cinematic shot description",
     "stock_query":"short concrete stock search query",
     "asset_type":"ai_video|stock|motion_graphic",
     "motion":"push_in|pan_left|pan_right|punch_zoom|parallax|none",
     "transition":"cut|whip|flash|zoom"
   }}
 ],
 "description":"...",
 "hashtags":["#AI"],
 "ai_generated":true,
 "evidence_ok":true
}}"""
    raw=run_text([{"role":"system","content":SYSTEM},{"role":"user","content":prompt}],0.45,3600)
    m=re.search(r"\{.*\}",raw,re.S)
    if not m: raise ValueError("Script engine did not return JSON")
    data=json.loads(m.group(0))
    scenes=data.get("scenes") or []
    if not 14 <= len(scenes) <= 24:
        raise ValueError(f"Storyboard quality gate failed: {len(scenes)} scenes; expected 14-24")
    if any(float(s.get("duration",0)) > 2.6 for s in scenes):
        raise ValueError("Storyboard quality gate failed: a scene exceeds 2.6 seconds")
    return data

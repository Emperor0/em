from __future__ import annotations
import json, re
from .cfai import run_text

SYSTEM="""You are D7 Media Cloud's senior short-form content director.
Target: global English audience. Optimize for viral reach, retention and monetization together.
Hard rules: original content, no fake claims, no fake tests or personal experience, no copied scripts,
no misinformation, no unlicensed media, no deceptive title/thumbnail. Reject weak global fit or weak monetization.
The finished video must feel premium and fast, never like a slideshow. Return strict JSON only."""


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
 "hook": "first 1.0 second",
 "reason": "...",
 "monetization": ["ads","affiliate","sponsor","digital_product"],
 "format": "short",
 "research_queries": ["...","...","..."],
 "visual_style": "premium cinematic editorial tech",
 "reject": false
}}
Reject if score <82."""
    raw=run_text([{"role":"system","content":SYSTEM},{"role":"user","content":prompt}],0.2,1400)
    m=re.search(r"\{.*\}",raw,re.S)
    if not m: raise ValueError("Planner did not return JSON")
    return json.loads(m.group(0))


def script(plan, research):
    prompt=f"""Plan:
{json.dumps(plan,ensure_ascii=False)}
Research:
{json.dumps(research,ensure_ascii=False)[:18000]}

Create a premium 35-50 second English YouTube Short / TikTok script.
MANDATORY production rules:
- Hook/payoff begins in the first 0.5-1.0 second. No greeting.
- 14 to 22 distinct visual beats.
- Each beat 0.8 to 2.8 seconds; no scene may exceed 3.2 seconds.
- At least 70% of scene time should request moving video (stock or AI video), not still images.
- Reserve 2-4 visually spectacular hero beats for ai_video, and use rights-safe vertical stock for support beats.
- Every beat must advance information, tension, proof, comparison or payoff. No filler.
- Spoken delivery should be natural, punchy and factual, roughly 145-175 words total.
- Use only claims supported by the research. Never say “I tested”, “I tried”, or imply first-hand use unless provided as verified evidence.
- End with a useful final payoff, not generic engagement bait.

Return strict JSON:
{{
 "title":"...",
 "hook":"...",
 "script":"...",
 "scenes":[
   {{"duration":1.5,"voice":"exact narration for this beat","visual_prompt":"specific cinematic shot with motion and composition","asset_type":"ai_video|stock|motion_graphic"}}
 ],
 "description":"...",
 "hashtags":["#AI"],
 "ai_generated":true,
 "evidence_ok":true
}}"""
    raw=run_text([{"role":"system","content":SYSTEM},{"role":"user","content":prompt}],0.48,3600)
    m=re.search(r"\{.*\}",raw,re.S)
    if not m: raise ValueError("Script engine did not return JSON")
    data=json.loads(m.group(0))
    scenes=data.get("scenes") or []
    if len(scenes) < 14:
        raise ValueError(f"Script quality gate: only {len(scenes)} visual beats")
    if any(float(s.get("duration",0)) > 3.2 for s in scenes):
        raise ValueError("Script quality gate: scene longer than 3.2 seconds")
    return data

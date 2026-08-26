from __future__ import annotations
import json, re
from .cfai import run_text

SYSTEM="""You are D7 Media Cloud's content strategist.
Target: global English audience. Optimize for viral reach AND monetization, not views alone.
Hard rules: original content, no fake claims, no fake tests, no copied scripts, no misinformation,
no unlicensed media, no deceptive title/thumbnail. Reject topics with weak global fit or weak monetization.
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
 "hook": "first 1.5 seconds",
 "reason": "...",
 "monetization": ["ads","affiliate","sponsor","digital_product"],
 "format": "short",
 "research_queries": ["...","..."],
 "visual_style": "...",
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

Write a 35-50 second English Short with extremely strong retention.
No greeting. New information or visual payoff every 1-2 seconds.
Use only claims supported by research.
Return JSON:
{{
 "title":"...",
 "hook":"...",
 "script":"...",
 "scenes":[{{"duration":2.0,"voice":"...","visual_prompt":"...","asset_type":"ai_video|stock|motion_graphic"}}],
 "description":"...",
 "hashtags":["#AI"],
 "ai_generated":true,
 "evidence_ok":true
}}"""
    raw=run_text([{"role":"system","content":SYSTEM},{"role":"user","content":prompt}],0.55,2600)
    m=re.search(r"\{.*\}",raw,re.S)
    if not m: raise ValueError("Script engine did not return JSON")
    return json.loads(m.group(0))

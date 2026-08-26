from __future__ import annotations
import requests, feedparser, re, math, time
from urllib.parse import quote

UA={"User-Agent":"D7MediaCloud/4.0"}

RSS = [
 "https://news.google.com/rss/search?q=AI+technology+when:1d&hl=en-US&gl=US&ceid=US:en",
 "https://news.google.com/rss/search?q=artificial+intelligence+tools+when:1d&hl=en-US&gl=US&ceid=US:en",
 "https://news.google.com/rss/search?q=gaming+technology+when:1d&hl=en-US&gl=US&ceid=US:en",
]

def google_news():
    out=[]
    for url in RSS:
        try:
            feed=feedparser.parse(url)
            for e in feed.entries[:25]:
                out.append({"source":"google_news","title":e.title,"url":e.link,"signal":60})
        except Exception:
            pass
    return out

def hackernews():
    out=[]
    try:
        ids=requests.get("https://hacker-news.firebaseio.com/v0/topstories.json",timeout=15).json()[:40]
        for i in ids:
            d=requests.get(f"https://hacker-news.firebaseio.com/v0/item/{i}.json",timeout=10).json()
            if d and d.get("title"):
                out.append({"source":"hackernews","title":d["title"],"url":d.get("url") or f"https://news.ycombinator.com/item?id={i}","signal":d.get("score",0)})
    except Exception:
        pass
    return out

def reddit():
    out=[]
    for sub in ["artificial","technology","ChatGPT","LocalLLaMA","gadgets"]:
        try:
            j=requests.get(f"https://www.reddit.com/r/{sub}/hot.json?limit=15",headers=UA,timeout=15).json()
            for x in j.get("data",{}).get("children",[]):
                d=x.get("data",{})
                out.append({"source":"reddit","title":d.get("title",""),"url":"https://www.reddit.com"+d.get("permalink",""),"signal":d.get("score",0)})
        except Exception:
            pass
    return out

def dedupe(items):
    seen=set(); out=[]
    for x in items:
        k=re.sub(r"[^a-z0-9]+"," ",x["title"].lower()).strip()
        if not k or k in seen: continue
        seen.add(k); out.append(x)
    return out

def collect():
    return dedupe(google_news()+hackernews()+reddit())

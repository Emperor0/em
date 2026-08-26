from __future__ import annotations
import requests, feedparser, re
from urllib.parse import quote, urlparse
from bs4 import BeautifulSoup

UA={"User-Agent":"Mozilla/5.0 D7MediaCloud/4.0"}

def fetch_text(url):
    r=requests.get(url,headers=UA,timeout=18)
    r.raise_for_status()
    soup=BeautifulSoup(r.text,"html.parser")
    for x in soup(["script","style","nav","footer"]): x.decompose()
    return re.sub(r"\s+"," ",soup.get_text(" ",strip=True))[:5000]

def google_news_search(q):
    url=f"https://news.google.com/rss/search?q={quote(q)}&hl=en-US&gl=US&ceid=US:en"
    f=feedparser.parse(url)
    return [{"title":e.title,"url":e.link} for e in f.entries[:8]]

def research(queries):
    sources=[]; domains=set()
    for q in queries[:4]:
        for x in google_news_search(q):
            try:
                text=fetch_text(x["url"])
                dom=urlparse(x["url"]).netloc
                if text and dom not in domains:
                    sources.append({"title":x["title"],"url":x["url"],"domain":dom,"excerpt":text})
                    domains.add(dom)
            except Exception:
                pass
            if len(sources)>=5: break
        if len(sources)>=5: break
    return {"verified":len(sources)>=2,"sources":sources,"source_count":len(sources)}

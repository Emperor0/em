from __future__ import annotations
import os, json, tempfile, requests
from pathlib import Path
from . import config

def youtube(video:Path, meta:dict):
    if not config.YOUTUBE_CLIENT_JSON or not config.YOUTUBE_TOKEN_JSON:
        return {"status":"SKIPPED","reason":"youtube secrets missing"}

    from google.oauth2.credentials import Credentials
    from google.auth.transport.requests import Request
    from googleapiclient.discovery import build
    from googleapiclient.http import MediaFileUpload

    scopes=["https://www.googleapis.com/auth/youtube.upload","https://www.googleapis.com/auth/youtube.readonly"]
    tok=json.loads(config.YOUTUBE_TOKEN_JSON)
    creds=Credentials.from_authorized_user_info(tok,scopes)
    if creds.expired and creds.refresh_token:
        creds.refresh(Request())

    yt=build("youtube","v3",credentials=creds)
    body={
      "snippet":{"title":meta["title"][:100],"description":meta["description"],"tags":[h.lstrip("#") for h in meta.get("hashtags",[])]},
      "status":{"privacyStatus":config.YOUTUBE_PRIVACY,"selfDeclaredMadeForKids":False}
    }
    req=yt.videos().insert(part="snippet,status",body=body,media_body=MediaFileUpload(str(video),chunksize=-1,resumable=True))
    resp=None
    while resp is None:
        _,resp=req.next_chunk()
    return {"status":"PUBLISHED","id":resp["id"],"url":f"https://youtu.be/{resp['id']}"}

def tiktok(video:Path, meta:dict):
    if not config.TIKTOK_ACCESS_TOKEN:
        return {"status":"SKIPPED","reason":"tiktok token missing"}

    h={"Authorization":f"Bearer {config.TIKTOK_ACCESS_TOKEN}","Content-Type":"application/json; charset=UTF-8"}
    if not config.TIKTOK_DIRECT_POST_APPROVED:
        # Unapproved clients can only push a draft/private flow. This remains automated up to TikTok's platform restriction.
        endpoint="https://open.tiktokapis.com/v2/post/publish/inbox/video/init/"
        size=video.stat().st_size
        body={"source_info":{"source":"FILE_UPLOAD","video_size":size,"chunk_size":size,"total_chunk_count":1}}
    else:
        endpoint="https://open.tiktokapis.com/v2/post/publish/video/init/"
        info=requests.post("https://open.tiktokapis.com/v2/post/publish/creator_info/query/",headers=h,json={},timeout=30)
        info.raise_for_status()
        allowed=info.json().get("data",{}).get("privacy_level_options",[])
        privacy=config.TIKTOK_PRIVACY if config.TIKTOK_PRIVACY in allowed else (allowed[0] if allowed else "SELF_ONLY")
        size=video.stat().st_size
        body={
          "post_info":{
            "title":(meta["title"]+" "+" ".join(meta.get("hashtags",[])))[:2200],
            "privacy_level":privacy,
            "disable_comment":False,"disable_duet":False,"disable_stitch":False,
            "brand_content_toggle":False,"brand_organic_toggle":False,
            "is_aigc":bool(meta.get("ai_generated",True))
          },
          "source_info":{"source":"FILE_UPLOAD","video_size":size,"chunk_size":size,"total_chunk_count":1}
        }

    r=requests.post(endpoint,headers=h,json=body,timeout=30)
    r.raise_for_status()
    data=r.json().get("data",{})
    upload_url=data.get("upload_url")
    if not upload_url: raise RuntimeError(f"TikTok init returned no upload URL: {r.text[:500]}")
    raw=video.read_bytes()
    rr=requests.put(upload_url,headers={
        "Content-Type":"video/mp4",
        "Content-Length":str(len(raw)),
        "Content-Range":f"bytes 0-{len(raw)-1}/{len(raw)}"
    },data=raw,timeout=300)
    rr.raise_for_status()
    return {"status":"SUBMITTED","publish_id":data.get("publish_id"),"direct":config.TIKTOK_DIRECT_POST_APPROVED}

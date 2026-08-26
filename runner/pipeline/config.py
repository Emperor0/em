from __future__ import annotations
import os, json

JOB_ID=os.getenv("D7_JOB_ID","manual")
CALLBACK_URL=os.getenv("D7_CALLBACK_URL","")
CALLBACK_SECRET=os.getenv("D7_CALLBACK_SECRET","")

CF_ACCOUNT_ID=os.getenv("CF_ACCOUNT_ID","")
CF_AI_TOKEN=os.getenv("CF_AI_TOKEN","")
CF_TEXT_MODEL=os.getenv("CF_TEXT_MODEL","@cf/zai-org/glm-4.7-flash")
CF_TTS_MODEL=os.getenv("CF_TTS_MODEL","@cf/myshell-ai/melotts")
CF_IMAGE_MODEL=os.getenv("CF_IMAGE_MODEL","@cf/black-forest-labs/flux-1-schnell")

HF_TOKEN=os.getenv("HF_TOKEN","")
HF_VIDEO_SPACE=os.getenv("HF_VIDEO_SPACE","Upsampler/wan-2-2-5b-video")
HF_VIDEO_API_NAME=os.getenv("HF_VIDEO_API_NAME","")
HF_VIDEO_ARGS_JSON=os.getenv("HF_VIDEO_ARGS_JSON","{}")
HF_MAX_GPU_JOBS=int(os.getenv("HF_MAX_GPU_JOBS","1"))

PEXELS_API_KEY=os.getenv("PEXELS_API_KEY","")
PIXABAY_API_KEY=os.getenv("PIXABAY_API_KEY","")

YOUTUBE_CLIENT_JSON=os.getenv("YOUTUBE_CLIENT_JSON","")
YOUTUBE_TOKEN_JSON=os.getenv("YOUTUBE_TOKEN_JSON","")
YOUTUBE_PRIVACY=os.getenv("YOUTUBE_PRIVACY","public")

TIKTOK_ACCESS_TOKEN=os.getenv("TIKTOK_ACCESS_TOKEN","")
TIKTOK_DIRECT_POST_APPROVED=os.getenv("TIKTOK_DIRECT_POST_APPROVED","false").lower()=="true"
TIKTOK_PRIVACY=os.getenv("TIKTOK_PRIVACY","PUBLIC_TO_EVERYONE")

MAX_DAILY_POSTS=int(os.getenv("MAX_DAILY_POSTS","2"))
MIN_SCORE=float(os.getenv("MIN_SCORE","82"))
ZERO_COST=os.getenv("ZERO_COST","true").lower()=="true"

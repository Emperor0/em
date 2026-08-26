from __future__ import annotations
import os

JOB_ID=os.getenv("D7_JOB_ID","manual")
CALLBACK_URL=os.getenv("D7_CALLBACK_URL","")
CALLBACK_SECRET=os.getenv("D7_CALLBACK_SECRET","")

CF_ACCOUNT_ID=os.getenv("CF_ACCOUNT_ID","")
CF_AI_TOKEN=os.getenv("CF_AI_TOKEN","")
CF_TEXT_MODEL=os.getenv("CF_TEXT_MODEL","@cf/zai-org/glm-4.7-flash")
CF_TTS_MODEL=os.getenv("CF_TTS_MODEL","@cf/myshell-ai/melotts")
CF_IMAGE_MODEL=os.getenv("CF_IMAGE_MODEL","@cf/black-forest-labs/flux-1-schnell")

# Premium cloud voice. Edge TTS is the zero-cost fallback when Workers AI TTS is unavailable.
VOICE_ENGINE=os.getenv("VOICE_ENGINE","edge")
EDGE_VOICE=os.getenv("EDGE_VOICE","en-US-BrianMultilingualNeural")
EDGE_RATE=os.getenv("EDGE_RATE","+4%")
EDGE_PITCH=os.getenv("EDGE_PITCH","-2Hz")

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
MIN_SCORE=float(os.getenv("MIN_SCORE","84"))
ZERO_COST=os.getenv("ZERO_COST","true").lower()=="true"

# Non-negotiable production quality gates.
MIN_SCENES=int(os.getenv("MIN_SCENES","14"))
MAX_SCENE_SECONDS=float(os.getenv("MAX_SCENE_SECONDS","2.6"))
MIN_VIDEO_ASSET_RATIO=float(os.getenv("MIN_VIDEO_ASSET_RATIO","0.60"))
MAX_STILL_RATIO=float(os.getenv("MAX_STILL_RATIO","0.10"))
MIN_FINAL_WIDTH=int(os.getenv("MIN_FINAL_WIDTH","1080"))
MIN_FINAL_HEIGHT=int(os.getenv("MIN_FINAL_HEIGHT","1920"))
MIN_FINAL_FPS=float(os.getenv("MIN_FINAL_FPS","29"))
MIN_FINAL_BITRATE=int(os.getenv("MIN_FINAL_BITRATE","7000000"))
QUALITY_FIRST=True

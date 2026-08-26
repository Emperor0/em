from __future__ import annotations
import requests
from . import config

def send(payload):
    if not config.CALLBACK_URL: return
    requests.post(config.CALLBACK_URL,
        headers={"Authorization":f"Bearer {config.CALLBACK_SECRET}","Content-Type":"application/json"},
        json=payload,timeout=30).raise_for_status()

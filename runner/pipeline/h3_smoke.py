from pathlib import Path
import json, subprocess
from .hfvideo import generate

OUT=Path("output/h3-smoke")
OUT.mkdir(parents=True,exist_ok=True)

PROMPT=(
    "A premium cinematic macro shot of a futuristic AI workstation in a modern dark studio. "
    "A hand moves across a clean keyboard while elegant abstract workflow nodes animate across a monitor, "
    "realistic screen glow reflecting on the desk, shallow depth of field, subtle camera push-in, "
    "high-end technology commercial aesthetic, physically plausible motion, crisp details"
)

video=Path(generate(PROMPT,OUT,None,0))
probe=subprocess.check_output([
    "ffprobe","-v","error","-show_streams","-show_format","-of","json",str(video)
],text=True)
info=json.loads(probe)
(OUT/"probe.json").write_text(json.dumps(info,indent=2),encoding="utf-8")
print(video)
print(json.dumps(info,indent=2))

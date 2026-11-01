import sys, time, wave, json, os
import numpy as np
import psutil
from faster_whisper import WhisperModel
model_name = sys.argv[1]; threads = int(sys.argv[2]); sp = sys.argv[3]
def load(p):
    w = wave.open(p); a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32)/32768.0; return a
clips = {k: load(os.path.join(sp, k+".wav")) for k in ("short","mid","long")}
proc = psutil.Process()
t0=time.perf_counter()
m = WhisperModel(model_name, device="cpu", compute_type="int8", cpu_threads=threads, download_root=os.path.join(sp,"models"))
load_s = time.perf_counter()-t0
rss_load = proc.memory_info().rss/1e6
out = {"model":model_name,"threads":threads,"load_s":round(load_s,2),"rss_after_load_mb":round(rss_load)}
def run(a, beam):
    t=time.perf_counter()
    segs, info = m.transcribe(a, language="en", beam_size=beam, condition_on_previous_text=False, without_timestamps=True, vad_filter=False, temperature=0.0)
    text = " ".join(s.text.strip() for s in segs)
    return time.perf_counter()-t, text
run(clips["short"],1)  # warmup
res={}
for beam in (1,5):
    for k,a in clips.items():
        ts=[]; 
        for i in range(3):
            d,txt=run(a,beam); ts.append(d)
        res[f"beam{beam}_{k}({len(a)/16000:.1f}s)"]={"median_s":round(sorted(ts)[1],2),"min_s":round(min(ts),2),"text":txt}
out["results"]=res
out["rss_peak_mb"]=round(proc.memory_info().peak_wset/1e6) if hasattr(proc.memory_info(),"peak_wset") else None
print(json.dumps(out,indent=1))

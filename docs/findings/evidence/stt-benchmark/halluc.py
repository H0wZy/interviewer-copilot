import sys, os, wave, time, numpy as np
from faster_whisper import WhisperModel
sp=sys.argv[2]; name=sys.argv[1]
def load(p):
    w=wave.open(p); return np.frombuffer(w.readframes(w.getnframes()),dtype=np.int16).astype(np.float32)/32768.0
m=WhisperModel(name,device="cpu",compute_type="int8",cpu_threads=2,download_root=os.path.join(sp,"models"))
rng=np.random.default_rng(0)
mid=load(os.path.join(sp,"mid.wav")); short=load(os.path.join(sp,"short.wav"))
cases={
 "silence_4s":np.zeros(64000,np.float32),
 "noise_-45dB_4s":(rng.standard_normal(64000)*0.0056).astype(np.float32),
 "noise_-30dB_4s":(rng.standard_normal(64000)*0.0316).astype(np.float32),
 "speech_cut_0.6s":short[:9600],
 "speech_noisy_-20dB":(mid+rng.standard_normal(len(mid))*0.03).astype(np.float32),
}
def tr(a,**kw):
    segs,_=m.transcribe(a,language="en",condition_on_previous_text=False,without_timestamps=True,vad_filter=False,**kw)
    return [(s.text.strip(),round(s.no_speech_prob,2),round(s.avg_logprob,2),round(s.compression_ratio,2)) for s in segs]
for k,a in cases.items():
    print(k,"DEFAULT-ish T-fallback:",tr(a,beam_size=1))
    print(k,"STRICT T=0:",tr(a,beam_size=1,temperature=0.0,no_speech_threshold=0.6,log_prob_threshold=-1.0,compression_ratio_threshold=2.4))
print("--- vocabulary bias on mid (B=1)")
for label,kw in [("none",{}),("hotwords",{"hotwords":"C#, .NET, ASP.NET Core, React, Next.js, PostgreSQL, Docker"}),("initial_prompt",{"initial_prompt":"Technical interview. C#, .NET, ASP.NET Core, React, Next.js, PostgreSQL, Docker."})]:
    t=time.perf_counter(); r=tr(mid,beam_size=1,temperature=0.0,**kw); print(label,round(time.perf_counter()-t,2),r)
    t=time.perf_counter(); r=tr(short,beam_size=1,temperature=0.0,**kw); print(label,"short",round(time.perf_counter()-t,2),r)

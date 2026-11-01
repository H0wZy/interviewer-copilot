import subprocess, struct, json, sys, time, wave, threading
exe = sys.argv[1:-2]; model_dir = sys.argv[-2]; wavdir = sys.argv[-1]
HDR = struct.Struct("<BII")
def frame(t, seq, payload=b""): return HDR.pack(t, seq, len(payload)) + payload
p = subprocess.Popen(exe, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=0x08000000)
t0 = time.perf_counter()
def log_err():
    for l in p.stderr: sys.stderr.write("[worker-stderr] " + l.decode(errors="replace"))
threading.Thread(target=log_err, daemon=True).start()
p.stdin.write(frame(1, 0, json.dumps({"model": "base.en", "model_dir": model_dir, "cpu_threads": 2, "beam_size": 1,
    "hotwords": "C#, .NET, ASP.NET Core, React, Next.js, PostgreSQL, Docker"}).encode())); p.stdin.flush()
def rd():
    return json.loads(p.stdout.readline())
while True:
    m = rd(); print(round(time.perf_counter()-t0,2), m)
    if m["type"] == "ready": break
def pcm(name):
    w = wave.open(f"{wavdir}/{name}.wav"); return w.readframes(w.getnframes())
for seq, name in enumerate(["short", "mid", "long", "short", "mid"], start=1):
    p.stdin.write(frame(2, seq, struct.pack("<Q", seq*1000) + pcm(name))); p.stdin.flush()
p.stdin.write(frame(4, 99)); p.stdin.flush()
done = 0
while done < 5:
    m = rd(); print(round(time.perf_counter()-t0,2), {k: (v if k != "text" else v[:70]) for k, v in m.items()})
    if m["type"] in ("result", "dropped"): done += 1
p.stdin.write(frame(0x7F, 0)); p.stdin.flush()
print(rd()); print("exit", p.wait(timeout=10))

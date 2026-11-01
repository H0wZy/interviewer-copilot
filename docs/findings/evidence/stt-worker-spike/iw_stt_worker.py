"""Prototype STT worker. stdin: binary frames, stdout: JSON lines, stderr: logs."""
import os, sys, struct, json, threading, queue, time
import numpy as np

HDR = struct.Struct("<BII")  # type u8, seq u32, len u32  (9 bytes)
T_CONFIG, T_AUDIO, T_CANCEL, T_PING, T_SHUTDOWN = 1, 2, 3, 4, 0x7F

# keep fd 1 reserved for protocol; anything that print()s goes to stderr
_out = os.fdopen(os.dup(1), "wb", buffering=0)
os.dup2(2, 1)
_out_lock = threading.Lock()
def emit(obj):
    data = (json.dumps(obj, ensure_ascii=True, separators=(",", ":")) + "\n").encode("ascii")
    with _out_lock:
        _out.write(data)

def read_exact(f, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = f.read(n - len(buf))
        if not chunk:
            return None
        buf += chunk
    return bytes(buf)

def main():
    stdin = sys.stdin.buffer
    emit({"type": "hello", "proto": 1, "pid": os.getpid()})
    cfg = {"model": "base.en", "model_dir": None, "cpu_threads": 2, "beam_size": 1,
           "initial_prompt": None, "hotwords": None}
    hdr = read_exact(stdin, HDR.size)
    t, seq, ln = HDR.unpack(hdr)
    assert t == T_CONFIG
    cfg.update(json.loads(read_exact(stdin, ln)))
    emit({"type": "loading", "model": cfg["model"]})
    t0 = time.perf_counter()
    from faster_whisper import WhisperModel
    model = WhisperModel(cfg["model"], device="cpu", compute_type="int8",
                         cpu_threads=cfg["cpu_threads"], download_root=cfg["model_dir"],
                         local_files_only=bool(cfg.get("offline", True)))
    model.transcribe(np.zeros(16000, np.float32), language="en", beam_size=1)  # warm-up
    emit({"type": "ready", "model": cfg["model"], "load_ms": int((time.perf_counter() - t0) * 1000)})

    q = queue.Queue(maxsize=2)          # bounded: backpressure policy = drop oldest
    cancelled = set()

    def infer_loop():
        while True:
            item = q.get()
            if item is None:
                return
            seq, t_cap_ms, pcm = item
            if seq in cancelled:
                continue
            a = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768.0
            t = time.perf_counter()
            try:
                segs, _ = model.transcribe(
                    a, language="en", beam_size=cfg["beam_size"], temperature=0.0,
                    condition_on_previous_text=False, without_timestamps=True, vad_filter=False,
                    no_speech_threshold=0.6, log_prob_threshold=-1.0, compression_ratio_threshold=2.4,
                    hotwords=cfg["hotwords"], initial_prompt=cfg["initial_prompt"])
                segs = list(segs)
                emit({"type": "result", "seq": seq, "t_cap_ms": t_cap_ms,
                      "text": " ".join(s.text.strip() for s in segs).strip(),
                      "audio_ms": len(a) * 1000 // 16000, "proc_ms": int((time.perf_counter() - t) * 1000),
                      "avg_logprob": min((s.avg_logprob for s in segs), default=None),
                      "no_speech_prob": max((s.no_speech_prob for s in segs), default=None)})
            except Exception as e:  # report, keep serving
                emit({"type": "error", "seq": seq, "code": "infer_failed", "message": str(e)})
    th = threading.Thread(target=infer_loop, daemon=True); th.start()

    while True:  # reader loop never blocks on inference, so Rust's pipe write never stalls
        hdr = read_exact(stdin, HDR.size)
        if hdr is None:
            break
        t, seq, ln = HDR.unpack(hdr)
        payload = read_exact(stdin, ln) if ln else b""
        if payload is None:
            break
        if t == T_AUDIO:
            t_cap_ms = struct.unpack_from("<Q", payload)[0]
            item = (seq, t_cap_ms, payload[8:])
            while True:
                try:
                    q.put_nowait(item); break
                except queue.Full:
                    try:
                        old = q.get_nowait(); emit({"type": "dropped", "seq": old[0], "reason": "backlog"})
                    except queue.Empty:
                        pass
        elif t == T_CANCEL:
            cancelled.add(seq)
        elif t == T_PING:
            emit({"type": "pong", "seq": seq})
        elif t == T_SHUTDOWN:
            break
    q.put(None); th.join(timeout=5)
    emit({"type": "bye"})

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        emit({"type": "error", "seq": 0, "code": "fatal", "message": repr(e)})
        raise

# Research: Local STT with faster-whisper (Phase 3 seam)

> Produced on 2026-11-01 by the `loopback-architecture` research workflow (Sonnet 5.5 teammates) while the project was still called *interview-whisper*, on the 2-core laptop. Historical input: where it differs from [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) (written after the adversarial review), the architecture wins. Absolute paths were replaced with `<scratch>`, `<repo>` or `%USERPROFILE%`.

**Question investigated:** Local STT worker (faster-whisper) seam for Phase 3: model choice, parameters, IPC protocol, supervision, packaging, question-boundary ownership

## Recommendation

Use faster-whisper 1.2.1 (CTranslate2 4.8.4, compute_type=int8, CPU) in a separate Python process, with base.en as the default and tiny.en as the fallback. I installed it on this exact machine (2C/2T Athlon Silver 10, ~1 GB free RAM) and measured. For 3.8 s, 9.6 s and 17.9 s utterances at beam_size=1, cpu_threads=2, the median latencies were: tiny.en 0.90 / 1.13 / 1.25 s; base.en 1.86 / 2.05 / 2.42 s; small.en 5.4 / 7.4 / 9.5 s. Beam 5 added roughly 0.1-1 s for tiny and base and was noisy for small. small.en is too slow (real-time factor about 0.8 for a 10 s utterance, 360-760 MB RSS) and would starve the browser call, so it is an opt-in 'accuracy' mode only. tiny.en is about 2x faster but mangled technical terms on noisy speech. Latency is almost flat in utterance length because Whisper's encoder always processes a 30 s padded window (pad_or_trim to 3000 frames), so send whole pre-segmented utterances of up to about 12 s, not tiny slices. Segmentation (energy VAD plus resample 48k stereo to 16k mono) stays in Rust and vad_filter=False in Python. Transcribe with beam_size=1, language='en', temperature=0.0 (no fallback ladder, which can multiply latency), condition_on_previous_text=False, without_timestamps=True, no_speech_threshold=0.6, log_prob_threshold=-1.0, compression_ratio_threshold=2.4, plus hotwords or initial_prompt for the C#/.NET vocabulary. IPC is a stdin/stdout pipe: binary length-prefixed frames Rust to Python, and JSON-lines Python to Rust, with stderr for logs. It is the simplest and most robust option: no ports, no firewall prompt, and the child dies with the parent when the pipe closes. I prototyped it and it works on Windows binary pipes, including as a PyInstaller exe. The Python reader thread is separate from the inference thread, so pings answer during inference, and a bounded queue drops the oldest segment when behind. In Rust, spawn with tauri-plugin-shell 2.6.0, using the same CommandEvent code path for dev and release: app.shell().command(venv_python) in dev, app.shell().sidecar('iw-stt') in release, both with set_process_group(true) and kill_on_drop(true) (Windows job object, CREATE_NO_WINDOW). Question-boundary detection should live in Rust. Rust owns the capture clock and the silence durations, so the state machine is deterministic, unit-testable with cargo test, and still runs if the webview is throttled. Python stays a stateless transcriber, so a cloud STT backend can be swapped in behind the same JSON schema. The frontend only renders, and can override manually. The heuristic is: accumulate utterances into a 'turn'; close the turn after 1200-1500 ms of silence, or after about 700 ms if the text ends in '?'; classify as a question if it has '?', an interrogative or imperative starter, or an 'I'd like you to ...' pattern. Phase 4's LLM then does the real intent check. Phase 2 only needs to commit to one thing: the audio hand-off is a stream of (seq, capture_ts_ms, 16 kHz mono PCM16 utterance) frames. Everything above is behind that seam.

## Versions checked

| Name | Version | Checked at |
|---|---|---|
| faster-whisper | 1.2.1 (uploaded 2025-10-31; requires ctranslate2&lt;5,>=4.0, huggingface-hub>=0.21, tokenizers&lt;1,>=0.13, onnxruntime&lt;2,>=1.14, av>=11, tqdm) | https://pypi.org/pypi/faster-whisper/json |
| ctranslate2 | 4.8.4 (2026-10-25; cp312 win_amd64 wheel 20.4 MB) | https://pypi.org/pypi/ctranslate2/json |
| av (PyAV) | 19.0.2 (abi3 cp312 win_amd64 wheel 28.2 MB) | https://pypi.org/pypi/av/json |
| onnxruntime | 1.31.1 (cp312 win_amd64 wheel 14.9 MB) | https://pypi.org/pypi/onnxruntime/json |
| tokenizers | 0.23.3 (abi3 win_amd64 wheel 2.9 MB) | https://pypi.org/pypi/tokenizers/json |
| huggingface_hub | 2.4.0 (pure Python, 0.9 MB; pulls hf-xet, httpx2, click, filelock, fsspec) | https://pypi.org/pypi/huggingface-hub/json |
| numpy | 2.5.4 (resolved by pip on this machine for Python 3.12) | local pip install into a venv in the scratchpad |
| PyInstaller | 6.22.3 (requires Python >=3.8,&lt;3.16) | https://pypi.org/pypi/pyinstaller/json |
| tauri (crate) | 2.12.5 | https://crates.io/api/v1/crates/tauri |
| tauri-plugin-shell (crate) | 2.6.0 (2026-10-28) | https://crates.io/api/v1/crates/tauri-plugin-shell |
| @tauri-apps/api (npm) | 2.12.4 | https://registry.npmjs.org/@tauri-apps/api/latest |
| whisper-rs (crate) | 0.16.0 (2026-03-12; repo moved to codeberg.org/tazz4843/whisper-rs) | https://crates.io/api/v1/crates/whisper-rs |
| rubato (crate) | 5.0.1 | https://crates.io/api/v1/crates/rubato |
| earshot (crate, pure-Rust VAD) | 1.2.2 | https://crates.io/api/v1/crates/earshot |
| voice_activity_detector (crate, Silero via ort) | 0.2.1 | https://crates.io/api/v1/crates/voice_activity_detector |
| win32job (crate) | 2.0.3 (not needed if tauri-plugin-shell set_process_group is used) | https://crates.io/api/v1/crates/win32job |
| Python embeddable (3.12.10 amd64) | zip is 11.1 MB | https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip |

## Verified facts

- MEASURED on the target machine (AMD Athlon Silver 10, 2C/2T, Windows 11, SAC enforced), faster-whisper 1.2.1 with ctranslate2 4.8.4, compute_type=int8, cpu_threads=2, language=en, no vad, temperature=0, synthetic Zira TTS clips, median of 3 after warm-up, whole transcribe() call including mel extraction. Beam 1, short 3.8 s / mid 9.6 s / long 17.9 s: tiny.en 0.90 / 1.13 / 1.25 s; base.en 1.86 / 2.05 / 2.42 s; small.en 5.40 / 7.42 / 9.48 s. Beam 5: tiny.en 0.94 / 1.26 / 2.22 s; base.en 2.21 / 2.78 / 3.06 s; small.en 10.3 / 7.8 / 12.5 s (noisy, other processes were running).
  - source: Local benchmark scripts in the scratchpad (bench.py, run 2026-11-01) on this machine
- MEASURED: with base.en and 1 core kept busy by a spin loop, cpu_threads=2 gave 2.7 / 3.3 / 3.6 s at beam 1 (vs 1.9 / 2.1 / 2.4 s on an idle machine). cpu_threads=1 results were noisy (base.en 2.7-7.5 s, tiny.en 1.2-2.1 s), so use 2 threads and treat 'call + webcam running' as a 1.3-2x slowdown. Run-to-run noise is large on this box (about +/-30%).
  - source: Local benchmark (bench.py with threads=1 and a concurrent busy process)
- MEASURED RAM (Python process RSS, int8): tiny.en 148 MB after load / 262 MB peak; base.en 185 / 363 MB; small.en 362 / 759 MB. Warm model load (files cached) plus import: import faster_whisper about 0.6-0.9 s; load tiny 1.8 s, base 1.3 s, small 3.6 s. First inference after load: tiny 0.95 s, base 1.8 s, small 6.1 s, so do a warm-up transcribe before sending 'ready'.
  - source: Local imp.py / bench.py runs
- MEASURED install footprint: venv with faster-whisper==1.2.1 is 289 MB (including ~13 MB pip). Largest parts: av.libs 65 MB, ctranslate2 62 MB (ctranslate2.dll 58 MB, libiomp5md.dll 2 MB), onnxruntime 48 MB, numpy 34 MB + numpy.libs 21 MB, hf_xet 10 MB, tokenizers 8 MB, huggingface_hub 8 MB. PyInstaller 6.22.3 --onedir build (--collect-all faster_whisper) is 238 MB and took 71 s to build here.
  - source: Local pip install and PyInstaller build
- All dependencies have cp312 win_amd64 (or abi3) wheels, so no compiler is needed: ctranslate2 4.8.4, av 19.0.2 (requires Python >=3.12), onnxruntime 1.31.1 (requires >=3.11), tokenizers 0.23.3. Installed pip list on this machine also pulled numpy 2.5.4, protobuf, flatbuffers, PyYAML, httpx2, click, fsspec, filelock, tqdm.
  - source: PyPI JSON for each package; local pip list
- Model files, Systran HF repos (fp16 model.bin, quantized to int8 at load): tiny.en model.bin 75.5 MB, base.en 145.2 MB, small.en 483.5 MB; each also has tokenizer.json (2.1 MB), vocabulary.txt, config.json. HF cache for the three models is about 672 MiB.
  - source: https://huggingface.co/api/models/Systran/faster-whisper-{tiny,base,small}.en?blobs=true; local du
- ctranslate2.get_supported_compute_types('cpu') on this CPU returns {int8, float32, int8_float32} (no float16/bfloat16), so int8 is the right choice. CTranslate2 docs: prebuilt x86-64 binaries include MKL and oneDNN, MKL is used on Intel CPUs and oneDNN on other x86-64 (AMD); env vars CT2_USE_MKL and CT2_FORCE_CPU_ISA exist.
  - source: Local ctranslate2 call; https://github.com/OpenNMT/CTranslate2/blob/master/docs/hardware_support.md
- WhisperModel.transcribe (non-batched) always runs pad_or_trim(segment) to 3000 mel frames (30 s) before the encoder, so encoder cost is fixed per call; only decoding grows with token count. This matches the measured near-flat latency vs utterance length.
  - source: https://github.com/SYSTRAN/faster-whisper/blob/master/faster_whisper/transcribe.py (lines around 1217) and audio.py pad_or_trim
- Signature defaults (WhisperModel.transcribe): beam_size=5, best_of=5, temperature=[0.0,0.2,0.4,0.6,0.8,1.0], compression_ratio_threshold=2.4, log_prob_threshold=-1.0, no_speech_threshold=0.6, condition_on_previous_text=True, without_timestamps=False, vad_filter=False, hotwords=None, initial_prompt=None, chunk_length=None, hallucination_silence_threshold=None. WhisperModel.__init__(model_size_or_path, device, device_index, compute_type, cpu_threads=0, num_workers=1, download_root, local_files_only). Segment objects expose avg_logprob, no_speech_prob, compression_ratio (used in my worker).
  - source: https://github.com/SYSTRAN/faster-whisper/blob/master/faster_whisper/transcribe.py
- Filtering semantics: temperature fallback is triggered when compression_ratio > threshold or avg_logprob &lt; log_prob_threshold, but it is cancelled when no_speech_prob > no_speech_threshold AND avg_logprob &lt; log_prob_threshold. A segment is skipped when no_speech_prob > no_speech_threshold and avg_logprob is not > log_prob_threshold. hallucination_silence_threshold only works with word_timestamps=True, so leave it unset. The BatchedInferencePipeline does not apply these rules.
  - source: faster_whisper/transcribe.py generate_with_fallback and generate_segments (read via WebFetch)
- hotwords are encoded as ' '+hotwords and placed after &lt;|startofprev|> in the decoder prompt (capped at max_length//2 tokens) only when no prefix is set; initial_prompt tokens are prepended to the previous-text context for each window. Both cost a few extra decoder prompt tokens. In my TTS test on clean audio both produced 'ASP.NET Core ... PostgreSQL', and 'C sharp' became 'C-sharp' (never 'C#'), so post-process 'C-sharp'/'C sharp' to 'C#' in Rust.
  - source: transcribe.py get_prompt (lines 1569-1602); local halluc.py test
- Hallucination test on base.en and tiny.en (int8): 4 s of digital silence, -45 dBFS white noise and -30 dBFS white noise all returned zero segments with both the default temperature ladder and temperature=0 plus thresholds; a 0.6 s cut-off speech fragment returned a short guess ('Tell me about that.' / 'Tell me about your'); speech with -20 dB noise: base.en correct, tiny.en wrote 'a SP.NET Core using FOSFRA SQL'. Hence base.en over tiny.en for technical vocabulary.
  - source: Local halluc.py run on this machine (synthetic, not real call audio)
- VadOptions defaults for faster-whisper's Silero VAD: threshold 0.5, min_silence_duration_ms=2000, speech_pad_ms=400, max_speech_duration_s=inf. Silero via onnxruntime is a hard dependency anyway (import chain), but vad_filter defaults to False in WhisperModel.transcribe.
  - source: https://github.com/SYSTRAN/faster-whisper/blob/master/faster_whisper/vad.py
- faster-whisper's published CPU benchmark (13 min audio, small model, 8 threads, Intel i7-12700K, beam 5): faster-whisper int8 1m42s with 1477 MB RAM, fp32 2m37s, whisper.cpp fp32 2m05s. whisper.cpp is not clearly faster, so there is no reason to adopt it.
  - source: https://github.com/SYSTRAN/faster-whisper/blob/master/README.md
- tauri-plugin-shell 2.6.0 API: Command::spawn() -> Result&lt;(Receiver&lt;CommandEvent>, CommandChild)>; CommandChild::write(&mut self, &[u8]) -> Result&lt;()> (blocking write_all to the stdin pipe), kill(self), pid(); CommandEvent is #[non_exhaustive] with Stdout(Vec&lt;u8>), Stderr(Vec&lt;u8>), Error(String), Terminated(TerminatedPayload{code: Option&lt;i32>, signal: Option&lt;i32>}); builder methods include arg/args/env/envs/current_dir/set_raw_out/set_process_group/kill_on_drop. On Windows it sets CREATE_NO_WINDOW, and with process_group it uses a suspended start plus a job object. In non-raw mode stdout events are split on '\n' or '\r'. The event channel has capacity 1.
  - source: https://raw.githubusercontent.com/tauri-apps/plugins-workspace/shell-v2.6.0/plugins/shell/src/process/mod.rs
- Tauri v2 sidecar: bundle.externalBin entries (e.g. 'binaries/iw-stt') require a file with the target-triple suffix (iw-stt-x86_64-pc-windows-msvc.exe); Rust uses app.shell().sidecar('iw-stt') with the filename only; capabilities need shell:allow-execute / shell:allow-spawn with sidecar:true when invoked from JS. A sidecar is a single executable, so a PyInstaller onedir folder with _internal/ needs to ship as bundle.resources and be spawned by path, not as a lone externalBin file.
  - source: https://v2.tauri.app/develop/sidecar/ (the onedir-vs-externalBin point is my inference from that page)
- Smart App Control (docs): in enforcement mode apps run only if recognized by Microsoft's app-intelligence service or signed by a CA in the Trusted Root Program; unknown, unsigned code is blocked by default. This machine reports VerifiedAndReputablePolicyState=1 (enforced).
  - source: https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview; local registry read
- EMPIRICAL SAC results on this machine (SAC enforced): (a) venv python.exe (PSF-signed) loads the unsigned ctranslate2.dll/libiomp5md.dll and other pip wheels fine; (b) an unsigned PyInstaller --onedir iw-stt.exe built locally ran and served the full protocol; (c) a copy of that exe with Zone.Identifier ZoneId=3 (MOTW) also started and ran. SAC did not block any of these here. SAC is reputation-based, so a downloaded or distributed build on another machine may differ.
  - source: Local experiments in scratchpad (proto/dist/iw-stt, iw-motw.exe)
- PROTOTYPE VERIFIED on Windows: a 9-byte-header binary frame protocol on stdin plus JSON-lines on stdout works with sys.stdin.buffer and an os.dup/os.dup2 redirect of fd 1 to stderr (protects the channel from stray prints). The reader thread answered a ping while inference was running; a bounded queue (maxsize 2) emitted 'dropped' for the oldest queued segments under overload; shutdown frame gives clean exit code 0. It works both as 'python -I worker.py' and as the PyInstaller exe. First run of the exe: hello at 2.75 s, ready at 9.0 s (base.en, cold); the warm venv run took 2.5 s to ready.
  - source: Local prototype iw_stt_worker.py and client.py in scratchpad/proto
- whisper-rs 0.16.0 wraps whisper.cpp; features cuda, hipblas, openblas, metal, vulkan are off by default. Its repo has moved to Codeberg. Its BUILDING.md lists, for Windows with MSVC: Visual Studio C++, cmake and LLVM (clang) on PATH (or an MSYS2/mingw route). None of these are installed on this machine.
  - source: https://docs.rs/crate/whisper-rs/0.16.0; https://codeberg.org/tazz4843/whisper-rs BUILDING.md (README of the GitHub mirror)

## API sketch (research-time, superseded by ARCHITECTURE.md where they differ)

```text
## Python: worker transcribe call (verified against faster-whisper 1.2.1 signature; prototype ran this)
```python
model = WhisperModel(cfg["model"], device="cpu", compute_type="int8",
                     cpu_threads=2, download_root=cfg["model_dir"], local_files_only=True)
model.transcribe(np.zeros(16000, np.float32), language="en", beam_size=1)   # warm-up BEFORE emitting "ready"
segs, _ = model.transcribe(
    audio_f32_16k,                  # np.float32 [-1,1]; int16 -> /32768.0
    language="en",                  # .en models: skip language detection
    beam_size=1,                    # beam 5 gave ~no accuracy win on clean audio here, +0.1..1 s
    temperature=0.0,                # single pass; a [0..1] ladder can re-decode up to 6x
    condition_on_previous_text=False,   # utterances are independent; avoids repetition loops
    without_timestamps=True,        # fewer decode tokens; Rust owns timing
    vad_filter=False,               # Rust already segmented
    no_speech_threshold=0.6, log_prob_threshold=-1.0, compression_ratio_threshold=2.4,
    hotwords="C#, .NET, ASP.NET Core, React, Next.js, PostgreSQL, Docker",  # or initial_prompt=; make it user-editable
)
text = " ".join(s.text.strip() for s in segs)   # generator: iterate to run. Also drop if min(avg_logprob) < -1.0 or compression_ratio > 2.4
```
## Wire protocol v1 (stdin = binary frames, stdout = JSON lines, stderr = logs)
Rust to Python frame: `[u8 type][u32 LE seq][u32 LE len][payload]` (9-byte header).
- 0x01 CONFIG (first frame): JSON {"model":"base.en","model_dir":"...","cpu_threads":2,"beam_size":1,"hotwords":"...","initial_prompt":null,"offline":true}
- 0x02 AUDIO: payload = u64 LE capture_start_ms + PCM s16le mono 16 kHz (seq = utterance id; max about 12 s = 384 KB)
- 0x03 CANCEL(seq), 0x04 PING(seq), 0x7F SHUTDOWN
Python to Rust, one JSON object per line (ensure_ascii=True so no raw \n or \r inside):
- {"type":"hello","proto":1,"pid":N}, {"type":"loading","model":..}, {"type":"ready","model":..,"load_ms":..}
- {"type":"result","seq":N,"t_cap_ms":..,"text":..,"audio_ms":..,"proc_ms":..,"avg_logprob":..,"no_speech_prob":..}
- {"type":"dropped","seq":N,"reason":"backlog"}, {"type":"error","seq":N,"code":"infer_failed|fatal|model_missing","message":..}, {"type":"pong","seq":N}, {"type":"bye"}
Backpressure: Python reader thread drains stdin at all times; the inference queue is bounded to 2 (drop oldest, emit 'dropped'). Rust also keeps at most 2 utterances in flight (else it merges adjacent utterances or drops the oldest) and writes from a dedicated thread, because CommandChild::write is a blocking write_all.
Ready gate: UI shows 'Whisper: loading' until 'ready'; Rust must not send AUDIO before it (or it just queues in the pipe).
## Rust: spawn and supervise (tauri 2.12.5 + tauri-plugin-shell 2.6.0)
```rust
use tauri_plugin_shell::{ShellExt, process::{CommandEvent, CommandChild}};
let cmd = if cfg!(debug_assertions) {
    // dev: venv python (PSF-signed); -I = isolated mode
    app.shell().command(venv_python).args(["-I", worker_py])   // `command()` signature not re-verified, see unverified
} else {
    app.shell().sidecar("iw-stt")?                              // or spawn the onedir exe by path from resources
};
let (mut rx, mut child) = cmd.set_process_group(true).kill_on_drop(true).spawn()?;  // job object on Windows
// writer thread owns `child`: std::sync::mpsc::sync_channel::<Vec<u8>>(4); loop { child.write(&frame)?; }
// reader: while let Some(ev) = rx.recv().await { match ev {
//   CommandEvent::Stdout(line) => serde_json::from_slice::<WorkerMsg>(&line) -> dispatch/emit,
//   CommandEvent::Stderr(l) => log::debug!, CommandEvent::Error(e) => log,
//   CommandEvent::Terminated(p) => restart policy (backoff 1,2,4,8,15 s; give up after 5 in 60 s and surface 'Whisper crashed')
//   _ => {} } }   // CommandEvent is #[non_exhaustive]
fn frame(t: u8, seq: u32, payload: &[u8]) -> Vec<u8> { let mut v = Vec::with_capacity(9 + payload.len());
  v.push(t); v.extend_from_slice(&seq.to_le_bytes()); v.extend_from_slice(&(payload.len() as u32).to_le_bytes()); v.extend_from_slice(payload); v }
```
Watchdog: if an in-flight utterance has no 'result' after max(15 s, 4x audio) kill and restart; ping every 5 s (pong is answered during inference, so it is a liveness check, not progress). Shutdown: send 0x7F, wait up to 3 s, then child.kill(). Parent crash: stdin EOF makes the worker exit, and the job object kills it too.
## Rust-owned segmentation and question-boundary state machine
- Pipeline: loopback f32 48k stereo -> mono -> rubato 48k to 16k -> energy/VAD (frame 20-30 ms, hangover) -> utterance [pre-roll 250 ms, min 0.5 s, force-cut at 12 s at the quietest recent frame, end-of-utterance silence 500-700 ms] -> AUDIO frame.
- Turn assembly: append each 'result' text to the current turn; turn closes when silence since the last utterance end >= T_end (1200-1500 ms; 700 ms when the text ends with '?'), or when a new utterance starts after T_end.
- is_question(turn) if any of: contains '?'; first words match interrogative starters (what|why|how|when|where|who|which|can|could|would|do|does|did|is|are|have|has|will|should); imperative or prompt patterns (tell me about|walk me through|describe|explain|talk about|give me an example|what's your experience|I'd like you to|let's talk about|can you|could you|how would you|how do you|what would you); also strip fillers ('so', 'okay', 'alright', 'um') before matching. Score = punctuation (+0.5) + starter (+0.3) + imperative (+0.3) + trailing silence >= T_end (+0.2); emit TurnClosed{id, text, is_question, score, silence_ms}. The UI shows it, the user can force 'Answer now' from the manual input, and the Phase 4 LLM prompt receives the whole turn plus the previous 2 turns as context.
- Post-process: map 'C-sharp|C sharp' to 'C#', 'dot net' to '.NET', 'post gress|postgres sql' to 'PostgreSQL' (small regex table in Rust; user-extendable).
```

## Alternatives considered

| Option | Verdict |
|---|---|
| Localhost WebSocket (e.g. FastAPI/websockets in Python, tokio-tungstenite 0.30.0 in Rust) | Rejected as primary. It needs port allocation and auth (any local process could connect), may trigger a Windows Firewall prompt, adds a heavier dependency set and needs its own liveness/orphan handling. Its only advantage is easier multi-client debugging, which a --debug flag on the pipe worker can cover. |
| Localhost HTTP (FastAPI/speaches style) with POST /transcribe | Rejected. Per-request overhead and base64/multipart for 100-400 KB audio, no natural streaming of ready/progress events without SSE, same port/firewall/auth concerns, and orphan processes if the parent dies. Fine for cloud-style backends later, not for the local seam. |
| stdin/stdout, length-prefixed binary frames in + JSON-lines out (recommended) | Adopted. Prototype verified on this machine, including as a PyInstaller exe; zero ports; the pipe closing gives parent-death detection; maps 1:1 to tauri-plugin-shell CommandEvent; audio bandwidth is only 32 KB/s. Needs a blocking-write thread on the Rust side and fd-1 protection on the Python side (both done in the prototype). |
| tauri-plugin-shell sidecar vs std::process::Command / tokio::process | Use tauri-plugin-shell 2.6.0 for both dev (command(venv_python)) and release (sidecar or spawn by path), because it already provides CREATE_NO_WINDOW, a Windows job object (set_process_group/kill_on_drop) and a uniform CommandEvent stream. Fall back to std::process::Command plus the win32job 2.0.3 crate if the plugin's capacity-1 channel or line splitting ever gets in the way (use set_raw_out(true) to bypass line splitting). |
| faster-whisper built-in Silero vad_filter=True | Not by default. Rust already needs energy/VAD segmentation for turn boundaries, and VadOptions default min_silence_duration_ms=2000 and speech_pad_ms=400 are not tuned for 3-10 s pre-cut utterances. Keep it as an optional flag if energy VAD proves too noisy on real call audio. onnxruntime is a hard install dependency regardless. |
| small.en as default | Rejected on this CPU: 5-9.5 s per utterance at beam 1 (up to 12.5 s at beam 5) and up to 759 MB RSS with ~1 GB free RAM; the call and webcam would starve it. Offer as an opt-in 'accuracy' mode for short utterances only. |
| tiny.en as default | Fallback only. About 2x faster (0.9-1.25 s), 148-262 MB RSS, but it garbled 'ASP.NET' and 'PostgreSQL' on noisy speech in my test. Auto-switch to it when the queue backlog is persistent (for example 3 consecutive utterances with proc_ms > audio_ms) or when RAM is low. |
| whisper.cpp via whisper-rs 0.16.0 in-process | Note, do not adopt. It would remove Python and ~240 MB of runtime, but needs VS C++ build tools + CMake + LLVM/clang on Windows (none installed), ggml model files, and an in-process crash takes down the whole app. Published numbers show no clear speed win over faster-whisper int8 (README: whisper.cpp fp32 2m05s vs faster-whisper int8 1m42s for the small model). Revisit only if packaging the Python runtime becomes a problem; the JSON schema keeps this swappable. |
| Cloud STT as an optional pluggable backend (e.g. Deepgram, AssemblyAI, OpenAI transcription) | Keep the same 'result' JSON schema so a backend can be swapped in behind the worker or a Rust trait, enabled only if the user opts in and a key is provided. Sends the interviewer audio off-device, so it needs explicit consent in the UI. Pricing, latency and model names not verified here. |
| PyInstaller --onefile | Avoid: it extracts ~240 MB to a temp dir on every start (slow cold start with AV scanning). Use --onedir (238 MB) shipped as a resources folder, or an embeddable Python 3.12.10 (11.1 MB zip) plus a pip-installed site-packages folder, which keeps the PSF-signed python.exe. |
| Question-boundary in Python or in the frontend | Rejected. Python lacks the capture clock and silence duration (and should stay stateless/swappable); the webview can be throttled when hidden or minimized during a call and is not unit-testable in the same way. Rust owns it, the UI is a renderer plus manual override, and the Phase 4 LLM does the final intent check. |

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Real call audio (browser WebRTC codec, 48k stereo downmixed, AI-voice interviewer, overlapping webcam/call load) is worse than my clean TTS tests, so WER and latency may be 1.3-2x worse. The 2 cores are shared with the browser, Tauri webview and Rust app. | high | Make the model and cpu_threads configurable at runtime; ship a self-test button that runs a bundled WAV through the worker and reports proc_ms; auto-fallback to tiny.en on sustained backlog; keep the Rust-side in-flight limit of 2 and merge/drop policy. Collect real recordings in Phase 3 acceptance tests. |
| Memory pressure: only 0.3-1 GB free RAM. Python+base.en peaks about 363 MB (small.en 759 MB), plus the webview, Rust and the user's browser; paging would cause multi-second stalls. | high | Default to base.en (or tiny.en when free RAM &lt; ~500 MB, check GlobalMemoryStatusEx before loading), load only one model at a time, free the worker when capture is stopped (optional unload after N min idle), show RAM use in the status bar. |
| Smart App Control on other machines or after distribution may block the unsigned PyInstaller exe, the unsigned Tauri exe and unsigned DLLs (ctranslate2.dll is NotSigned). Here (SAC enforced) none were blocked, including one with MOTW, but SAC is reputation-based. | medium | Dev: use venv python.exe (PSF-signed) via -I. Release: prefer embeddable Python plus site-packages (keeps signed python.exe) or sign the Tauri exe and sidecar with a trusted certificate; provide a clear 'worker failed to start (code ...)' error with the exe path. Re-test the final installer on a clean SAC-on VM. |
| Whisper hallucination on near-silence or cut-off segments (e.g. 'Thank you.' or invented continuations on 0.6 s fragments) can trigger bogus questions. In my tests silence/noise produced nothing, but short speech fragments produced plausible guesses. | medium | Rust drops utterances shorter than 0.5 s or with RMS below a floor; discard results with no_speech_prob > 0.6, avg_logprob &lt; -1.0, compression_ratio > 2.4, or text matching a small blocklist ('Thank you.', 'Thanks for watching', repeated n-grams); condition_on_previous_text=False; never feed a turn made only of filtered text into Phase 4. |
| Pipe protocol pitfalls: stray prints/library output on stdout corrupt JSON-lines; CommandChild::write blocks when the worker is busy; tauri-plugin-shell splits on '\r' as well as '\n'; the event channel capacity is 1 so a slow Rust consumer stalls the reader thread. | medium | Worker redirects fd 1 to stderr after dup'ing the protocol pipe (done in prototype); ensure_ascii JSON; dedicated Rust writer thread with a bounded channel; consume the CommandEvent receiver in its own task and forward through an unbounded channel; consider set_raw_out(true) with your own line buffer. |
| Cold-start latency and model delivery: the first PyInstaller run took about 9 s to ready and small.en load alone took 55 s on first download; HF download needs network and writes to the user cache unless download_root is set. | low | Show loading state from hello/loading; fetch models on first run into %LOCALAPPDATA%\&lt;app>\models with a progress UI (or bundle tiny/base), then always run with local_files_only=True and an explicit model_dir; run a warm-up transcribe before ready. |
| Dependency churn: faster-whisper 1.2.1 is from 2025-10-31 while its deps move quickly (huggingface_hub 2.x, numpy 2.5, av 19). Unpinned installs can break. | low | Pin exact versions (faster-whisper==1.2.1, ctranslate2==4.8.4, av==19.0.2, onnxruntime==1.31.1, tokenizers==0.23.3, huggingface_hub==2.4.0, numpy==2.5.4) and keep a pip freeze lockfile; test import in CI. |
| Non-English interviewers (PT-BR) would not work with .en models; the spec mentions base.en/small.en only. | low | Expose a language field in CONFIG; if not 'en', load the multilingual base/small and drop the .en assumption (slower, less accurate). Confirm with the user whether interviews are English only. |

## Not verified at research time

- Transcription accuracy (WER) of tiny.en/base.en/small.en on real browser-call audio with accents, codec artifacts, an AI-voice interviewer and overlapping speech; my tests used clean Microsoft Zira TTS plus synthetic white noise only.
- Exact latency under real concurrent load (browser call + webcam + Tauri webview). I only simulated one busy core; run-to-run noise was about +/-30% and the cpu_threads=1 results were inconsistent.
- Whether tauri-plugin-shell's app.shell().command(path) is callable from Rust without a capability scope entry and what its exact signature is; I verified Command/CommandChild/CommandEvent from the 2.6.0 source but not the ShellExt::command signature or scope behaviour for Rust-side calls.
- Whether a Tauri bundle.resources folder (PyInstaller onedir with _internal/) can be spawned by path via the plugin without sidecar naming; that is my inference from the sidecar docs, not tested.
- Smart App Control behaviour for a downloaded/installed release build on a different machine or a clean VM; SAC reputation can change over time, and my ZoneId=3 test covered only the exe, not every DLL.
- Whether VerifiedAndReputablePolicyState = 1 always means enforcement (widely documented registry semantics, but I did not fetch the Microsoft page for it; the task text says SAC is ON).
- Code-signing options, costs and whether signing is required for the Tauri exe on SAC-enforced machines (not researched).
- Accuracy of the question-boundary heuristics and the best silence thresholds (700 ms and 1200-1500 ms are design estimates; tune with real interview recordings). Whisper .en models emitted '?' for 'Can you walk me through ... ?' in my 3 clips, but imperative questions ('Tell me about ...') ended in '.', so the starter list is needed.
- Pre-quantized int8 CTranslate2 models (smaller downloads and faster load) via ct2-transformers-converter; not tested. Systran repos hold fp16 weights that are quantized at load.
- Whether distil-whisper small.en or turbo variants are faster/better on this CPU (not tested).
- Cloud STT vendors' current pricing, model names and streaming latency (not researched).
- Whether stubbing the 'av' dependency (65 MB av.libs) is viable when passing numpy arrays: faster_whisper.audio imports av at module import time, so a stub would be needed; not tested and not recommended.
- Embeddable-Python route (python-3.12.10-embed-amd64.zip, 11.1 MB) working with pip-installed ctranslate2/onnxruntime (needs site-packages added via the ._pth file); not tested, only the venv and PyInstaller routes were run.
- win32job / windows-sys usage details if the plugin is replaced by std::process::Command; versions verified, API not exercised.

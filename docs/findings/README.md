# Findings

Everything that was measured, researched or reviewed before writing application code. These files are evidence: they are not edited after the fact (add a new dated file instead). Where a research report disagrees with [`../ARCHITECTURE.md`](../ARCHITECTURE.md), the architecture (written after the adversarial review) wins.

| File | What it contains |
|---|---|
| [`phase1-diagnostics-laptop.md`](phase1-diagnostics-laptop.md) | Phase 1 on the 2-core laptop: machine, audio endpoints, mix format, loopback proof, buffer stress, 240 s idle trace (device position vs QPC), OS format conversion, process loopback, Smart App Control evidence, faster-whisper CPU benchmark |
| `phase1-diagnostics-h0wzy-pc.md` | *To be written in Phase 0 on h0wzy-pc* (SPEC section 6.0) |
| [`research-wasapi-rust.md`](research-wasapi-rust.md) | WASAPI loopback from Rust: crate choice (`wasapi` vs `windows` vs `cpal`), loopback semantics, notifications, no-mic guarantee |
| [`research-dsp-vad.md`](research-dsp-vad.md) | Resampling, metering, lock-free ring buffer, VAD options, CPU budget |
| [`research-tauri-devenv.md`](research-tauri-devenv.md) | Tauri 2 versions and APIs, frontend stack (Vite, Tailwind v4, shadcn), Windows toolchain, Smart App Control |
| [`research-whisper-stt.md`](research-whisper-stt.md) | faster-whisper on CPU, transcription parameters, worker IPC protocol, question-boundary heuristics |
| [`architecture-review.md`](architecture-review.md) | The three adversarial reviews of DRAFT v1 (Windows audio, crates/APIs, requirements/feasibility): 32 findings with evidence, and the resolution of each |
| [`evidence/`](evidence/README.md) | Probe sources, raw outputs and benchmark scripts behind the numbers above (research artifacts, not app code) |

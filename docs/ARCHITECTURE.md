# Interviewer Copilot - Architecture Specification

> **Status:** PROPOSAL v2 (post-review), 2026-11-01, **pending Marcos's approval**. Supersedes DRAFT v1. No Phase 2 code is written before approval (see `CLAUDE.md` rule 4 and `docs/SPEC.md` P-1). Marcos's answers to open questions 2, 3 and 7 are folded in (section 9).
> **Project rename and new dev box (2026-11-01):** the project is now *interviewer-copilot* (identifiers below use the `ic-` prefix). All measurements here come from the 2-core laptop where Phase 1 ran. Development continues on **h0wzy-pc** (Ryzen 7 5800X, 32 GB RAM, RTX 3050 8 GB): the design stays CPU-light because the laptop remains the performance floor, the laptop-specific build settings of section 7 are marked as such, and [`docs/SPEC.md`](SPEC.md) section 2 lists what must be re-measured there. The task order and acceptance criteria live in [`docs/SPEC.md`](SPEC.md).
> **Readers:** Marcos (approver) and the agents that implement from it. In this document 'you' is Marcos, 'I' is the agent that wrote it, and 'this box', 'this machine' and 'here' mean the 2-core laptop; on another machine replace those measured values by that machine's Phase 0 data. The document is self-contained; the Appendix records how each of the 32 review findings was decided.
> **Verification:** package versions were re-checked on 2026-11-01 (crates.io, npm, winget, rust-lang.org). Rust code below is a **skeleton, not compiled** (no toolchain on this box yet); `wasapi` 0.26.0 signatures were cross-read from its source. The Timeline design (3.3) rests on a 240 s trace measured on this machine; Windows behaviour claims cite Microsoft Learn. Anything not verified is tagged *(unverified)*.
> **Out of scope:** hiding the app window from screen capture or proctoring tools.

## 0. TL;DR

1. Windows-only **Tauri 2** app. The Rust core owns all audio; the React UI only renders state and sends commands.
2. Capture = **WASAPI shared-mode loopback on an Active *render* endpoint** via crate `wasapi =0.26.0` (+ `windows 0.62.2`, the version Tauri already builds). One dedicated MTA thread owns every long-lived COM object.
3. Event wait with a 20 ms timeout (the event is a hint, never the clock), **1 s endpoint buffer** (measured here: a 22 ms buffer lost 38-174 ms of audio per 7.4 s under 2x CPU oversubscription; 1 s lost none), device mix format as-is (48 kHz f32 stereo).
4. The capture thread only reads, zero-fills SILENT packets and *measured device-position holes*, and pushes f32 into a wait-free `rtrb` ring. It never deletes audio and never edits PCM to chase QPC stamps (measured here: stamps run about 70 ppm ahead of the sample clock and bunch by up to 9 ms). A DSP thread does meter -> mono -> `rubato` 48->16 kHz -> i16 -> history/sink (about 0.4 % of one core, modelled). Both threads join MMCSS "Audio".
5. "Never a microphone" is **structural**: `RenderEndpoint` newtype, exactly one `Direction::Capture` token in the crate, an OS-queried data-flow check right before `Initialize`, typed errors, no fallback path. The source scan is a lint; the runtime checks are the guarantee.
6. The UI receives a 25 Hz level frame over a Tauri `Channel` (2 Hz while the window is hidden); rare events use `emit`. Errors are a serde-tagged union mirrored in TypeScript.
7. Phase 3: Python `faster-whisper 1.2.1`, `base.en` int8 (measured ~2 s per utterance here with 2 threads) as a low-priority child process over stdin/stdout; Rust does VAD (`earshot`), segmentation and question boundaries.
8. Phase 4: Rust-side Anthropic Messages SSE client (raw HTTP, API version `2026-10-21`), key in Windows Credential Manager; the model is chosen in Phase 4.
9. Top risks: Smart App Control vs unsigned cargo outputs (Plan B first; switching it Off is effectively one-way); 2 cores + ~0.3-0.7 GB free RAM shared with a browser call; first build 15-40 min (estimate); sleep/resume and Bluetooth behaviour untested.
10. Decided by you (2026-11-01): English-only interviews (`.en` models); outputs = laptop speakers **and** a Bluetooth headset, so the Bluetooth gate test M11 is required; an Anthropic key is available (about US$120 of credit). Still needs you: approval, install authorization, Smart App Control (Plan B by default) and the gate tests at the keyboard (section 9).

### Decision table

| Component | Choice | Version | Why |
|---|---|---|---|
| Shell | Tauri 2 | `tauri` 2.12.5 (MSRV 1.90), `tauri-build` 2.7.4, `@tauri-apps/api` + `cli` 2.12.4 | spec; WebView2 156 already installed |
| Rust toolchain | stable MSVC, pinned in `rust-toolchain.toml` | 1.99.1 via rustup 1.29.1 | Tauri needs >= 1.90; a pin avoids surprise toolchain swaps under SAC |
| Frontend | React + TypeScript + Vite | React ^19.1 (template), TS ~6.0.3 (no `baseUrl`, section 7), Vite 8.3.5 | spec says React 18+; keep the template's validated pins |
| UI kit | Tailwind v4 + shadcn + lucide | `tailwindcss` and `@tailwindcss/vite` 4.3.3, `shadcn` CLI 4.23.0, `lucide-react` 1.66.0 | spec |
| WASAPI binding | `wasapi` crate, exact pin | `=0.26.0` (MIT, MSRV 1.85) | only candidate with ID/name enumeration, render check, event loopback, per-packet flags + device position + QPC, IMMNotificationClient; `unsafe` for us confined to `render_only.rs` and `platform.rs` (about 60 lines: OS-queried flow check, StableId read, MMCSS, power-throttling opt-out); `cpal` rejected (INFINITE wait, ignores SILENT) |
| Win32 extras | `windows` | 0.62.2, features `Win32_Foundation`, `Win32_Media_Audio`, `Win32_System_Threading`, `Win32_System_Performance`, plus what the StableId read needs (expected `Win32_UI_Shell_PropertiesSystem`, `Win32_System_Com_StructuredStorage`, `Win32_System_Variant`; confirm with `cargo check`) | QPC, MMCSS, power-throttling opt-out, HRESULT consts, IMMEndpoint; same version as Tauri's tree, so one build |
| Capture->DSP handoff | `rtrb` SPSC ring | 0.4.1 | wait-free, no deps, `push_entire_slice` all-or-nothing |
| Resampler | `rubato` `Fft<f32>` | 5.0.1 (re-exports `audioadapter_buffers` 5.2: no direct dependency needed) | exact 768-in/256-out chunks at 48 kHz, generic for other rates; `dasp` has no anti-alias filter |
| VAD (Phase 3) | `earshot` | `=1.2.2` | pure Rust, ~95 KiB, 256-sample frames; no ONNX Runtime |
| UI streaming | `tauri::ipc::Channel<T>` | tauri 2.12.5 | ordered and fast; events are documented as not for throughput |
| Errors / logs | `thiserror`, `log`, `tauri-plugin-log` | 2.0.21, 0.4, 2.10.1 | `wasapi` already logs through `log` |
| STT (Phase 3) | faster-whisper `base.en`, int8, beam 1, `cpu_threads` configurable (measured with 2) | 1.2.1 / ctranslate2 4.8.4 | measured on this CPU (section 4) |
| STT transport | stdin binary frames + stdout JSON lines | `tauri-plugin-shell` 2.6.0, `tauri-plugin-single-instance` 2.5.5 | no ports/firewall; pipe close = parent-death detection |
| LLM (Phase 4) | Anthropic Messages API, raw HTTP SSE | `anthropic-version: 2026-10-21`, `reqwest` 0.13.5 | no official Rust SDK |
| Secrets | Windows Credential Manager | `keyring-core` 1.0.1 + `windows-native-keyring-store` 1.1.0 | key typed once into the app, never stored in or returned to JS |
| Prefs (non-secret) | `tauri-plugin-store` | 2.5.0 | persona text, model id, UI state |
| Build tools | VS Build Tools (C++ x64 + Win11 SDK 26100), Node LTS | 18.11.2, 26.13.0 (winget) | Tauri prerequisites |
| Diagnostics | PowerShell + Python stdlib | `tools/diagnostics/{audio-probe,play-tone}.ps1`, `check_wav.py` | no extra installs; deterministic test tone for the gate |

### Glossary (for readers new to Windows audio)

| Term | Meaning here |
|---|---|
| Render endpoint | A Windows *output* device (speakers, headphones, HDMI). A *capture* endpoint is an input (microphone) and is never used. |
| Loopback | WASAPI mode that records what a render endpoint is playing (the mix of all apps) instead of listening to a microphone. |
| Shared mode | Normal Windows mixing where many apps share one device. Loopback works only here. |
| MTA / STA | COM threading models. `ic-wasapi` is MTA (free-threaded); Tauri's UI thread is STA. |
| Device position (`index`) | Per-packet frame counter of the stream, "the number of audio frames from the start of the stream". The authority for continuity: a hole in the audio is a jump in this counter. |
| QPC | QueryPerformanceCounter: the high-resolution wall clock. WASAPI also stamps each packet with a QPC time (100 ns units), but that stamp is a *sampled correlation*: it drifts about 70 ppm against the sample clock and bunches under load (3.3). It is metadata only; it is never used to insert or delete audio. |
| MMCSS | Multimedia Class Scheduler: lets an audio thread ask Windows for better scheduling; also exempts it from the Low-QoS treatment of occluded windows. |
| SPSC ring | Single-producer / single-consumer ring buffer: capture thread -> DSP thread without locks. |
| Resync | The capture loop saw a hole longer than 500 ms (stall, sleep/resume, engine restart); it does not synthesize it, it reopens the stream with a new generation. |
| VAD | Voice activity detection: finds when someone speaks so audio can be cut into utterances. |
| SSE | Server-sent events: how the Anthropic API streams tokens over HTTP. |

## 1. Constraints

| # | Constraint | Design consequence |
|---|---|---|
| R1 | Capture only audio leaving Windows (render endpoint, WASAPI loopback). Never a microphone. | `RenderEndpoint` newtype is the only way to open a stream; one `Direction::Capture` token in the crate; capture endpoints are never enumerated; an OS-queried data-flow check sits right before `Initialize`; the webview has no `getUserMedia` (3.4). |
| R2 | Loopback/device failure -> explicit error, never a silent switch to an input device. | Typed `AudioError`; the typed cause stays visible in the UI for as long as the problem lasts. Recovery retries only the same *kind* of target (a render endpoint), indefinitely, in a visible `Waiting` state; `Failed` is reserved for non-retryable errors and needs a user action (3.3, 3.8). The app never changes the user's selected target on its own. |
| R3 | Phase gate: Phase 2 proven with real desktop audio before any Whisper/Anthropic code. | Gate with pass criteria (3.9). Sections 4-5 are seams only; no code until you confirm. |
| E1 | 2 cores / 2 threads (Athlon Silver 10, Zen 2) shared with a browser call and webcam. | Two long-lived app threads (+ a short-lived device-list worker); capture thread = read, convert, push; DSP chain modelled at ~0.4 % of one core; Whisper runs at low priority with a configurable thread count. |
| E2 | 7.3 GB RAM, ~0.3-0.7 GB free, heavy paging. | Audio buffers < 3 MB total; no ONNX Runtime; `base.en` int8 (185-363 MB); one worker process. |
| E3 | No CUDA (Radeon iGPU only). | CPU int8 inference; no GPU code paths. |
| E4 | Smart App Control enforced; it blocked 1 of 10 freshly compiled unsigned exes here; `cargo.exe` was blocked elsewhere (rust-lang/rust#160163). | A *representative* Step-0 gate (build scripts, proc-macros, several fresh exes, the npm native binaries, the CodeIntegrity log), pinned toolchain, keep Microsoft-signed `link.exe`, your decision needed (7, 9). |
| E5 | Default render mix format 48 kHz / 2 ch / f32 (extensible, mask 0x3); period 10 ms (min 3 ms). | Native path with ~480-frame packets; 48->16 kHz is an exact 3:1; engine still handles any rate/channel count (Bluetooth HFP is 8 or 16 kHz mono). |
| E6 | One ACTIVE render endpoint, "Speakers (Realtek(R) Audio)"; Headphones jack UNPLUGGED; no Bluetooth yet; default capture "Microphone Array (AMD Audio Device)" must never be opened. Windows 11 (build 26200): Bluetooth A2DP and HFP share one unified endpoint (Learn). | Follow-default vs pinned modes and hot-plug are first-class; plug/unplug is a gate test. Bluetooth is a **required** path (Marcos will use a Bluetooth headset as well as the speakers, Q3) that is still untested here because no headset is paired yet: M11 is part of the gate. |
| E7 | Loopback is the mix of *all* apps on that endpoint (Spotify, Steam, notifications). | UI hint + OS-meter cross-check; optional process-loopback mode later (3.3). |
| E8 | **Measured here** (Realtek, Win 11 26200, 240 s, every render session idle, `LOOPBACK + EVENTCALLBACK`, 1 s buffer): the stream keeps delivering ~100 packets/s of all-zero, unflagged packets (0 SILENT flags, 0 wait timeouts, peak 0.0). **Unverified** for other endpoint classes (HDMI, USB, Bluetooth); Chromium and OBS code indicate that packets or events may stop there. | The loop never depends on packets or events arriving; "silence" is defined by level only; `packetsFlowing` is a 200 ms watchdog; an idle-credit mechanism keeps the timeline continuous if packets stop; M7 is run per endpoint class and records what each class really does. |
| E9 | Battery laptop (battery present, discharging, Balanced plan when probed on 2026-11-01); the app window will sit behind the call window. Learn's QoS table gives Low QoS (on battery: most efficient CPU frequency) to windowed processes that are minimized or fully occluded and to their child processes; only Audible processes and MMCSS audio threads are exempt. | MMCSS on both audio threads, process-wide power-throttling opt-out, STT child opted out too; M8 runs occluded, on battery and on AC. |

## 2. System overview

```
  Windows audio engine (audiodg.exe): mix of ALL apps on the render endpoint
        |  H1  WASAPI loopback, shared, event + 20 ms timeout, 1 s buffer
        |      f32 LE interleaved, 48 kHz, 2 ch (device mix), ~480-frame packets
        v
+----------------------- interviewer-copilot.exe : Tauri 2 / Rust process -----------------------+
|                                                                                                |
|  [T1 ic-wasapi, MTA]  enumerator, IMMNotificationClient, Loopback, Timeline(device pos), MMCSS |
|        |  H2  rtrb SPSC ring (2 s): interleaved f32 @ device rate, measured holes zero-filled  |
|        v                                                                                       |
|  [T2 ic-dsp, MMCSS]  meter(raw) -> downmix -> rubato 48->16k -> i16 -> PcmHistory -> FrameSink |
|        |                                             |                                         |
|        | H4  LevelFrame 25 Hz                        | H5  Pcm16Frame (256 x i16 = 16 ms)      |
|        |                                             v                                         |
|        |                      [Phase 3] Segmenter(earshot) -> Utterance <= 12 s                |
|        |                                             |  H6  stdin frames [type][seq][len][pcm] |
|  [tokio workers] commands, Phase 4 HTTPS client      |                                         |
|  [ic-devlist, short-lived MTA] device list / Refresh |                                         |
|  [main thread, STA] window + WebView2 host           |                                         |
|                                                                                                |
+------+------------------+-------------------+--------------------------------------------------+
       |                  |                   |  ^
       | invoke/Channel   | H8 HTTPS (P4)     |  | H7 stdout JSON lines
       v (ordered JSON)   v                   v  |
 WebView2 + React UI api.anthropic.com    ic-stt child process (Phase 3)
 (renders state only; no audio,           faster-whisper base.en; stateless;
  no keys, no product logic)              BELOW_NORMAL priority
```

| Hop | From -> To | Format / rate | Mechanism | Volume |
|---|---|---|---|---|
| H1 | audiodg -> T1 | f32 LE interleaved, 48 kHz, 2 ch, ~480 frames / 10 ms | WASAPI loopback, 1 s buffer, event + 20 ms timeout | 384 KB/s |
| H2 | T1 -> T2 | same + zero-filled holes (<= 500 ms each); one stream = one fixed (rate, ch) | `rtrb` 2 s (768 KB), drop + count on full | 384 KB/s |
| H3 | inside T2 | mono f32 48k -> mono f32 16k (768 -> 256 frames) -> i16 | rubato `Fft`, round + saturate | 32 KB/s out |
| H4 | T2 -> UI | `LevelFrame` JSON ~130 B, 25 Hz (2 Hz while the window is hidden) | `Channel<CaptureEvent>` | ~3.3 KB/s |
| H5 | T2 -> history / sink | 16 kHz mono i16 into `PcmHistory` (10 s in Phase 2); `Pcm16Frame` 256 x i16 for the sink (Phase 3 seam, optional in Phase 2) | trait call on T2 | 32 KB/s |
| H6 (P3) | Segmenter -> STT | `Utterance` <= 12 s, PCM16 16 kHz mono (<= 384 KB) | stdin binary frames | ~32 KB/s while speaking |
| H7 (P3) | STT -> Rust -> UI | JSON lines -> `TranscriptEvent` | stdout; `Channel` | text |
| H8 (P4) | Rust -> Anthropic | JSON request; SSE response | HTTPS (reqwest) | text |
| H9 (P4) | Rust -> UI | `AnswerEvent` deltas | `Channel` | text |

| Thread / process | Apartment | Owns | Lifetime |
|---|---|---|---|
| main (tao) | STA | window, WebView2 host | app |
| tokio workers (Tauri async runtime) | - | commands, Phase 4 HTTP | app |
| `ic-wasapi` | MTA (`wasapi::initialize_mta`, explicit) | DeviceEnumerator, notification registration, AudioClient, event handle, Timeline, MMCSS "Audio" | spawned in `setup`, ends at exit (respawned by `start_capture` after a panic) |
| `ic-dsp` | - | ring consumer, `Dsp`, `PcmHistory`, optional `FrameSink`, Channel clone, MMCSS "Audio" | per stream (re)open |
| `ic-devlist` | MTA, own `DeviceEnumerator` | one device-list / peek request (3.2) | per request, a few ms |
| `ic-devwatch` | MTA, own `DeviceEnumerator` | 2 s poll of the default render device; exists only if the notification registration failed | while capturing in that degraded case |
| `ic-stt-io` (P3) | - | worker stdin writer | per worker |
| `ic-stt` (P3, process) | - | faster-whisper | spawn on Start |

## 3. Rust audio engine (Phase 2)

### 3.1 Module layout (`src-tauri/src/`)

`lib.rs` (Builder, plugins, `manage(AudioState)`, `invoke_handler`, lifecycle hooks, power-throttling opt-out) and `main.rs` (generated; it calls the template's `<package>_lib::run()`, e.g. `interviewer_copilot_lib::run()`). Everything audio lives in `audio/`:

| File | Responsibility | Public types / functions |
|---|---|---|
| `mod.rs` | re-exports; consts `STT_RATE = 16_000`, `VAD_FRAME = 256`, `RING_SECONDS = 2`, `MAX_SYNTH_MS = 500`, `IDLE_AFTER_MS = 200` | - |
| `types.rs` | serde types shared by all files **and** the UI contract (3.7); no tauri | `Target`, `DeviceInfo`, `DeviceList`, `StreamInfo`, `LevelFrame`, `EngineStats`, `EngineStatus`, `CaptureEvent`, `RestartCause`, `StopReason`, `ReconnectPhase`, `EventSink` |
| `error.rs` | error taxonomy, wire wrapper, HRESULT classification (named imports only, no glob import of `windows::Win32::Media::Audio`) | `AudioError`, `IpcError`, `classify()` |
| `render_only.rs` | **the only file allowed to name `Direction`**: enumeration, stable ids, the render guards (cached + OS-queried), loopback open | `RenderEndpoint`, `list_render_devices`, `list_render_devices_blocking`, `spawn_default_poller`, `Loopback`, `OutFormat`, `is_render_flow` |
| `platform.rs` | non-audio Win32 (the only other `unsafe` file): MMCSS guard, process power-throttling opt-out, QPC / clock helpers | `Mmcss`, `opt_out_of_power_throttling`, `qpc_now_100ns`, `now_ms` |
| `timeline.rs` | device-position gap logic and idle credit (pure, unit-tested, no clock reads) + `qpc_to_100ns` | `Timeline`, `PacketMeta`, `Plan`, `qpc_to_100ns` |
| `convert.rs` | bytes -> interleaved f32; SILENT zeroing (pure) | `to_f32_interleaved` |
| `ring.rs` | `rtrb` wrapper, `push_silence` from a preallocated zero chunk, shared atomics | `AudioProducer`, `AudioConsumer`, `Stats`, `ring(rate, ch)` |
| `engine.rs` | engine thread, notifications + debounce, supervisor + backoff + `Waiting`, capture loop | `spawn_engine`, `Ctl`, `Shared` |
| `dsp/mod.rs` | `Dsp`: meter, downmix, resample, i16, history, optional sink (deterministic, no threads) | `Dsp::{new, process, flush}` |
| `dsp/downmix.rs` | channel-mask-weighted mono mix | `Downmix` |
| `dsp/resample.rs` | rubato wrapper, 16 kHz passthrough; `FrameAssembler` (re-chunker to 256-sample frames) is Phase 3-early | `Resampler16k`, `FrameAssembler` |
| `dsp/meter.rs` | 40 ms hop RMS/peak, ballistics | `Meter` |
| `dsp/pcm.rs` | `to_i16`, `PcmHistory` (10 s in Phase 2, 30 s with Phase 3), `PcmClock` | same |
| `dsp/wav.rs` | 16 kHz mono WAV writer (debug dump) | `write_wav16` |
| `sink.rs` | **Phase 3 seam, optional in Phase 2**: trait + event types + `NullSink` | `FrameSink`, `Pcm16Frame`, `SegmentEvent`, `FlushReason` |
| `pipeline.rs` | S2: the `PipelineFactory` / `PipelineHandle` traits (so `engine.rs` compiles against them); S4: the real implementation that starts/stops `ic-dsp` (MMCSS) for one stream | `Pipeline`, `DspCtl` |
| `ipc.rs` | **the only audio file that imports `tauri`**: commands, DTOs, state, `SwapSink` | `AudioState`, commands (3.7) |

Isolation rule: nothing under `audio/` except `ipc.rs` may `use tauri::*` (the engine talks through `EventSink` / `PipelineFactory` / `FrameSink` traits). If incremental `cargo test --lib` exceeds ~60 s on this box, the directory can be lifted into a workspace crate unchanged. Phase 3-4 add `audio/vad.rs`, `stt/`, `llm/` the same way.

**Phase 2 core vs optional.** Core = what T1-T11, H1-H5 and M1-M11 need: types, errors, render_only, platform, timeline, convert, ring, engine, dsp (meter, downmix, resampler, i16, 10 s `PcmHistory`, WAV writer), pipeline, ipc. Optional, may land at the start of Phase 3 without touching the gate: `sink.rs` (`FrameSink` and friends), `PcmHistory` beyond 10 s, `FrameAssembler`, `list_outputs(withPeek)`, process-loopback mode. Add nothing else before you confirm the gate.

```rust
// engine.rs / ring.rs / pipeline.rs: what the app can ask of ic-wasapi and what it is given at spawn (skeleton; EventSink lives in types.rs)
pub trait EventSink: Send + Sync { fn emit(&self, e: CaptureEvent); }            // ipc.rs: SwapSink over the Tauri Channel; tests: a Vec sink
pub enum Ctl {
    Start { target: Target, reply: SyncSender<Result<StreamInfo, AudioError>> },
    Stop { reply: SyncSender<()> },
    DumpWav { seconds: u32, reply: SyncSender<Result<PathBuf, AudioError>> },    // forwarded to the pipeline: the DSP thread only copies samples, a short-lived thread writes the file
    Shutdown { reply: SyncSender<()> },
    Dev(DevEvent),                                                               // sent by the IMMNotificationClient closures
}                                                                                // no ListDevices: Refresh never runs on the capture thread (3.2)
pub enum DevEvent { DefaultChanged(Role, Option<String>), State(String, DeviceState), Added(String), Removed(String) }
pub enum LoopEnd { Stop, Shutdown, Reopen(RestartCause), Restart(Target) }       // Restart = user switched device without Stop; Reopen(Resync) = hole > 500 ms
pub trait PipelineFactory: Send + Sync {                                          // contract fixed in S2; S3 supplies a Meter-only factory for the console proof, S4 the real ic-dsp one
    fn start(&self, info: &StreamInfo, cons: AudioConsumer, stats: Arc<Stats>) -> Result<Box<dyn PipelineHandle>, AudioError>;
}
pub trait PipelineHandle: Send { fn dump_wav(&self, seconds: u32) -> Result<PathBuf, AudioError>; fn stop(self: Box<Self>); }   // stop = join + flush (on_flush) before the next state event
pub struct Shared {
    pub events: Arc<dyn EventSink>, pub pipeline: Arc<dyn PipelineFactory>,
    pub status: Arc<RwLock<EngineStatus>>, pub dump_dir: PathBuf,
    pub on_devices_changed: Arc<dyn Fn(&str) + Send + Sync>,                     // -> app event audio:devices-changed
    pub warn: Arc<dyn Fn(&'static str, &str) + Send + Sync>,                     // non-fatal conditions -> app event audio:warning {code, message}
}
pub struct Stats { pub packets: AtomicU64, pub silent_packets: AtomicU64, pub discontinuities: AtomicU64, pub timestamp_errors: AtomicU64,
                   pub resyncs: AtomicU64, pub position_resets: AtomicU64, pub real_frames: AtomicU64, pub synth_frames: AtomicU64, pub dropped_frames: AtomicU64,
                   pub packet_age_max_ms: AtomicU32, pub heartbeat_ms: AtomicU64, pub packets_flowing: AtomicBool }   // Relaxed atomics shared by T1, T2 and the stats event; `.inc()` = helper over fetch_add(1, Relaxed)
```

### 3.2 Device enumeration (render only)

- **Source:** `DeviceEnumerator::get_device_collection(&Direction::Render)` = `EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)`. Unplugged / disabled / not-present endpoints are not listed (here 1 of 11 registered is active). Capture endpoints are never enumerated.
- **Where it runs:** `list_outputs` is served by a short-lived worker thread `ic-devlist` (`initialize_mta` -> its own `DeviceEnumerator` -> list, optional peek -> `deinitialize`; `render_only::list_render_devices_blocking`). It never touches the capture thread: Refresh does `EnumAudioEndpoints` plus `OpenPropertyStore` per device (RPCs, allocations, and ~100 ms of sleeps with peek), which must not stall draining or the 25 Hz level stream while capturing; no COM object crosses threads.
- **Names / IDs:** `get_friendlyname()` (PKEY_Device_FriendlyName, e.g. `Speakers (Realtek(R) Audio)`), `get_id()` (opaque, never parsed) and `stable_id` (below). The Phase 1 brief's registry GUID `{a45c254e-df1c-4ed4-a1e2-6d63d6a303f2}` is wrong (real: `{a45c254e-df1c-4efd-8020-67d146a850e0}`; through the COM property store the friendly name is pid 14, in the registry the readable names are `,2` plus the adapter name `{b3f8fa53-0004-438e-9003-51a46e139bfc},6`, as `tools/diagnostics/audio-probe.ps1` reads them); the Core Audio API makes the registry irrelevant.
- **Default markers:** `get_default_device_for_role(&Direction::Render, &Role::{Console, Multimedia, Communications})` gives three booleans per device. Use an index loop (`for d in &coll` unwraps inside wasapi 0.26.0).

```rust
// render_only.rs: runs on the ic-devlist worker (or the engine thread while idle); never inside the capture loop
pub fn list_render_devices(en: &DeviceEnumerator) -> Result<Vec<DeviceInfo>, AudioError> {
    let coll = en.get_device_collection(&Direction::Render).map_err(|e| classify("EnumAudioEndpoints", e))?;
    let dflt = |r: Role| en.get_default_device_for_role(&Direction::Render, &r).ok().and_then(|d| d.get_id().ok());
    let (c, m, k) = (dflt(Role::Console), dflt(Role::Multimedia), dflt(Role::Communications));
    let n = coll.get_nbr_devices().map_err(|e| classify("GetCount", e))?;
    let mut v = Vec::with_capacity(n as usize);
    for i in 0..n {
        let d = coll.get_device_at_index(i).map_err(|e| classify("Item", e))?;
        let id = d.get_id().map_err(|e| classify("GetId", e))?;
        let name = d.get_friendlyname().unwrap_or_else(|_| "(unnamed output)".into());
        v.push(DeviceInfo { is_default_console: c.as_deref() == Some(id.as_str()), is_default_multimedia: m.as_deref() == Some(id.as_str()),
                            is_default_communications: k.as_deref() == Some(id.as_str()), stable_id: stable_id_of(&d), peek_db: None, id, name });
    }
    v.sort_by(|a, b| b.is_default_console.cmp(&a.is_default_console).then(a.name.cmp(&b.name)));
    Ok(v)
}

/// PKEY_AudioEndpoint_StableId (Windows 11 24H2 / build 26100 and later): IMMDevice::OpenPropertyStore(STGM_READ) + IPropertyStore::GetValue; only a VT_LPWSTR value counts.
/// VT_EMPTY, a failed read or an older Windows => None (fall back to the endpoint id). Opaque and case-sensitive: never parsed, normalised or compared case-insensitively.
fn stable_id_of(d: &Device) -> Option<String> { /* ~10 unsafe lines; PROPERTYKEY: the windows crate's constant if it has one, else copied from the Windows SDK 10.0.26100 mmdeviceapi.h installed in step 1 (not verified here) */ }
```

| Mode (`Target`) | Resolution at every (re)open | Reacts to | UI |
|---|---|---|---|
| `FollowDefault` (default) | `get_default_device_for_role(Render, Console)`; Chromium maps the "default" sink to eConsole, so a call without `setSinkId` plays there | default-device change, state change of the current endpoint, invalidation | `System default -> <name>` |
| `Pinned { device_id, stable_id }` | `stable_id` first (`get_device(stable_id)`; `GetDevice` accepts it from build 26100), else `device_id`; then the guards (3.4): data flow == Render (OS-queried) and state == Active. **Never** moves to another device on its own. `StreamInfo` returns the resolved ids and the UI re-saves them | state / removal of that id, invalidation | banner if the default differs: `Call audio is probably on <default>` |

**Persistence and unavailable pinned devices.** Learn (Endpoint ID Strings; PKEY_AudioEndpoint_StableId): the ordinary endpoint id changes with a driver upgrade or a reinstall, and the stable id is more durable but not guaranteed. The UI persists `{ mode, deviceId, stableId }` and refreshes it from `StreamInfo` after every successful open. If a saved pinned device cannot be resolved (at startup or later) the app does **not** change the selection: the footer selector shows the typed error (`deviceNotFound` / `endpointNotActive`), capture stays stopped (or in `Waiting`, if it was running), and a one-click "Use system default" is offered as an explicit user action (rule R2). A third mode (follow the *Communications* default) is not needed for browsers; add only if a call app that uses eCommunications appears.

**Optional activity peek** (post-gate, S8): `list_outputs(withPeek = true)` activates `IAudioMeterInformation` on each *render* IMMDevice (`Device::get_audiometerinformation().get_peak_value()`), samples every 10 ms over 100 ms (Learn: the value is the peak of the *previous device period*, 10 ms here, so 5 samples 20 ms apart would see only half of the periods), keeps the max as `peekDb`, and the UI draws a dot next to outputs that are playing right now ("which output is the call on?"). Whether the meter reads before or after the endpoint volume is unverified. It opens no stream and cannot reach a microphone. The same call on the open endpoint, scheduled by deadline every 40 ms, feeds `osPeakDb` (gate check M3).

### 3.3 Loopback capture engine

**Thread and COM.** `std::thread` named `ic-wasapi`, spawned once in Tauri `setup`. First statement: `wasapi::initialize_mta()`, an explicit `CoInitializeEx(COINIT_MULTITHREADED)` (Tauri's main thread is STA and untouched); a failure is reported through `shared.fail(AudioError::Wasapi { hr, context: "CoInitializeEx" })`, never swallowed. Last statement: `wasapi::deinitialize()` after every COM object is dropped (they all live in one inner scope). All wasapi COM wrappers are `!Send`/`!Sync` in 0.26.0, so the rest of the app talks to the thread only through `Ctl` messages; anything that needs COM elsewhere (device list, default-device poll) builds its own enumerator on its own MTA thread. The body runs under `catch_unwind` (0.26.0 still contains `unwrap()`s; this needs `panic = "unwind"`, see 7).

**Scheduling and power (E1, E9).** `ic-wasapi` and `ic-dsp` both call `AvSetMmThreadCharacteristicsW("Audio")` (`TaskIndex` must be 0 on the first call; `AvRevertMmThreadCharacteristics` on exit; failure is non-fatal and surfaces as `audio:warning`). Why it is more than insurance: Learn's QoS table gives Low QoS to a windowed process that is minimized or fully occluded (and to its descendants, i.e. the Phase 3 worker) and, on battery, Low means the most efficient CPU frequency; only Audible processes and MMCSS-tagged audio threads (Deadline QoS, Windows 10 2004+) are exempt, and this app captures but plays nothing. At startup the process also opts out of execution-speed throttling: `SetProcessInformation(ProcessPowerThrottling, { Version: CURRENT, ControlMask: EXECUTION_SPEED, StateMask: 0 })`. Whether an explicit HighQoS tag overrides the visibility rule is not documented: M8 measures it. The 1 s buffer, not MMCSS, is what prevented loss in the stress test; this layer covers the occluded-on-battery case that the stress test did not.

**Initialization parameters**

| Parameter | Value | Why / evidence |
|---|---|---|
| Share mode, flags | Shared; `LOOPBACK + EVENTCALLBACK` (wasapi derives LOOPBACK from device Render + stream Capture + Shared) | loopback is shared-mode only; measured working on build 26200 |
| Format | `get_mixformat()` as-is (here 48000 / 2 / f32, mask 0x3) | always accepted in shared mode; no OS conversion |
| `hnsBufferDuration` | `10_000_000` (1 s = 48000 frames here); if `Initialize` fails, one retry with `0` on a **fresh** `IAudioClient` | measured: 22 ms lost 38-174 ms per 7.4 s under 2x load, 1 s lost 0 (managed-.NET probe: indicative); docs say a failed Initialize needs a new client |
| `hnsPeriodicity` | 0 (set by wasapi for `EventsShared`) | engine decides (10 ms here) |
| Event | `set_get_eventhandle()` **before** `start_stream()` | required order |
| Read buffer | `get_buffer_size() * blockalign` bytes, allocated once (endpoint loopback only; process loopback has its own sizing, see below) | `read_from_device` drops the packet and errors if the buffer is smaller than the packet |
| Wait | `wait_for_event(20)` | event = hint; timeout keeps control and the idle watchdog alive |
| Ring | 2 s of device-format f32 (768 KB here) | absorbs a ~2 s DSP stall; a 500 ms synthesized hole fits |

**Event-driven vs polling: hybrid, event + 20 ms timeout.** Measured on this machine: event-driven loopback delivered ~100 events/s with 0 timeouts and 99.8-100.2 % coverage while audio played, **and kept delivering ~100 packets/s of all-zero, unflagged packets for 240 s with every render session idle** (23,122 wakes, all signalled, 0 timeouts, 0 SILENT flags, peak 0.0); Windows 10 1703+ supports event-driven loopback. That is this driver only: Chromium avoids EVENTCALLBACK for endpoint loopback because it "requires active output audio to trigger the event" and runs a helper render client, and OBS plays a silent render stream to keep its stream alive, so other endpoint classes (HDMI, USB, Bluetooth) may stop delivering. The loop is therefore correct whether or not packets or events keep arriving (E8). A helper silent *render* stream is rejected as the default: it adds a session on the user's device (visible in the volume mixer, keeps the endpoint powered) and is unnecessary where packets flow (measured). It stays a documented fallback if an endpoint class is found that stops delivering; its stream category must stay the default, never Communications (which selects the Bluetooth HFP profile).

**Format handling.** `to_f32_interleaved(bytes, &WaveFormat, &mut Vec<f32>)` supports `(Float, 32)` and `(Int, 16)` for any channel count (extensible or not). Anything else returns `UnsupportedFormat`, and the supervisor does a **format retry on the same endpoint**, once, with `OutFormat::Os16kMono` (`AUTOCONVERTPCM + SRC_DEFAULT_QUALITY`, 16 kHz / mono / s16, channel mask `Some(0)`; measured to work here, `None` would give the 0x1 mask and about -3.5 dB). It is not a device fallback; it is logged and `StreamInfo.formatPath` reports it. Bluetooth HFP endpoints report 8 or 16 kHz mono PCM16 (see the invalidation table); T3 covers both rates.

```rust
// render_only.rs
/// The ONLY place that names Direction::Capture, and it is the *stream* direction: (device Render, stream Capture, Shared) => AUDCLNT_STREAMFLAGS_LOOPBACK.
fn initialize_loopback(c: &mut AudioClient, f: &WaveFormat, m: &StreamMode) -> Result<(), WasapiError> { c.initialize_client(f, &Direction::Capture, m) }

/// OS-queried data flow of the exact device we are about to activate (wasapi's cached `Device.direction` is only the argument we passed to get_default_device_for_role).
fn os_flow_is_render(d: &Device) -> Result<bool, AudioError> {
    let ep: IMMEndpoint = d.as_immdevice().cast().map_err(|e| classify_win("QueryEndpoint", e))?;
    let flow = unsafe { ep.GetDataFlow() }.map_err(|e| classify_win("GetDataFlow", e))?;
    Ok(flow == eRender)
}

pub struct Loopback {                                   // field order = drop order: evt last
    client: AudioClient, cap: AudioCaptureClient, pub fmt: WaveFormat, pub buf: Vec<u8>,
    pub index_valid: bool,                              // true for endpoint loopback; false for process loopback (device position always 0)
    pub meter: Option<AudioMeterInformation>, evt: Handle,
}
impl Loopback {
    pub fn open(ep: &RenderEndpoint, out: OutFormat) -> Result<Self, AudioError> {
        if !os_flow_is_render(&ep.device)? { return Err(AudioError::NotARenderEndpoint); }       // OS-queried, before any Activate
        let mut last = None;
        for hns in [10_000_000i64, 0] {
            let mut client = ep.device.get_iaudioclient().map_err(|e| classify("Activate", e))?;
            if client.get_direction() != Direction::Render { return Err(AudioError::NotARenderEndpoint); } // cached check, BEFORE initialize_client: it overwrites direction with the stream direction
            let (fmt, auto) = match out {
                OutFormat::Native => (client.get_mixformat().map_err(|e| classify("GetMixFormat", e))?, false),
                OutFormat::Os16kMono => (WaveFormat::new(16, 16, &SampleType::Int, 16_000, 1, Some(0)), true),
            };
            if !supported(&fmt) { return Err(AudioError::UnsupportedFormat { detail: describe(&fmt) }); }   // Float32 or Int16 only; supervisor retries once with Os16kMono
            match initialize_loopback(&mut client, &fmt, &StreamMode::EventsShared { autoconvert: auto, buffer_duration_hns: hns }) {
                Ok(()) => {
                    let evt = client.set_get_eventhandle().map_err(|e| classify("SetEventHandle", e))?;
                    let cap = client.get_audiocaptureclient().map_err(|e| classify("GetService", e))?;
                    let frames = client.get_buffer_size().map_err(|e| classify("GetBufferSize", e))? as usize;
                    let buf = vec![0u8; frames * fmt.get_blockalign() as usize];
                    let meter = ep.device.get_audiometerinformation().ok();      // OS peak for the cross-check
                    client.start_stream().map_err(|e| classify("Start", e))?;
                    return Ok(Self { client, cap, fmt, buf, index_valid: true, meter, evt });
                }
                Err(e) => last = Some(classify("Initialize", e)),
            }
        }
        Err(last.unwrap())
    }
}
impl Drop for Loopback { fn drop(&mut self) { let _ = self.client.stop_stream(); } }
```

**Silence-gap synthesis (exact algorithm).** Device position is the continuity authority; QPC stamps are metadata. Learn (IAudioCaptureClient::GetBuffer, `_AUDCLNT_BUFFERFLAGS`) defines `pu64QPCPosition` as the performance counter "at the time that the audio endpoint device recorded the device position of the first audio frame in the data packet" (a *sampled correlation* with the wall clock, not derived from the sample clock) and DATA_DISCONTINUITY as "not correlated with the previous packet's device position". Mature implementations do not trust stamps either: Chromium detects glitches from device position, forces monotonic stamps (last + 1 us) and switches to its own clock when a stamp is more than 500 ms off (crbug 1439283); libobs ignores stamp deviations under 70 ms; cpal keys on device position.

*Measured on this machine* (Win 11 10.0.26200, Realtek, default render endpoint, `LOOPBACK|EVENTCALLBACK`, 1 s buffer, nothing playing, 240 s; managed-.NET probe, so indicative for Rust): 23,854 packets (480 frames x 23,709, 960 x 145); device position contiguous in **23,853 of 23,853** transitions (there was no real hole); QPC stamps run **+68.5 ppm** ahead of the frame count (earlier runs 77 ppm; 16.6 ms accumulated in 240 s) with detrended jitter std 0.15 ms but **max deviation 9.4 ms** (stamps bunch when the engine is delayed); packet age (read minus stamp) p50 0.9 / p90 5.5 / p99 45.8 / max 170 ms with the machine in normal use (an earlier, quieter run: p50 0.1 / p99 2.8 / max 12.9 ms); the first packet has device position 480 with DATA_DISCONTINUITY set and arrives 13-53 ms after Start. DRAFT v1 reconciled the running frame count against the stamps on every packet with a 2 ms slack: fed this exact trace it inserted zeros 11 times (51.6 ms in total) and deleted real audio 3 times (21.8 ms) on a perfectly clean stream, because two different clocks were being compared. The slack only delays the first edit by 26-30 s; with the opposite drift sign the same code deletes speech.

Rules: (1) the continuity test compares device positions only; (2) PCM is never deleted or trimmed; (3) zeros are inserted only for a measured hole (or, while no packets arrive at all, by the idle watchdog), never more than 500 ms at a time (`MAX_SYNTH_MS`, Chromium's constant); a bigger hole is a stream-level event (stall, sleep/resume, engine restart): it is not synthesized, the stream is reopened (`Resync`); (4) the first packet starts the stream, with no leading pad; (5) wall-clock alignment is metadata: `t_ms = stream.t0_ms + sample_index / 16`, where `t0_ms` is taken when the first packet is read and a reopen continues the session clock. Device-vs-QPC drift is about 0.25 s per hour, irrelevant for transcript times; nothing is ever inserted or deleted to chase it.

```rust
// timeline.rs (pure: no clock reads, no COM, no allocation; every input is a parameter)
pub const MAX_SYNTH_MS: u64 = 500;                 // holes above this are resyncs, not synthesis
const STAMP_NOISE_100NS: u64 = 250_000;            // 25 ms: above the measured stamp noise (max 9.4 ms detrended); used only by the flagged-but-contiguous fallback

pub struct PacketMeta { pub index: u64, pub stamp: u64, pub frames: u32, pub discontinuity: bool, pub stamp_untrusted: bool }
// index = device position of the first frame; stamp = QPC in 100 ns units; discontinuity = DATA_DISCONTINUITY (already false for the first packet); stamp_untrusted = TIMESTAMP_ERROR

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Plan { Pad(u64), Resync(u64) }            // Pad(n): push n zero frames, then the packet. Resync(gap): push the packet as it is, then reopen the stream.

pub struct Timeline { rate: u64, index_valid: bool, next_index: Option<u64>, prev_end_stamp: u64, credit: u64, pub position_resets: u64 }
impl Timeline {
    pub fn new(rate: u32, index_valid: bool) -> Self { Self { rate: rate as u64, index_valid, next_index: None, prev_end_stamp: 0, credit: 0, position_resets: 0 } }
    fn frames(&self, d100ns: u64) -> u64 { (d100ns as u128 * self.rate as u128 / 10_000_000) as u64 }
    fn dur(&self, frames: u64) -> u64 { (frames as u128 * 10_000_000 / self.rate as u128) as u64 }
    pub fn max_synth(&self) -> u64 { self.rate * MAX_SYNTH_MS / 1000 }
    pub fn on_packet(&mut self, p: &PacketMeta) -> Plan {
        let stamp = if p.stamp_untrusted { self.prev_end_stamp } else { p.stamp };      // TIMESTAMP_ERROR: the stamp is uncertain, so treat the packet as contiguous
        let owed = match self.next_index {
            None => 0,                                                                  // first packet: the stream starts here; its DATA_DISCONTINUITY flag is undefined after Start
            Some(expected) => {
                let gap = if self.index_valid { p.index as i128 - expected as i128 } else { 0 };
                if gap > 0 { gap as u64 }                                               // a measured hole: exactly this many frames
                else if gap < 0 { self.position_resets += 1; 0 }                        // device position went backwards: re-anchor below, never delete audio
                else if p.discontinuity && !p.stamp_untrusted && stamp.saturating_sub(self.prev_end_stamp) > STAMP_NOISE_100NS {
                    self.frames(stamp - self.prev_end_stamp)                            // flagged but contiguous index (or no index: process loopback): trust the stamp delta only above the noise floor
                } else { 0 }
            }
        };
        let owed = owed.saturating_sub(std::mem::take(&mut self.credit));               // silence the idle watchdog already emitted for this stretch (credit applies to the first packet after it only)
        self.next_index = Some(p.index + p.frames as u64);                              // device domain: synthetic frames never enter it
        self.prev_end_stamp = stamp.saturating_add(self.dur(p.frames as u64));
        if owed > self.max_synth() { Plan::Resync(owed) } else { Plan::Pad(owed) }
    }
    /// No packet for IDLE_AFTER: wall-clock silence owed since the previous pass (clamped per call); remembered as credit against the next real gap.
    pub fn on_idle(&mut self, elapsed_100ns: u64) -> u64 { let f = self.frames(elapsed_100ns).min(self.max_synth()); self.credit = self.credit.saturating_add(f); f }
}

/// Microsoft's formula for pu64QPCPosition is mathematical: 10_000_000 * t / f. In u64, t * 10^7 overflows at 184,467 s (51.2 h) of uptime with the 10 MHz QPC measured here, and after 12.8 minutes with a 2.4 GHz one.
pub fn qpc_to_100ns(counter: u64, freq: u64) -> u64 { ((counter as u128 * 10_000_000u128) / freq as u128) as u64 }
```

| Situation | Handling |
|---|---|
| steady stream | `index == expected`: nothing. No QPC, no slack, nothing is ever dropped or trimmed |
| first packet (arrives ~13-53 ms after Start; measured: index 480, DATA_DISCONTINUITY set) | no leading pad; the stream starts at its first packet; the flag is ignored (undefined on the first call after Start) |
| `DATA_DISCONTINUITY` hole (measured earlier: 8544 frames = 178 ms; device position jumped by the same amount) | `gap = index - expected > 0` -> exactly `gap` zeros, then the packet; `discontinuities += 1`, `warn!` |
| hole > 500 ms (stall longer than the 1 s buffer, sleep/resume, engine restart) | `Plan::Resync`: the packet is pushed as it is, `resyncs += 1`, `warn!`, the stream is reopened with cause `resync` (new generation; downstream gets `on_flush(StreamChange)`); a resync within 10 s of the previous one takes the next backoff step |
| `index < expected` (device position reset) | re-anchor (`expected = index + n`), `position_resets += 1`, `warn!`; nothing inserted, nothing deleted |
| flagged but index contiguous, or no index (process loopback) | stamp delta between this packet's stamp and the previous packet's end, used only if > 25 ms; otherwise nothing |
| `TIMESTAMP_ERROR` | stamp untrusted: the packet is treated as contiguous, `timestamp_errors += 1` (a zero or garbage stamp, the failure Chromium had to guard against, cannot cause a pad) |
| `SILENT` flag | data ignored: bytes zeroed before conversion (0.26.0 does not do it; upstream issue #88); `silent_packets += 1` |
| no packets (an endpoint class that stops delivering; not seen on this driver) | watchdog: after 200 ms without a packet each pass pads the elapsed wall time (clamped to 500 ms per pass) and records it as credit; `packetsFlowing = false`. The first packet after the stretch consumes the credit, so no time is counted twice and nothing is deleted if the device position did not move |
| stamp drift / jitter / bunching | ignored (metadata only) |

**Capture loop** (one pass about every 10-20 ms; stop latency <= ~25 ms). Per-packet work is read, convert, push: no allocation (`f32buf`, the zero chunk and the read buffer are preallocated), no locks, no COM beyond the capture client itself. Rare control events (device plug, default change) may allocate; they happen at human speed.

```rust
// engine.rs (skeleton; Cx = enumerator, stats, sinks, control receiver, debounce)
const IDLE_AFTER: Duration = Duration::from_millis(200);

fn capture_loop(cx: &mut Cx, lb: &mut Loopback, prod: &mut AudioProducer, tl: &mut Timeline) -> Result<LoopEnd, AudioError> {
    let (ch, bpf) = (lb.fmt.get_nchannels() as usize, lb.fmt.get_blockalign() as usize);
    let mut f32buf: Vec<f32> = Vec::with_capacity(lb.buf.len() / bpf * ch);           // reused: no per-packet allocation
    let (mut first, mut last_read, mut last_pass, mut next_peek) = (true, Instant::now(), Instant::now(), Instant::now());
    loop {
        if let Some(end) = cx.poll_control()? { return Ok(end); }    // try_recv only: Stop, Shutdown, Start (switch), DumpWav, debounced Dev events. Never enumerates devices (3.2)
        let t0 = Instant::now();
        let signaled = lb.evt.wait_for_event(20).is_ok();            // Err(EventTimeout) also covers WAIT_FAILED
        if !signaled && t0.elapsed() < Duration::from_millis(5) { thread::sleep(Duration::from_millis(10)); }   // never spin on a failing wait
        loop {                                                       // drain: the auto-reset event may cover several packets
            match lb.cap.get_next_packet_size().map_err(|e| classify("GetNextPacketSize", e))? { Some(n) if n > 0 => {}, _ => break }
            let (frames, info) = lb.cap.read_from_device(&mut lb.buf).map_err(|e| classify("GetBuffer", e))?;
            if frames == 0 { break; }
            let bytes = frames as usize * bpf;
            if info.flags.silent { lb.buf[..bytes].fill(0); cx.stats.silent_packets.inc(); }
            let flagged = info.flags.data_discontinuity && !first;   // undefined on the first packet after Start
            if flagged { cx.stats.discontinuities.inc(); log::warn!(target: "ic::audio", "data discontinuity at device position {}", info.index); }
            if info.flags.timestamp_error { cx.stats.timestamp_errors.inc(); }
            cx.stats.note_age(qpc_now_100ns().saturating_sub(info.timestamp));   // diagnostic only: how late this read is (M8)
            let plan = tl.on_packet(&PacketMeta { index: info.index, stamp: info.timestamp, frames, discontinuity: flagged, stamp_untrusted: info.flags.timestamp_error });
            first = false; last_read = Instant::now();
            to_f32_interleaved(&lb.buf[..bytes], &lb.fmt, &mut f32buf)?;
            if let Plan::Pad(n) = plan { prod.push_silence(n as usize, &cx.stats); }   // fixed-size chunks of a preallocated zero slice, counted in synth_frames
            prod.push(&f32buf, &cx.stats);                           // all-or-nothing; on Full: dropped_frames += n. PCM is never trimmed
            cx.stats.packets.inc();
            if let Plan::Resync(gap) = plan {
                cx.stats.resyncs.inc(); log::warn!(target: "ic::audio", "hole of {gap} frames exceeds {} ms: resync", MAX_SYNTH_MS);
                return Ok(LoopEnd::Reopen(RestartCause::Resync));
            }
        }
        let now = Instant::now();
        let flowing = now.duration_since(last_read) < IDLE_AFTER;
        if !flowing { prod.push_silence(tl.on_idle((now.duration_since(last_pass).as_nanos() / 100) as u64) as usize, &cx.stats); }   // watchdog: wall-clock silence while no packets arrive
        last_pass = now;
        cx.stats.packets_flowing.store(flowing, Relaxed); cx.heartbeat(); cx.wake_dsp();      // heartbeat_ms = now; unpark ic-dsp
        if now >= next_peek { cx.sample_os_peak(&lb.meter); next_peek = now + Duration::from_millis(40); }   // IAudioMeterInformation: peak of the previous ~10 ms device period -> atomic
    }
}
```

**Device invalidation, default-device and format changes**

| Trigger | Detected by | Action |
|---|---|---|
| `AUDCLNT_E_DEVICE_INVALIDATED`, `_RESOURCES_INVALIDATED`, `_SERVICE_NOT_RUNNING`, `_ENDPOINT_CREATE_FAILED` (Learn: unplugged or reconfigured), `E_NOTFOUND` from `Activate` / `GetDevice` | any wait/packet/read error -> `AudioError::DeviceLost` | stop pipeline, backoff, reopen (re-resolve target) |
| `AUDCLNT_E_CPUUSAGE_EXCEEDED` (the engine's process pass exceeded its CPU budget; plausible on 2 cores) | `Initialize` / read error -> `AudioError::Transient` | backoff, reopen |
| default render (eConsole) changed | `set_default_device_callback(Render, Console, id)`; if the registration failed, a 2 s poll thread sends the same event | FollowDefault: debounce 250 ms -> reopen; Pinned: ignore + UI banner |
| state change / removal of the current id | `set_device_state_callback`, `set_device_removed_callback` | reopen attempt (fails -> fast backoff -> `Waiting`) |
| device added | `set_device_added_callback` | `audio:devices-changed` UI event only |
| sample-rate / format change in Windows Sound settings | invalidation | same as DeviceLost; new `StreamInfo` shows the new rate/channels |
| `AUDCLNT_E_DEVICE_IN_USE` (exclusive-mode app) | `Initialize` error | `ExclusiveBusy`, backoff |
| **Bluetooth A2DP <-> HFP (Windows 11, this box)** | not documented: hypothesis only | Learn (Bluetooth Classic audio): on Windows 11 A2DP and HFP are *one* unified endpoint per device; Windows selects HFP when an app opens the headset's microphone endpoint or creates a Communications-category output stream (a browser call does both) and resamples to the HFP rate (8 or 16 kHz, mono). Expect the **same endpoint id with a changed mix format**, not a different device, so Pinned should survive. Whether our loopback stream is invalidated at the flip is undocumented: if it is, it is DeviceLost and the reopen shows the new format. Test M11 with Marcos's headset (required, Q3; not paired here yet). Windows 10's split "Stereo" / "Hands-Free" endpoints are out of scope |
| system sleep / resume, long scheduler stall | stream invalidated (DeviceLost), or the device position jumps (> 500 ms hole => `Resync`) | both end in `Running` or `Waiting` without CPU spikes; the idle watchdog clamps its pad; no power-event subscription in Phase 2 (M10 decides whether `RegisterSuspendResumeNotification` is needed) |

Callback rules (Microsoft): closures only `tx.send(Ctl::Dev(..))` (non-blocking, no locks, no COM, never touch the enumerator). They are registered on the engine thread and unregistered by dropping the registration.

**Supervisor and backoff** (state chart, then policy):

```
Idle --Start--> Opening --ok--> Running --Stop--> Idle
        first open fails: return the error to the Start caller, stay Idle (user just pressed Start: no hidden retries)
Running --DeviceLost / debounced default change / Resync--> Reconnecting(fast) --open ok--> Running
Reconnecting(fast) --6 attempts failed--> Waiting --open ok--> Running           (Waiting = retry the SAME target kind every 2 s, indefinitely)
Reconnecting | Waiting --Stop--> Idle
any --non-retryable error (NotARenderEndpoint, UnsupportedFormat after the format retry, EnginePanic)--> Failed --(user presses Start)--> Opening
```

1. Loop: `RenderEndpoint::resolve(target)` -> `Loopback::open(Native)` (format retry once with `Os16kMono` on `UnsupportedFormat`) -> `PipelineFactory::start` -> emit `Started{cause}` -> `Timeline::new(rate, lb.index_valid)` -> `capture_loop`.
2. `LoopEnd::Reopen` (default changed, user switched device, resync) reopens immediately; if the previous stream also ended within the last 10 s it takes the next backoff step instead, so a resync loop cannot spin. Errors go to backoff.
3. Fast phase: delays 0, 200, 500, 1000, 2000, 2000 ms (6 attempts, ~5.7 s), each emitting `Reconnecting{phase: fast, attempt, inMs, elapsedMs, cause}`; the counter resets after 10 s of stable capture. Sleeps are interruptible by Stop/Shutdown and by a relevant `Dev` event (default changed, state change of the target), which retries at once.
4. After the fast phase: **`Waiting`**. Re-resolve the same `Target` every 2 s (FollowDefault: the current default render endpoint, or `noDefaultRenderDevice` while there is none; Pinned: the same endpoint becoming Active again), emitting `Reconnecting{phase: waiting, ...}` on each attempt so the UI keeps a persistent banner with the typed cause and the choices "Use system default" and "Stop"; the UI may flash the window title or play a sound after 10 s. Rule R2 holds: only the same kind of target is retried, never an input device, never another device on its own. Why not give up after ~5.7 s: no document bounds the time of a Bluetooth profile switch, a resume from sleep, an audio-service restart or a dock re-enumeration, and failing mid-interview is the worst outcome for this product.
5. `Failed` only for non-retryable errors (`AudioError::terminal()`: `NotARenderEndpoint`, `UnsupportedFormat` after the format retry, `EnginePanic`, `EngineGone`, `Dsp`); it never auto-recovers.
6. Order guarantee for the UI: `Started` < `Level`* < (`Reconnecting` | `Failed` | `Stopped`); the pipeline is joined and flushed before the next state event.

**Engine thread skeleton**

```rust
pub fn spawn_engine(rx: Receiver<Ctl>, tx: Sender<Ctl>, shared: Shared) -> JoinHandle<()> {
    thread::Builder::new().name("ic-wasapi".into()).spawn(move || {
        let r = catch_unwind(AssertUnwindSafe(|| {
            if let Err(e) = wasapi::initialize_mta().ok() {                       // explicit CoInitializeEx(MTA): a failure is reported, never silent
                shared.fail(AudioError::Wasapi { hr: hr_string(e.code()), context: "CoInitializeEx".into() }); return;
            }
            {   // every COM object lives in this scope
                let _mmcss = Mmcss::join();                                       // platform.rs: AvSetMmThreadCharacteristicsW(w!("Audio")), TaskIndex starts at 0; Drop reverts; failure => warn
                let en = match DeviceEnumerator::new() { Ok(en) => en, Err(e) => { shared.fail(classify("MMDeviceEnumerator", e)); return; } };
                let mut cb = DeviceEventCallbacks::new();
                { let t = tx.clone(); cb.set_default_device_callback(move |d, r, id| { if render_only::is_render_flow(&d) { let _ = t.send(Ctl::Dev(DevEvent::DefaultChanged(r, id))); } }); }
                { let t = tx.clone(); cb.set_device_state_callback(move |id, s| { let _ = t.send(Ctl::Dev(DevEvent::State(id, s))); }); }
                { let t = tx.clone(); cb.set_device_added_callback(move |id| { let _ = t.send(Ctl::Dev(DevEvent::Added(id))); }); }
                { let t = tx.clone(); cb.set_device_removed_callback(move |id| { let _ = t.send(Ctl::Dev(DevEvent::Removed(id))); }); }
                let _reg = match en.register_notification_callback(cb) {          // returns Result<DeviceEventRegistration, _>: binding it unchecked would swallow the failure
                    Ok(r) => Some(r),                                             // keep alive; Drop unregisters
                    Err(e) => {
                        log::warn!(target: "ic::audio", "device notifications unavailable: {e}");
                        (shared.warn)("deviceNotifications", "Device change notifications are unavailable; polling the default output every 2 s");
                        let t = tx.clone();                                       // ic-devwatch lives in render_only.rs (it must name Direction::Render): own MTA + enumerator, polls every 2 s
                        render_only::spawn_default_poller(move |id| { let _ = t.send(Ctl::Dev(DevEvent::DefaultChanged(Role::Console, id))); });
                        None
                    }
                };
                engine_main(&en, &rx, &shared);   // idle loop: recv() -> Start -> supervise | Dev -> notify UI | DumpWav (not capturing: error) | Shutdown -> return
            }
            wasapi::deinitialize();
        }));
        if let Err(p) = r { shared.fail(AudioError::EnginePanic { detail: panic_message(&p) }); }
    }).expect("spawn ic-wasapi")
}
```

**Process loopback ("capture the browser only")** is not in the gate (S8). Later option: `AudioClient::new_application_loopback_client(pid, true)`. It needs its own open path, because `Loopback::open` as written would misbehave: (a) `get_buffer_size()` returns nonsense (wasapi documents values like 3,131,961,357; upstream's own tests cap reads at 192,000 frames), so size the read buffer as `min(get_buffer_size(), 2 * rate)` frames or a fixed 192,000, never `get_buffer_size() * blockalign` unchecked (about 25 GB: abort); (b) it needs Windows 10 build 20348+ (Learn, PROCESS_LOOPBACK_MODE): add `AudioError::ProcessLoopbackUnsupported` (a wire change: TS union + golden test) and gate on the OS build; (c) `GetMixFormat` is unsupported and the format is not fixed by the OS: supply a `WaveFormat` (f32, 48 kHz, 2 ch) plus autoconvert explicitly; (d) wasapi 0.26.0 maps `include_tree = false` to EXCLUDE (code and Microsoft agree) while its doc comment says the opposite: always pass `true` and do not trust the crate docs here; Chrome/Edge render in child processes, so the root PID is needed; (e) device position is always 0 (`index_valid = false`: contiguous assumption plus the flagged-stamp fallback); (f) it needs no endpoint, so it cannot reach a microphone. `initialize_loopback` stays the single `Direction::Capture` site, so T8 stays at one token.

### 3.4 Structural no-mic guarantee and explicit errors

Layers, strongest first:

1. **Type:** `Loopback::open` accepts only `&RenderEndpoint`. Its fields are private; the only constructor is `resolve()`, which obtains devices solely via `get_default_device_for_role(&Direction::Render, ..)` or `get_device(id)` and then rejects anything whose data flow is not Render (`NotARenderEndpoint`) or whose `get_state() != Active` (`EndpointNotActive`). Two flow checks exist and they are not equivalent: wasapi's `Device.direction` is *cached* (for `get_default_device_for_role` it is merely the argument we passed in, so that check cannot fail on that path; for `get_device(id)` it is read from `IMMEndpoint::GetDataFlow`). So `resolve()` also runs the **OS-queried** check (`os_flow_is_render`: `IMMEndpoint::GetDataFlow() == eRender`), and `Loopback::open` repeats it before any `Activate`. A capture-endpoint id (typo, stale preference, tampering) is refused before anything is activated.
2. **Single token, scanned as a lint:** `Direction::Capture` (the *stream* direction that makes `(Render, Capture, Shared)` = loopback) appears once, in `render_only::initialize_loopback`. In wasapi 0.26.0 `(Capture device, Capture stream)` is an ordinary microphone capture, and the bundled examples `loopback.rs` and `record.rs` open the microphone, so nothing may be copied from them. `tests/no_mic_guard.rs` scans the **whole `src-tauri` tree** (`src/`, `tests/`, `examples/`, `build.rs`; comments excluded):
   - exactly one `Direction::Capture`, in `audio/render_only.rs`; the identifier `Direction` (any variant, `use ... as`, `use Direction::*`) and `IMMDeviceEnumerator`, `IMMEndpoint`, `EDataFlow`, `eRender` only in `audio/render_only.rs` (the engine's callback uses `render_only::is_render_flow`);
   - nowhere: `eCapture`, `eAll`, a numeric `EDataFlow(..)` constructor, `DEVINTERFACE_AUDIO_CAPTURE`, `GetDefaultAudioEndpoint`, `EnumAudioEndpoints`, `get_default_device(` (the direction-taking helper), and any glob import from `windows::Win32::Media::Audio` or `wasapi` (`error.rs` imports named HRESULT constants);
   - `cargo tree` contains none of `cpal`, `rodio`, `miniaudio`, `portaudio`, `rtaudio`, `sdl2` (a later dependency cannot silently add a second audio stack);
   - the only exception: the H4 hardware test may hold the synthetic capture-endpoint id as a *string literal* (it contains no forbidden identifier).
   A second scan of `src/**/*.{ts,tsx}` forbids `getUserMedia`, `mediaDevices`, `MediaRecorder` (`npm run check:nomic`). Both scans are **lints**: token matching can be bypassed by aliasing or by a new `windows` feature (`Win32_Media_Audio` is enabled app-wide), so they catch mistakes and copy-paste, not a determined author. Layer 3 is the guarantee.
3. **Runtime checks, release builds included:** the OS-queried flow check (before `Activate`) and the cached-direction check (immediately before `initialize_client`), both plain `if`s, not `debug_assert`; plus one `info!` line per open: `endpoint=<name> id=<id> flow=Render loopback=true`.
4. **Windows' own backstop, and a decision.** `IAudioClient::Initialize` with `AUDCLNT_STREAMFLAGS_LOOPBACK` on a capture endpoint fails with `AUDCLNT_E_WRONG_ENDPOINT_TYPE` (Learn), so Windows itself would refuse that combination; but wasapi sets LOOPBACK only when its cached direction says Render, so a bug that hands it a capture device opens the microphone silently instead of reaching that backstop. Owning the `Initialize` call and the capture-client reads (explicit `LOOPBACK|EVENTCALLBACK`, `IAudioCaptureClient` from `GetService`; about 80 lines of `unsafe`, dropping wasapi's capture wrappers) would let the OS enforce rule R1. **Phase 2 decision: not taken.** Layers 1-3 put three independent checks on one choke point, and the cost is the "about 0 unsafe" goal and a harder first Rust project. Revisit if the gate review wants OS enforcement (open question 10).
5. **WebView:** no media code; defense in depth (not required for the gate): header `Permissions-Policy: microphone=(), camera=()` through `app.security.headers` (the key exists in the Tauri config; header value untested) and Vite `server.headers` in dev; optionally a WebView2 `PermissionRequested` handler denying microphone/camera (hook availability in wry unverified).
6. **Evidence:** M6 in the gate (3.9).

```rust
// render_only.rs: the only constructor of the only type Loopback::open accepts
pub struct RenderEndpoint { device: Device, pub id: String, pub stable_id: Option<String>, pub name: String }    // private `device`: nobody else can build one
impl RenderEndpoint {
    pub fn resolve(en: &DeviceEnumerator, t: &Target) -> Result<Self, AudioError> {
        let device = match t {
            Target::FollowDefault => en.get_default_device_for_role(&Direction::Render, &Role::Console).map_err(|_| AudioError::NoDefaultRenderDevice)?,
            Target::Pinned { device_id, stable_id } => {
                let by_stable = stable_id.as_deref().and_then(|s| en.get_device(s).ok());          // durable id first (Windows 11 24H2+), then the endpoint id
                match by_stable { Some(d) => d, None => en.get_device(device_id).map_err(|_| AudioError::DeviceNotFound { id: device_id.clone() })? }
            }
        };
        if device.get_direction() != Direction::Render || !os_flow_is_render(&device)? { return Err(AudioError::NotARenderEndpoint); }   // microphone / "Stereo Mix" id: refused, nothing activated
        let id = device.get_id().map_err(|e| classify("GetId", e))?;
        let st = device.get_state().map_err(|e| classify("GetState", e))?;
        if st != DeviceState::Active { return Err(AudioError::EndpointNotActive { id, state: st.to_string() }); }
        let name = device.get_friendlyname().unwrap_or_else(|_| id.clone());
        let stable_id = stable_id_of(&device);
        Ok(Self { device, id, stable_id, name })
    }
}
pub fn is_render_flow(d: &Direction) -> bool { *d == Direction::Render }          // lets engine.rs filter notifications without naming `Direction`
```

| `AudioError` kind | UI message | Action offered |
|---|---|---|
| `noDefaultRenderDevice` | Windows has no default output device. | Refresh / open Sound settings (retried automatically once capture has run) |
| `deviceNotFound`, `endpointNotActive` | The selected output is unavailable (unplugged or disabled). | "Use system default" (explicit user action) / choose another; retried automatically once capture has run |
| `exclusiveBusy` | Another app has exclusive control of the output device. | Retry |
| `deviceLost` | Output changed or was removed - reconnecting... (automatic) | - |
| `transient` | Windows audio is busy (0x...) - retrying. | - |
| `unsupportedFormat`, `wasapi`, `dsp` | Windows audio error 0x...: context (or the DSP detail) | Retry / copy details |
| `notARenderEndpoint` | Refused: not an output device. (indicates a bug) | - |
| `enginePanic`, `engineGone`, `engineTimeout` | The audio engine crashed / is not responding. | Start again (respawns the engine thread) |

### 3.5 DSP

All of this runs in `Dsp::process(&mut self, interleaved: &[f32], flowing: bool, os_peak: Option<f32>)`, which has no clock and no threads, so unit tests drive it with synthetic data. The `ic-dsp` thread (MMCSS "Audio", `TaskIndex` 0 on first call, reverted on exit, failure non-fatal) loops: pop whole frames from the ring (<= 1024 frames per call) -> `process` -> `park_timeout(10 ms)`; the capture thread calls `unpark()` after each pass (timer granularity of `park_timeout` on Windows is unverified and irrelevant given the 2 s ring).

1. **Meter (on raw, un-downmixed frames):** hop = `rate / 25` frames (1920 at 48 kHz = 40 ms), counted in samples so the 25 Hz rate is independent of packet size.
   ```rust
   let rms_db  = 10.0 * (sumsq / (hop_frames * ch) as f32).max(1e-9).log10();       // floor -90 dBFS; sumsq over all samples of all channels
   let peak_db = 20.0 * peak.max(3.1623e-5).log10();                                // peak = max |x|, floor -90 dBFS
   self.rms = if rms_db > self.rms { rms_db } else { self.rms + A * (rms_db - self.rms) };   // instant attack; A = 1 - exp(-0.040/0.250) = 0.1479 (release tau 250 ms), dB domain
   if peak_db >= self.hold { self.hold = peak_db; self.age = 0 } else { self.age += 1; if self.age > 20 { self.hold = (self.hold - 0.48).max(peak_db) } }   // hold 800 ms, fall 12 dB/s
   ```
   Payload `LevelFrame { seq, tMs, rmsDb, peakDb, holdDb, clip (peak >= 0.999), packetsFlowing, osPeakDb }`, one decimal, ~130 B JSON, sent from `ic-dsp`. `packetsFlowing` is the capture thread's 200 ms watchdog (a packet was read in the last 200 ms), copied from `Stats`; it says nothing about loudness. "Silence" is a **level** notion (rms <= -85 dBFS for >= 500 ms) and is derived in the UI, because on this driver an idle endpoint still delivers a continuous stream of zero packets (E8). `osPeakDb` is the `IAudioMeterInformation` peak of the *previous ~10 ms device period* (Learn: "always retrieves the peak value recorded during the previous device period"), sampled by deadline every 40 ms; it is a shape/timing cross-check, and whether it follows the endpoint volume is unverified.
2. **Downmix to mono:** stereo `0.5*(L+R)` (mono-in-stereo keeps its level); mono passes through; more than 2 channels: weights by channel-mask bit (FL, FR = 1; FC = 1.414; BL, BR, FLC, FRC, BC, SL, SR = 0.707; LFE = 0), divided by the sum of weights; mask 0 or mismatched popcount -> equal weights. Bit values are the standard `SPEAKER_*` constants (unverified here; one 5.1 unit test, mask 0x3F). Only stereo is exercised in the gate.
3. **Resample to 16 kHz:** `Fft::<f32>::new(rate_in, 16_000, 768, 1, FixedSync::Both)`, mono. At 48 kHz this gives exactly 768 in / 256 out (one VAD frame), delay 128 frames (8 ms), cutoff ~7.58 kHz; 44.1 kHz gives 882/320; 8 kHz (Bluetooth HFP narrowband) is a 1:2 upsample; the code always reads `input_frames_next()` and never hard-codes sizes. `rate_in == 16_000` bypasses the resampler. Process with `process_into_buffer(&InterleavedSlice::new(..), &mut InterleavedSlice::new_mut(..), None)` into pre-allocated buffers (no allocation per call; `rubato`'s `log` feature stays off); `InterleavedSlice` comes from `rubato::audioadapter_buffers::direct` (re-exported). `FrameAssembler` (re-chunks arbitrary resampler output into exact 256-sample frames) is only needed by the Phase 3 sink. Rejected: Async/sinc/poly resamplers (drift-tolerant, costlier, or unfiltered), `dasp` (no anti-alias filter), libsamplerate bindings (C build, unmaintained). Fallback if rustfft compile time hurts: a documented 120-tap Kaiser polyphase FIR (stopband -75.6 dB, 1.2 ms delay) for 48 kHz only.
4. **f32 -> i16:** `(x * 32768.0).round() as i16` (Rust `as` saturates and maps NaN to 0), no dither (faster-whisper's own path does not dither; dither would turn digital silence into noise that invites hallucinations).
5. **History / dump:** `PcmHistory` (absolute sample index; 10 s of i16 = 320 KB in Phase 2, 30 s = 960 KB with Phase 3). `DspCtl::DumpWav{seconds}`: the DSP thread only copies the last N seconds (a sub-millisecond memcpy) and a short-lived thread writes `ic-<unix_ms>.wav` (16 kHz mono 16-bit) to `app_data_dir()/dumps/` (3.10).
6. **Stats (1 Hz):** `packets, silentPackets, discontinuities, timestampErrors, resyncs, positionResets, synthFrames, droppedFrames, packetAgeMaxMs, heartbeatAgeMs, dspBusyPct` from shared atomics plus a timer around `process`. `packetAgeMaxMs` (read time minus the packet's QPC stamp, max per second) is a direct measure of capture-thread scheduling lateness under load (measured here: p99 46 ms, max 170 ms).
7. **UI rate:** `Level` is sent at 25 Hz while the window is visible and at 2 Hz while it is hidden (`set_ui_visible`); the meter itself keeps running at 25 Hz so ballistics are right on return. `Stats` stays at 1 Hz.

### 3.6 Segmentation / VAD (implemented in Phase 3; buffers and interfaces fixed now; the types are optional in Phase 2)

```rust
// sink.rs - Phase 2 may ship the trait + NullSink; Phase 3 adds audio/vad.rs: Segmenter: FrameSink
pub struct Pcm16Frame<'a> { pub index: u64, pub t_ms: u64, pub rms_db: f32, pub samples: &'a [i16; VAD_FRAME] }
pub trait FrameSink: Send {
    fn on_frame(&mut self, f: &Pcm16Frame<'_>, hist: &PcmHistory);   // on ic-dsp; must return in << 1 ms; heavy work goes to another thread
    fn on_flush(&mut self, why: FlushReason, hist: &PcmHistory);     // Stop | StreamChange (reopen, resync) | Shutdown: close any open segment
}
pub enum SegmentEvent {
    SpeechStart { t_ms: u64 },
    SpeechEnd { seq: u32, start: u64, end: u64 /* 16 kHz sample index into PcmHistory */, reason: EndReason /* Silence | MaxLen | Flush */ },
    TurnEnd { t_ms: u64 },
}
```

Time base: `t_ms = stream.t0_ms + sample_index / 16` (16 kHz). Synthetic silence counts as samples, so `t_ms` tracks the wall clock to within the device-vs-QPC drift (about 0.25 s per hour); `t0_ms` is the session-relative offset taken at the first packet of each stream, so transcript time stays monotonic across reopen.

State machine (parameters are research defaults, **tune on recorded interviewer audio in Phase 3**; none measured here):

| Parameter | Value |
|---|---|
| Classifier | `earshot::Detector` `predict_i16` per 256-sample frame (score 0..1), EMA smoothing alpha = 0.4 (unverified), AND a cheap energy gate |
| Speech start | >= 3 consecutive frames with smoothed score >= 0.6 and frame RMS >= -50 dBFS (adaptive floor: start at floor+12 dB, stop at floor+8 dB) |
| Stay in speech | smoothed score >= 0.5 |
| Pre-roll | 320 ms (20 frames) taken from `PcmHistory` |
| Segment end (`SpeechEnd`) | 600 ms (38 frames) below 0.5 |
| Min voiced | 300 ms, shorter segments are discarded |
| Soft cut / hard cut | at a >= 250 ms pause once the segment is > 8 s / forced at 12 s (Whisper encoder cost is flat to 30 s; 12 s keeps frames <= 384 KB) |
| Turn end (`TurnEnd`) | 1400 ms of non-speech (700 ms if the transcript so far ends with `?`, decided by the Phase 3 turn assembler, not the VAD); configurable, see section 4 |
| Escalation | Silero via `voice_activity_detector` + `ort` only if earshot misfires on real call audio (ort pinned to rc.10 there, +~8 MB, unmeasured RAM) |

### 3.7 Tauri IPC contract

Rules: high-rate streams use `tauri::ipc::Channel<T>` passed as a command argument; `emit` only for rare broadcasts; tagged enums with `tag = "event", content = "data"`; camelCase on the wire (Rust `on_event` = JS `onEvent`); the webview never receives raw audio.

```rust
// types.rs: the wire types ARE the domain types (serde only, no tauri). ipc.rs adds only commands, state and the Channel sink.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)] #[serde(tag = "mode", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum Target { FollowDefault, Pinned { device_id: String, #[serde(default)] stable_id: Option<String> } }   // struct variant: serde cannot internally tag a newtype String

#[derive(Clone, Serialize)] #[serde(rename_all = "camelCase")]
pub struct DeviceInfo { pub id: String, pub stable_id: Option<String>, pub name: String, pub is_default_console: bool, pub is_default_multimedia: bool,
                        pub is_default_communications: bool, pub peek_db: Option<f32> }
#[derive(Clone, Serialize)] #[serde(rename_all = "camelCase")]
pub struct DeviceList { pub devices: Vec<DeviceInfo>, pub default_console_id: Option<String> }
#[derive(Clone, Serialize)] #[serde(rename_all = "camelCase")]
pub struct StreamInfo { pub generation: u32, pub device_id: String, pub stable_id: Option<String>, pub device_name: String, pub mode: TargetKind /* followDefault|pinned */,
    pub sample_rate: u32, pub channels: u16, pub sample_format: SampleKind /* f32|i16 */, pub buffer_frames: u32,
    pub format_path: FormatPath /* native|osConverted */, pub stt_rate: u32, #[serde(skip)] pub t0_ms: u64 }
// TargetKind, SampleKind, FormatPath, EngineState, RestartCause, StopReason, ReconnectPhase: plain unit enums with rename_all = "camelCase"
#[derive(Clone, Copy, Serialize)] #[serde(rename_all = "camelCase")]
pub struct LevelFrame { pub seq: u32, pub t_ms: u64, pub rms_db: f32, pub peak_db: f32, pub hold_db: f32,
                        pub clip: bool, pub packets_flowing: bool, pub os_peak_db: Option<f32> }
#[derive(Clone, Copy, Serialize)] #[serde(rename_all = "camelCase")]
pub struct EngineStats { pub packets: u64, pub silent_packets: u64, pub discontinuities: u64, pub timestamp_errors: u64, pub resyncs: u64, pub position_resets: u64,
                         pub synth_frames: u64, pub dropped_frames: u64, pub packet_age_max_ms: u32, pub heartbeat_age_ms: u32, pub dsp_busy_pct: f32 }
#[derive(Clone, Serialize)] #[serde(rename_all = "camelCase")]
pub struct EngineStatus { pub state: EngineState /* idle|running|reconnecting|waiting|failed */, pub stream: Option<StreamInfo>,
                          pub target: Option<Target>, pub last_error: Option<IpcError> }
#[derive(Clone, Serialize)] #[serde(rename_all = "camelCase", rename_all_fields = "camelCase", tag = "event", content = "data")]
pub enum CaptureEvent {
    Started { stream: StreamInfo, cause: RestartCause },        // initial | userSwitch | defaultChanged | deviceLost | formatChanged | resync
    Level(LevelFrame), Stats(EngineStats),
    Reconnecting { attempt: u32, in_ms: u32, elapsed_ms: u64, phase: ReconnectPhase /* fast|waiting */, cause: IpcError },
    Failed { error: IpcError },
    Stopped { reason: StopReason },                              // user | appExit | replaced
}
// wire sample: {"event":"level","data":{"seq":1042,"tMs":41680,"rmsDb":-23.4,"peakDb":-9.8,"holdDb":-9.8,"clip":false,"packetsFlowing":true,"osPeakDb":-8.1}}
// error sample: {"kind":"deviceNotFound","id":"{0.0.0.00000000}.{...}","message":"Output device not found: ...","retryable":true}

// ipc.rs: command signatures only; each body sends a Ctl and waits for the reply with recv_timeout (list_outputs spawns the ic-devlist worker instead)
#[tauri::command(async)] pub fn list_outputs(with_peek: Option<bool>, st: State<'_, AudioState>) -> Result<DeviceList, IpcError>;   // named so it never clashes with render_only::list_render_devices
#[tauri::command(async)] pub fn start_capture(target: Target, on_event: Channel<CaptureEvent>, st: State<'_, AudioState>) -> Result<StreamInfo, IpcError>;
#[tauri::command(async)] pub fn stop_capture(st: State<'_, AudioState>) -> Result<(), IpcError>;        // idempotent
#[tauri::command(async)] pub fn get_engine_status(st: State<'_, AudioState>) -> Result<EngineStatus, IpcError>;
#[tauri::command(async)] pub fn dump_wav(seconds: u32, st: State<'_, AudioState>) -> Result<String, IpcError>;   // diagnostics, returns the path
#[tauri::command] pub fn set_ui_visible(visible: bool, st: State<'_, AudioState>);                      // atomic store, no engine round trip
```

```ts
// src/lib/ipc.ts - mirrors the Rust wire types 1:1
import { invoke, Channel } from "@tauri-apps/api/core";
export type CaptureTarget = { mode: "followDefault" } | { mode: "pinned"; deviceId: string; stableId?: string | null };
export interface RenderDevice { id: string; stableId: string | null; name: string; isDefaultConsole: boolean; isDefaultMultimedia: boolean; isDefaultCommunications: boolean; peekDb: number | null }
export interface DeviceList { devices: RenderDevice[]; defaultConsoleId: string | null }
export interface StreamInfo { generation: number; deviceId: string; stableId: string | null; deviceName: string; mode: "followDefault" | "pinned"; sampleRate: number; channels: number;
  sampleFormat: "f32" | "i16"; bufferFrames: number; formatPath: "native" | "osConverted"; sttRate: 16000 }
export interface LevelFrame { seq: number; tMs: number; rmsDb: number; peakDb: number; holdDb: number; clip: boolean; packetsFlowing: boolean; osPeakDb: number | null }
export interface EngineStats { packets: number; silentPackets: number; discontinuities: number; timestampErrors: number; resyncs: number; positionResets: number;
  synthFrames: number; droppedFrames: number; packetAgeMaxMs: number; heartbeatAgeMs: number; dspBusyPct: number }
type ErrBase = { message: string; retryable: boolean };
export type IpcError = ErrBase & (
  | { kind: "noDefaultRenderDevice" } | { kind: "deviceNotFound"; id: string } | { kind: "endpointNotActive"; id: string; state: string }
  | { kind: "notARenderEndpoint" } | { kind: "exclusiveBusy" } | { kind: "deviceLost" } | { kind: "transient"; hr: string; context: string }
  | { kind: "unsupportedFormat"; detail: string }
  | { kind: "wasapi"; hr: string; context: string } | { kind: "dsp"; detail: string } | { kind: "enginePanic"; detail: string }
  | { kind: "engineGone" } | { kind: "engineTimeout" });
export interface EngineStatus { state: "idle" | "running" | "reconnecting" | "waiting" | "failed"; stream: StreamInfo | null; target: CaptureTarget | null; lastError: IpcError | null }
export type CaptureEvent =
  | { event: "started"; data: { stream: StreamInfo; cause: "initial" | "userSwitch" | "defaultChanged" | "deviceLost" | "formatChanged" | "resync" } }
  | { event: "level"; data: LevelFrame } | { event: "stats"; data: EngineStats }
  | { event: "reconnecting"; data: { attempt: number; inMs: number; elapsedMs: number; phase: "fast" | "waiting"; cause: IpcError } }
  | { event: "failed"; data: { error: IpcError } } | { event: "stopped"; data: { reason: "user" | "appExit" | "replaced" } };
export async function startCapture(target: CaptureTarget, onEvent: (e: CaptureEvent) => void): Promise<StreamInfo> {
  const ch = new Channel<CaptureEvent>(); ch.onmessage = onEvent;            // set before invoke; delivery is ordered
  return invoke<StreamInfo>("start_capture", { target, onEvent: ch });       // rejects with IpcError
}
export const listOutputs = (withPeek = false) => invoke<DeviceList>("list_outputs", { withPeek });
export const setUiVisible = (visible: boolean) => invoke<void>("set_ui_visible", { visible });
```

**Semantics**
- `start_capture` is idempotent: same target while running -> only swaps the event channel (UI reload / HMR recovery, via `SwapSink`) and returns the current `StreamInfo` (the old channel gets `stopped{replaced}` first); a different target -> `LoopEnd::Restart` (device switch without Stop). If `ic-wasapi` has ended (`enginePanic` / `engineGone`), `start_capture` respawns it first.
- A failed *first* open is returned as the command's error. After the first success, failures arrive as `reconnecting` (fast, then waiting) / `failed` events.
- `started` (Channel) and the command's return value carry the same `StreamInfo`; ordering between an invoke response and Channel messages is not guaranteed, so the UI reducer must be idempotent. The UI re-saves `deviceId` / `stableId` from `StreamInfo` after every `started`.
- Device-list changes use the app event `audio:devices-changed` (`{reason}`) via `AppHandle::emit`, also while not capturing. Non-fatal degradations use `audio:warning` (`{code, message}`; codes `deviceNotifications`, `mmcss`, `powerThrottling`).
- `set_ui_visible(visible)` is called on `visibilitychange` (a minimized window becomes hidden) and on Tauri window focus / minimize events; detection of a *fully occluded* window is unverified, so this is an optimization, not a requirement.
- Timeouts: Start 5 s, Shutdown ack 3 s -> `engineTimeout`; List 3 s (worker). `Channel::send` behaviour after the JS callback disappears is unverified; `start_capture` re-binding covers reloads.

**State and lifecycle.** `AudioState { tx: Mutex<Sender<Ctl>>, join: Mutex<Option<JoinHandle<()>>>, status: Arc<RwLock<EngineStatus>>, events: Arc<SwapSink>, ui_visible: Arc<AtomicBool> }` is registered with `app.manage(..)` in `setup` (Tauri wraps state in `Arc`; no extra `Arc`). `Ctl` carries `SyncSender` reply channels (`recv_timeout`) for start/stop/dump; `get_engine_status` reads the `RwLock` snapshot. `shutdown()` is idempotent: send `Ctl::Shutdown{ack}`, wait <= 3 s, join; called from `WindowEvent::CloseRequested` and `RunEvent::ExitRequested` / `Exit`. Capabilities for Phase 2: `core:default` only (custom commands need no entries). Note: `Ctl` and sinks must stay `Send`; COM objects never cross threads.

### 3.8 Error taxonomy and logging

```rust
// error.rs
use windows::Win32::Media::Audio::{AUDCLNT_E_CPUUSAGE_EXCEEDED, AUDCLNT_E_DEVICE_IN_USE, AUDCLNT_E_DEVICE_INVALIDATED, AUDCLNT_E_ENDPOINT_CREATE_FAILED,
    AUDCLNT_E_RESOURCES_INVALIDATED, AUDCLNT_E_SERVICE_NOT_RUNNING, AUDCLNT_E_UNSUPPORTED_FORMAT};   // named imports: a glob import is forbidden by the T8 lint
const E_NOTFOUND: HRESULT = HRESULT(0x8007_0490u32 as i32);                  // HRESULT_FROM_WIN32(ERROR_NOT_FOUND): GetDevice / Activate on a vanished endpoint

#[derive(Debug, Clone, thiserror::Error, serde::Serialize)]
#[serde(tag = "kind", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum AudioError {
    #[error("Windows has no default output device")]                    NoDefaultRenderDevice,
    #[error("Output device not found: {id}")]                           DeviceNotFound { id: String },
    #[error("Output device is not active ({state}): {id}")]             EndpointNotActive { id: String, state: String },
    #[error("Refused: endpoint is not an output (render) device")]      NotARenderEndpoint,     // R1/R2 guard
    #[error("Another app has exclusive control of the output device")]  ExclusiveBusy,
    #[error("The output device was removed, disabled or reconfigured")] DeviceLost,
    #[error("Windows audio is temporarily unavailable ({hr}): {context}")] Transient { hr: String, context: String },
    #[error("Unsupported audio format: {detail}")]                      UnsupportedFormat { detail: String },
    #[error("Windows audio error {hr}: {context}")]                     Wasapi { hr: String, context: String },
    #[error("Audio processing error: {detail}")]                        Dsp { detail: String },
    #[error("The audio engine crashed: {detail}")]                      EnginePanic { detail: String },
    #[error("The audio engine is not running")]                         EngineGone,
    #[error("The audio engine did not answer in time")]                 EngineTimeout,
}
impl AudioError {
    /// Non-retryable: the supervisor stops with `Failed` and waits for the user. Everything else is retried after a first success (fast backoff, then `Waiting`).
    pub fn terminal(&self) -> bool { matches!(self, Self::NotARenderEndpoint | Self::UnsupportedFormat { .. } | Self::EnginePanic { .. } | Self::EngineGone | Self::Dsp { .. }) }
    pub fn retryable(&self) -> bool { !self.terminal() }                                          // wire field `retryable`
}

#[derive(Debug, Clone, serde::Serialize)]   // wire type: commands return Result<T, IpcError>; `?` converts via From
pub struct IpcError { #[serde(flatten)] pub error: AudioError, pub message: String, pub retryable: bool }
impl From<AudioError> for IpcError { fn from(e: AudioError) -> Self { Self { message: e.to_string(), retryable: e.retryable(), error: e } } }

pub fn classify_win(ctx: &'static str, e: windows::core::Error) -> AudioError { classify(ctx, WasapiError::Windows(e)) }
pub fn classify(ctx: &'static str, e: WasapiError) -> AudioError {            // WasapiError::Windows(#[from] windows_core::Error)
    match &e {
        WasapiError::Windows(w) => {
            let c = w.code();                                                    // HRESULT
            if c == AUDCLNT_E_DEVICE_INVALIDATED || c == AUDCLNT_E_RESOURCES_INVALIDATED || c == AUDCLNT_E_SERVICE_NOT_RUNNING
               || c == AUDCLNT_E_ENDPOINT_CREATE_FAILED || (c == E_NOTFOUND && matches!(ctx, "Activate" | "GetDevice")) { AudioError::DeviceLost }
            else if c == AUDCLNT_E_CPUUSAGE_EXCEEDED { AudioError::Transient { hr: format!("{:#010x}", c.0 as u32), context: ctx.into() } }
            else if c == AUDCLNT_E_DEVICE_IN_USE { AudioError::ExclusiveBusy }
            else if c == AUDCLNT_E_UNSUPPORTED_FORMAT { AudioError::UnsupportedFormat { detail: ctx.into() } }
            else { AudioError::Wasapi { hr: format!("{:#010x}", c.0 as u32), context: ctx.into() } }
        }
        WasapiError::UnsupportedFormat => AudioError::UnsupportedFormat { detail: ctx.into() },
        WasapiError::DeviceNotFound(id) => AudioError::DeviceNotFound { id: id.clone() },
        other => AudioError::Wasapi { hr: "n/a".into(), context: format!("{ctx}: {other}") },
    }
}
```

The enum is internally tagged with struct/unit variants only (serde cannot internally tag a tuple variant holding a string). Golden-JSON tests pin every variant's shape to the TypeScript union above; T11 pins `classify`.

**Logging.** `log` facade everywhere (wasapi already uses it) + `tauri-plugin-log` 2.10.1 with stdout (dev) and file (app log dir) targets, `max_file_size` 1 MB and `RotationStrategy::KeepSome(5)` (both exist in 2.10.1 per docs.rs; the rest of the builder API is unverified: check at S2); target `ic::audio`. `info`: state transitions, endpoint open (name, id, flow, format, buffer frames), device events, and one line each time the meter crosses -60 dBFS (up or down, with 6 dB hysteresis so music near the threshold cannot flood the log) carrying the unix-ms time (M2 is judged from these lines); `warn`: discontinuities, resyncs, position resets, ring overruns, retries; `debug`: 1 Hz stats; `error`: failures. Never log audio samples, transcripts at `info`, or keys. A panic hook logs the message; the engine panic path also emits `failed{enginePanic}`.

### 3.9 Phase 2 acceptance gate

**Automated (`cargo test`, no hardware; `#[ignore]` hardware tests run with `-- --ignored`):**

| ID | Test | Pass criterion |
|---|---|---|
| T1 | downmix | stereo (1,0) -> 0.5; (a,a) -> a; 5.1 mask 0x3F weights sum-normalised; mask 0 -> equal weights |
| T2 | `to_i16` | 1.0 -> 32767; -1.0 -> -32768; 0.5 -> 16384; NaN -> 0; 1e9 -> 32767 |
| T3 | resampler, synthetic sines 48 k stereo -> 16 k | 1 kHz and 3 kHz keep amplitude within 0.3 / 0.5 dB (skip first 512 output samples); 7 kHz >= -3 dB; **12 kHz attenuated >= 40 dB** (catches a missing anti-alias filter); output length within +-400 samples of N/3; 16 kHz input bypasses; 44.1 kHz works; **Int16 mono at 16 kHz (bypass) and at 8 kHz (1:2 upsample: 1 kHz keeps amplitude within 0.5 dB, output length 2N +-400) as Bluetooth HFP produces**. Thresholds are initial (unverified until the first run) |
| T4 | meter calibration | 1 kHz amp 0.5: RMS -9.03 dBFS, peak -6.02 dBFS (+-0.1); silence -90.0; full-scale square: RMS 0 dBFS + `clip`; step to silence reaches <= -89 dB within 1.3 s (tau 250 ms); hold 800 ms then 12 dB/s |
| T5 | `Timeline` (pure) | (a) replay of a generated trace matching the measured statistics (fixed-seed PRNG: contiguous index, 480-frame packets with ~0.6 % 960-frame ones, stamps +70 ppm against the frame count with bunching up to +-10 ms, 240 s): **zero pads, zero resyncs, `position_resets == 0`, output frames == input frames**; (b) first packet with index 480 and DATA_DISCONTINUITY set -> `Pad(0)`; (c) index jump of 8544 frames (the measured 178 ms hole) -> `Pad(8544)` exactly; (d) hole of 600 ms -> `Resync`, nothing synthesized; hole of 1 h -> `Resync` (bounded output, T5b); (e) TIMESTAMP_ERROR packet, stamp = 0, stamp 1 h in the future, each with contiguous index -> `Pad(0)`; (f) flagged with contiguous index: stamp delta 100 ms -> about 4800 frames, 10 ms -> `Pad(0)`, and the same with `index_valid = false`; (g) index goes backwards -> `Pad(0)`, `position_resets == 1`, next packet judged against the new anchor; (h) 10 s without packets: `on_idle` pads <= 500 ms per call and the first packet after it (device position advanced by the full 10 s) gets only the residual, total emitted == 10 s +-20 ms; with a position that did not advance -> `Pad(0)`; (i) `qpc_to_100ns(u64::MAX / 10_000_000 + 1, 10_000_000)` and a 2.4 GHz frequency neither overflow nor wrap |
| T6 | `convert` | f32 and i16 bytes for 1/2/6 channels; SILENT zeroing |
| T7 | `ring` (+ `FrameAssembler` when it lands) | all-or-nothing push; drop counter on Full; `push_silence` of 500 ms allocates nothing; arbitrary chunking -> exact 256-sample frames, no sample lost |
| T8 | guard scan | the rules of 3.4 layer 2 over the whole `src-tauri` tree and `cargo tree`; zero `getUserMedia` / `mediaDevices` / `MediaRecorder` in `src/` |
| T9 | pipeline, synthetic, **two idle modes** | 2 s tone in 10 ms contiguous packets, then 3 s of (a) all-zero packets still arriving, and (b) no packets at all (simulated clock drives the watchdog): Level frames ~25 Hz of simulated time (+-2 over 10 s); RMS tracks the tone (-9.0 dB +-0.5 for amp 0.5); <= -89 dB by 1.3 s after it ends; `packetsFlowing` stays true in (a) and turns false within 300 ms in (b); `PcmHistory` / sink frames = ms*16 +-32 in both modes; `droppedFrames == 0` |
| T10 | serde golden | every `CaptureEvent` and `IpcError` variant matches the JSON in 3.7; `tsc --noEmit` passes |
| T11 | `classify` (pure) | INVALIDATED / RESOURCES_INVALIDATED / SERVICE_NOT_RUNNING / ENDPOINT_CREATE_FAILED / E_NOTFOUND (ctx `Activate`) -> `DeviceLost`; CPUUSAGE_EXCEEDED -> `Transient`; DEVICE_IN_USE -> `ExclusiveBusy`; unknown HRESULT -> `Wasapi`; `terminal()` is true only for the five listed kinds |
| H1-H5 (ignored, need the PC) | hardware | H1 device list contains this machine's default render endpoint (name from its Phase 0 report; "Speakers (Realtek(R) Audio)" on the laptop) flagged console+multimedia+communications; H2 `resolve(FollowDefault)` ok; H3 open loopback 1 s **while audio is playing** (an idle endpoint may deliver no packets on other drivers, E8): packets arrive, device position contiguous over >= 90 packets, first packet within 100 ms; H4 `resolve(Pinned { device_id: <capture id> })` -> `NotARenderEndpoint` (id synthesised from the registry as `{0.0.1.00000000}.{guid}`; only `GetDevice` + `GetDataFlow`, nothing activated); H5 `stable_id` of Speakers is `Some` on build >= 26100 and `resolve(Pinned { stable_id, bogus device_id })` finds the same endpoint (or `None` is recorded as data) |

**Manual (you at the keyboard; evidence goes into `docs/phase2-gate.md`):**

| ID | Check | Pass criterion |
|---|---|---|
| M1 | device list + Refresh | Speakers listed with Default badge; plugging headphones then Refresh (or the auto event) updates the list; Refresh while capturing does not interrupt the level stream |
| M2 | `tools\diagnostics\play-tone.ps1` (1 kHz WAV via `System.Media.SoundPlayer`, prints its start as `play_start_unix_ms`), Start (FollowDefault); also YouTube by ear | judged from log timestamps, not by eye: the `-60 dBFS up` log line follows `play_start_unix_ms` by <= 500 ms (a human cannot judge 200 ms); pause -> `-60 dBFS down` and <= -89 dBFS within 2 s; level events 25 +-2 Hz measured by `seq` over 10 s, also while idle. M2b (informational): master volume 100 % -> 20 % leaves **our** level within +-1 dB = pre-volume tap on this driver; record whether `osPeakDb` follows the volume (data, not an oracle) |
| M3 | OS meter cross-check | `osPeakDb` (peak of the previous ~10 ms device period) rises and falls together with our meter; compare shape and timing, not absolute values; volume (in)dependence is recorded, not assumed |
| M4 | 10 s WAV dump while the 1 kHz tone plays, then while speech plays | `tools/diagnostics/check_wav.py` (stdlib only): 16000 Hz / mono / 16-bit, 10.0 +-0.1 s, no clipping; tone measures 1000 +-5 Hz and **the residual after subtracting the best-fit sinusoid over the middle 8 s stays below -50 dBFS** (an inserted or deleted block of even 1 ms breaks the phase and fails this: the objective "no clicks" check); you confirm speech by ear: intelligible, normal pitch and speed |
| M5 | plug / unplug headphones | record the observed Realtek behaviour as data (it may show a "which device did you plug in?" prompt, or not change the default at all); accept either an automatic default switch or a manual default change. FollowDefault: after the default moves, `reconnecting` -> `started` on the new device (cause defaultChanged), audio continues, and back on unplug. Pinned to Speakers: no auto-move; banner shows; if the pinned endpoint becomes unavailable -> `reconnecting(fast)`, then `waiting` with a persistent banner and the typed error, never another device, and the stream resumes by itself when the endpoint returns |
| M6 | no capture endpoint opened | T8 green; logs show only `flow=Render loopback=true`; no Windows microphone indicator in the tray while capturing; Settings > Privacy > Microphone shows no entry for the app (expected, unverified) |
| M7 | idle test per endpoint class actually used: speakers (this box) and the Bluetooth headset (required, Q3), plus wired headset / HDMI if available. Pause all audio 60 s, then resume | **record** per class: packets/s, SILENT-flag count, zero fraction of the samples, `GetBufferSize`, `synthFrames`. Pass (whatever the packet behaviour): meter reaches -90 dBFS and recovers on resume, level events continue at 25 Hz, no panic. Expected on speakers (measured): ~100 packets/s of unflagged zeros, `packetsFlowing` stays true, `synthFrames` ~0 |
| M8 | stress: YouTube 1080p + webcam test page + app, 5 min, **app window fully covered by the maximised browser**; run once on battery and once on AC (and once minimised) | `droppedFrames == 0`; `discontinuities` and `resyncs` recorded; `dspBusyPct` < 3 %; `packetAgeMaxMs` recorded; CPU of the app plus its WebView2 children < 5 % of the machine; record whether the explicit power-throttling opt-out kept the process out of Low QoS. If failed on a debug build, repeat with a release build before drawing conclusions |
| M9 | exit | closing the window ends `ic-wasapi` in < 3 s; no leftover process |
| M10 | sleep / resume: Sleep for 30 s while capturing, then wake | automatic recovery to `running` within ~10 s, or an explicit `reconnecting` / `waiting` banner that then recovers; no CPU spike, no panic, no giant synthesized silence (`synthFrames` bounded); Stop still works. If this fails, add a power-event subscription |
| M11 | Bluetooth headset (**required**: Marcos will use one in interviews, Q3) | pair, make it the default, Start; play the test tone / YouTube (A2DP) and check the meter; then join a browser call (e.g. a Meet test call) so the headset microphone opens: record the new mix format (expect 8 or 16 kHz mono), whether the stream is invalidated, the endpoint id before and after (expected unchanged on Windows 11), that capture resumes by itself within ~10 s and that the meter follows the call audio; dump 10 s of WAV in call mode and copy it at once out of `dumps/` (that folder keeps 5 files and is purged after 24 h, 3.10) to `%LOCALAPPDATA%\interviewer-copilot\testset\` (outside the repo, reused by the Phase 3 test set); leave the call and confirm the return to A2DP; Pinned and FollowDefault both; the logs never show a capture endpoint (M6) |

Independent oracle: the existing `tools\diagnostics\audio-probe.ps1` (read-only C# COM probe, already in the repo) can run next to the app during M2 to confirm that the endpoint really carries audio (several shared-mode loopback clients on one endpoint are normal Windows behaviour; not tested here).

All of T1-T11, H1-H5, M1-M11 green + your written confirmation unlocks Phase 3. If the Bluetooth headset is not available on gate day, the gate may close for the speakers only with M11 recorded as **pending**; the headset must then not be relied on in a real interview until M11 passes.

### 3.10 Phase 2 operational details

| Item | Decision |
|---|---|
| WAV dump | `app_data_dir()/dumps/ic-<unix_ms>.wav`; the UI shows the full path with a "Copy path" button (`tauri-plugin-opener` is removed, so there is no "show in folder") |
| Dump retention | keep the last 5, delete anything older than 24 h at startup (the files contain the interviewer's voice, i.e. other people's audio) |
| Logs | `tauri-plugin-log` file target in the app log dir, 1 MB per file, 5 files kept (3.8) |
| Autostart | never: capture starts only when you press Start; a launch shows Idle |
| Window reload (F5 / HMR) | the engine keeps running; the UI calls `get_engine_status`, then `start_capture(sameTarget, newChannel)` to rebind (idempotent) |
| Persisted preferences | Phase 2: `localStorage` (last target `{mode, deviceId, stableId}`, refreshed from `StreamInfo`); from Phase 3 `tauri-plugin-store` |
| Time-limited evidence | the gate run writes `docs/phase2-gate.md`; WAVs used as evidence stay outside the repo (`*.wav` is gitignored) |

## 4. Phase 3 seam: local speech-to-text (design now, build after the gate)

**Model (measured on this CPU: faster-whisper 1.2.1, ctranslate2 4.8.4, int8, `cpu_threads=2`, language en, no VAD, TTS clips, median of 3 warm runs; run-to-run noise ~+-30 %; `cpu_threads=1` is NOT measured):**

| Model | 3.8 s clip | 9.6 s | 17.9 s | RSS after load / peak | Verdict |
|---|---|---|---|---|---|
| tiny.en | 0.90 s | 1.13 s | 1.25 s | 148 / 262 MB | automatic fallback (garbled "ASP.NET" / "PostgreSQL" on noisy speech) |
| **base.en** | **1.86 s** | **2.05 s** | **2.42 s** | 185 / 363 MB | **default** |
| small.en | 5.40 s | 7.42 s | 9.48 s | 362 / 759 MB | opt-in accuracy mode only (RTF ~0.8 at 10 s) |

Parameters: `beam_size=1` (beam 5 adds 0.1-1 s, no gain on clean audio), `temperature=0.0`, `condition_on_previous_text=False`, `without_timestamps=True`, `vad_filter=False` (Rust already segmented), `no_speech_threshold=0.6`, `log_prob_threshold=-1.0`, `compression_ratio_threshold=2.4`, `hotwords` = user-editable stack list. Encoder cost is flat (30 s padded window), so send whole utterances <= 12 s. With one core busy by a synthetic burner, base.en measured 2.7 / 3.3 / 3.6 s: budget 1.3-2x; that is not a real video call and not under memory pressure. Warm-up transcribe before `ready`. English only (`.en`), confirmed by Marcos (Q2); multilingual models are out of scope.

**CPU policy (E1, E9).** The worker must not starve the browser call that runs on the same two cores, and two Whisper threads saturate both cores for 2-4 s per utterance, which the interviewer would notice as call stutter. (1) The worker is spawned at `BELOW_NORMAL_PRIORITY_CLASS` (`IDLE_PRIORITY_CLASS` while the model loads); the parent sets this through creation flags (`std::os::windows::process::CommandExt::creation_flags`) or `SetPriorityClass` on the child handle right after spawn, independently of the spawn API. (2) The parent opts the child out of execution-speed throttling (`SetProcessInformation` on the child handle), because a descendant of a window-owning process inherits Low QoS while the app window is occluded. (3) `cpu_threads` is a config value; **the initial default is 1** until a measurement under a real call shows that 2 leaves the call unaffected (1-thread latency is unmeasured; expect it to be markedly slower than the table above). (4) Automatic fallback to `tiny.en` when measured `proc_ms` exceeds N x `audio_ms` (N tuned at the gate; start at 1.5). (5) The capture and DSP threads stay MMCSS "Audio" and are never starved by the worker.

**Process.** Spawn on Start, exit 5 min after Stop, via `tauri-plugin-shell` 2.6.0 (`set_process_group(true)` + `kill_on_drop(true)` = Windows job object, CREATE_NO_WINDOW). Dev: `<venv>\python.exe -I stt-worker\ic_stt_worker.py` (the PSF-signed python loads the unsigned wheels under SAC; tested). Release: PyInstaller `--onedir` (238 MB) shipped as a resource folder and spawned by path (an onedir cannot be a lone `externalBin`; inference). `app.shell().command()` signature and scope behaviour for Rust-side calls are unverified (confirm in Phase 3 step 1; fallback `std::process::Command` + `win32job` 2.0.3, which also gives direct control of creation flags). Venv and models live outside the repo (`%LOCALAPPDATA%\interviewer-copilot\{stt-venv,models}`). Pins: `faster-whisper==1.2.1 ctranslate2==4.8.4 av==19.0.2 onnxruntime==1.31.1 tokenizers==0.23.3 huggingface_hub==2.4.0 numpy==2.5.4`. A single-instance guard (`tauri-plugin-single-instance` 2.5.5) prevents two workers.

**Wire protocol v1**

| Direction | Frame | Payload |
|---|---|---|
| Rust -> worker (stdin, binary) | `[u8 type][u32 LE seq][u32 LE len][payload]` | `0x01 CONFIG` JSON `{model, model_dir, cpu_threads, beam_size, hotwords, initial_prompt, offline}`; `0x02 AUDIO` = u64 LE capture_start_ms + s16le mono 16 kHz PCM (seq = utterance id, <= 384 KB); `0x03 CANCEL`; `0x04 PING`; `0x7F SHUTDOWN` |
| worker -> Rust (stdout, one JSON per line, `ensure_ascii`; fd 1 is dup'd to stderr so stray prints cannot corrupt it) | JSON lines | `hello{proto,pid}`, `loading`, `ready{model,load_ms}`, `result{seq,t_cap_ms,text,audio_ms,proc_ms,avg_logprob,no_speech_prob}`, `dropped{seq,reason}`, `error{seq,code,message}`, `pong`, `bye` |

Back-pressure: the worker's reader thread is separate from inference (pings answer during inference); inference queue 2 with drop-oldest + `dropped`; Rust keeps <= 2 utterances in flight, writes from a dedicated thread (`CommandChild::write` blocks), watchdog `max(15 s, 4x audio)` -> kill + restart, restart backoff 1/2/4/8/15 s, give up after 5 in 60 s (`whisperStatus: crashed`). stdin EOF and the job object kill orphans.

**Flow (all in Rust except transcription):** `FrameSink` (`Segmenter`) -> `SpeechEnd` -> extract PCM from `PcmHistory` -> `Utterance{seq, t_start, t_end, pcm}` -> AUDIO frame -> `result` -> post-process (regex table: "C-sharp"/"C sharp" -> "C#", "dot net" -> ".NET", "postgres sql" -> "PostgreSQL"; drop if `no_speech_prob > 0.6`, `avg_logprob < -1.0`, `compression_ratio > 2.4`, or a blocklist like "Thank you.") -> `TranscriptEvent::Utterance` -> `TurnAssembler` (pure, unit-testable): a turn stays open while speech continues or STT results are in flight, closes at `max(last result, last speech end + T_end)`; `is_question` = `?` or interrogative / imperative starter ("what, why, how, can you, walk me through, tell me about, describe, explain, I'd like you to ..." after stripping fillers) -> `TurnClosed{id, text, isQuestion, score}` -> Phase 4. The UI only renders and may force "Answer now". `T_end` is configurable (default 1.4 s), the UI shows a "listening..." state while a turn is open, and a later `turnClosed` for the same question supersedes a premature answer. Why Rust owns it: it owns the capture clock, it is unit-testable, and it keeps working if the webview is throttled; the worker stays stateless so a cloud STT could be swapped in behind the same JSON schema (opt-in only: audio would leave the machine).

```ts
type TranscriptEvent =
  | { event: "whisperStatus"; data: { state: "loading" | "ready" | "busy" | "crashed" | "stopped"; model: string; queue: number; lastProcMs?: number } }
  | { event: "utterance"; data: { seq: number; tStartMs: number; tEndMs: number; text: string; procMs: number; filtered?: "noSpeech" | "lowConfidence" | "blocklist" } }
  | { event: "turnClosed"; data: { id: number; text: string; isQuestion: boolean; score: number; silenceMs: number; utteranceSeqs: number[] } };
```

**Latency budget** (interviewer stops speaking -> first answer token). These are **targets to be measured at the Phase 3 / 4 gates, not promises**:

| Stage | Idle | Loaded | Source |
|---|---|---|---|
| Worker cold start after Start (spawn + model load; UI shows `loading`, capture continues; early questions are queued or reported as missed) | to be measured | could be tens of seconds under paging *(unverified)* | measure at the Phase 3 gate on the real machine state |
| VAD hangover until `SpeechEnd` | 0.6 s | 0.6 s | design parameter |
| Whisper base.en beam 1 | ~2.0 s (1.9-2.4) with 2 threads | ~3.3 s (2.7-3.6) with one core busy | measured (2 threads, synthetic burner); 1-thread and real-call figures pending |
| Turn close (`T_end` 1.4 s, 0.7 s if `?`) | 0 (already elapsed during STT) | 0 | STT dominates for short utterances; a long final utterance (up to 12 s) costs ~3.6 s of Whisper alone when loaded; the 0.7 s fast path depends on a "?" that base.en often omits |
| Anthropic connection | pre-warmed on Start (one `HEAD` to the API host, no key; re-warm after 60 s idle) -> 0 | 0 | avoids cold DNS/TLS on the first question |
| LLM time-to-first-token | ~1 s | ~1-2 s | estimate, measure in Phase 4 |
| **Total** | **~3.6 s** | **~5-6 s** | target |

Knobs if too slow: hangover 400 ms, auto-fallback to tiny.en (~1 s), a speculative first transcription of long utterances.

**Phase 3 gate (short):** protocol self-test with a bundled WAV (`proc_ms` shown); 20 recorded interviewer-style utterances (clean TTS, compressed call audio, Bluetooth call-mode (HFP) audio dumped during M11, chimes, music, silence+noise) with acceptable transcripts and no hallucinated questions; p50/p95 latency measured **under a real browser video call with the webcam on** (not a synthetic CPU burner) and with the machine's real memory pressure, for `cpu_threads` 1 and 2, together with the impact on call quality (frame drops, audio glitches on the call side) and the pass/fail decision for the default thread count; cold-start time; no orphan Python after exit or `taskkill` of the app.

## 5. Phase 4 seam: Anthropic client (design now)

- **Placement:** Rust (`src-tauri/src/llm/`: `client.rs` reqwest + SSE parser, `prompt.rs`, `sections.rs` incremental section parser, `secrets.rs`, `ipc.rs`). The key reaches JS only once, when Marcos types it into the settings field (`set_api_key`); it is never stored there, returned or displayed back. One request in flight; a new turn aborts the previous (`JoinHandle::abort` drops the connection).
- **HTTP:** `reqwest` 0.13.5 with `default-features = false, features = ["native-tls", "stream", "system-proxy", "json"]` (all four exist in the 0.13.5 feature table; `json` is required for `.json(&body)` / `.json::<T>()`, otherwise build bodies with `serde_json` and `.body()`; `http2` and `charset` are dropped on purpose: SSE works over HTTP/1.1, add `http2` only if connection reuse proves slow). Reason: 0.13's `default-tls` is rustls + aws-lc-rs (crates.io feature table), which compiles C code and may need CMake/NASM on Windows (unverified); neither is installed here. native-tls uses SChannel and needs no extra build tools (standard behaviour, not tested here).
- **Request** (checked against the Anthropic migration guide for API version 2026-10-21): `POST https://api.anthropic.com/v1/messages`; headers `x-api-key`, `anthropic-version: 2026-10-21`, `content-type: application/json`; body `{model, max_tokens (~1500), stream: true, system, messages, output_config: {effort: "low"}}`. The guide's raw-HTTP cURL example uses `x-api-key`, and it states "The Claude API accepts a key in either header" (only the SDKs and the CLI switched to `Authorization: Bearer`); Phase 4 step 1 still smoke-tests one raw request and expects 200. **Do not send** `temperature` / `top_p` / `top_k` (400 on every model under that version), assistant prefill (400), or `thinking: {type: "disabled"}` (400 on Opus 5.5 / Sonnet 5.5); omit `thinking` and steer latency with `effort`.
- **Stream:** SSE events `message_start`, `content_block_start`, `content_block_delta` (`delta.type == "text_delta"`), `content_block_stop`, `message_delta` (`stop_reason`, usage), `message_stop`; `ping` and in-stream `error` events are handled defensively (their exact shapes are not shown in the docs excerpt: unverified). HTTP errors: 400/401/403/404/413 not retryable; 429/500/529 retry up to 2x (0.5 s, 1.5 s, honour `retry-after`) **only before the first token**; after that surface the error and offer Retry.
- **Model:** a setting, never hard-coded. Candidates from the skill's model table (cached 2026-10-06): `claude-sonnet-5-5` (\$2/\$10 per MTok), `claude-haiku-5-5` (\$0.10/\$0.50), `claude-opus-5-5` (\$4/\$20). Phase 4 picks the default with a 5-question bake-off (time-to-first-token + your judgement of answers); the selector may be fed by `GET /v1/models`. Budget (Q7): Marcos has a key and about US$120 of prepaid credit, so the bake-off also reports the measured cost per interview (usage from `message_delta` x current prices) for each candidate. Model ids, prices and the API-version rules in this section are re-verified with the `claude-api` skill at the start of Phase 4, before any code (the brief's `claude-3-5-sonnet` is retired).
- **Prompt and persona inputs:** system = static persona (from the brief: Marcos, Brazil, trainee, stack list; the full text is typed into the app and never committed) + hard constraint "never fabricate seniority or production ownership" + output contract of five headed sections in fixed order: QUESTION, INTENT, SUGGESTED ANSWER (English), KEY CONCEPTS, LEARNING NOTES (PT-BR). User message = previous 2 turns + the closed turn (or the manual override text). The persona text is user-editable (`tauri-plugin-store`). Add `cache_control` only if the system prompt exceeds the model's minimum cacheable prefix (512-4096 tokens, model dependent), otherwise it silently will not cache.
- **Streaming to the UI:** per-request `Channel<AnswerEvent>`; `sections.rs` turns `## SECTION` headers in the text stream into `section` events and routes text to `delta`.
  ```ts
  type AnswerEvent =
    | { event: "started"; data: { requestId: number; turnId: number | null; model: string } }
    | { event: "section"; data: { requestId: number; name: "question" | "intent" | "suggestedAnswer" | "keyConcepts" | "learningNotes" } }
    | { event: "delta"; data: { requestId: number; text: string } }
    | { event: "done"; data: { requestId: number; stopReason: string; inputTokens: number; outputTokens: number } }
    | { event: "error"; data: { requestId: number; error: IpcError } };   // adds llmAuth, llmRateLimited, llmOverloaded, llmBadRequest, llmNetwork, noApiKey
  ```
- **Key storage:** `keyring-core::set_default_store(windows_native_keyring_store::Store::new()?)` once in `setup` (exact return type to confirm at compile time); `Entry::new("interviewer-copilot", "anthropic-api-key")`; commands `set_api_key`, `has_api_key`, `clear_api_key`; none returns the key. Dev-only fallback: env `ANTHROPIC_API_KEY` read in Rust under `cfg!(debug_assertions)`.
- **Trigger and privacy:** automatic on `turnClosed{isQuestion}`, or Enter / global hotkey (`tauri-plugin-global-shortcut` 2.4.1 with `global-shortcut:allow-register|unregister|is-registered`). Only text (turns + persona) leaves the machine; a first-run notice says so.

## 6. UI mapping

| UI region | Shows | Source (IPC) | Phase |
|---|---|---|---|
| Top bar: device status | name, format, follow/pinned, reconnecting (fast) / **waiting** (persistent banner with the typed cause and "Use system default" / "Stop") / failed | `started`, `reconnecting`, `failed`, `stopped`; `get_engine_status` | 2 |
| Top bar: capture indicator | pulsing dot, mini meter, "no packets" (only when `packetsFlowing` is false), "silence" (derived in the UI: rms <= -85 dBFS for 500 ms), heartbeat warning | `level.packetsFlowing`, `level.rmsDb`, `stats.heartbeatAgeMs` | 2 |
| Top bar: Whisper status | loading / ready / busy / crashed + last `procMs` | `TranscriptEvent.whisperStatus` | 3 |
| Top bar: model selector | LLM model id (setting) | `get_llm_settings` / `set_llm_model` | 4 |
| Left sidebar (collapsible) | timestamped utterances, turn markers, filtered items dimmed | `utterance`, `turnClosed` | 3 |
| Main view | question card, streaming answer sections, key concepts, PT-BR notes | `AnswerEvent` | 4 |
| Footer: device switch | "System default" + pinned devices, Refresh; a saved pinned device that is gone stays selected, shows its typed error, and offers "Use system default" (never switches by itself) | `list_outputs`, `start_capture(newTarget)`, `audio:devices-changed` | 2 |
| Footer: Start / Stop | toggle (never auto-started) | `start_capture` / `stop_capture` | 2 |
| Footer: manual input | text -> forces a turn | `submit_manual_turn(text)` | 3-4 |
| Phase 2 extras | dBFS bar (-90..0, hold tick, CLIP, idle label), stats panel (including resyncs, positionResets, packetAgeMaxMs), "Dump last 10 s WAV" with path + copy button | `level`, `stats`, `dump_wav` | 2 |

Rendering rules: the level handler writes into a mutable ref and a `requestAnimationFrame` paints the bar only when a new frame has arrived (no React state per 25 Hz frame, no repaint at display rate when nothing changed); lifecycle state lives in one `useReducer`; no product logic in the webview (it can be throttled when occluded); the UI reports visibility with `set_ui_visible` (Level drops to 2 Hz while hidden); last device choice in `localStorage` for Phase 2 (per-viewer convenience), `tauri-plugin-store` from Phase 3.

## 7. Dev environment (written for the 2-core laptop; h0wzy-pc differences in `docs/SPEC.md` section 2)

> **Consent gate and portability.** Do not run any command of this section before Marcos has answered SPEC P-1 and P-2 (installs need his explicit authorization; SPEC S0). The winget installs raise UAC prompts, so Marcos must be at the keyboard; if winget stops at an agreements prompt that a non-interactive shell cannot answer, add `--accept-package-agreements --accept-source-agreements` only after he has accepted the licences. Run each numbered block as one PowerShell session or script: `$repo` and `$g` do not survive separate tool calls. Laptop-only lines that do not apply on h0wzy-pc: `CARGO_BUILD_JOBS`, 'keep `target/` on C:' (use an SSD, SPEC 2.2), closing the browser, the build-time estimates and the Defender exclusions.

```powershell
$repo = (Get-Location).Path   # run this block from the root of your clone (keep the clone on an SSD)
# 0) Decide Smart App Control first (section 9, Q1). State: Windows Security > App & browser control > Smart App Control. Plan B (keep it On) is the default.
# 1) C++ toolchain (UAC prompt; only the two components Tauri needs). Fallback if 18.x misbehaves: --id Microsoft.VisualStudio.2022.BuildTools, same --override.
winget install --id Microsoft.VisualStudio.BuildTools --source winget --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --addProductLang En-us"
# 2) Rust (winget id verified: rustup 1.29.1). Open a NEW terminal afterwards. The installer may already add the default toolchain; the next line is then a no-op.
winget install --id Rustlang.Rustup --source winget
rustup set profile minimal ; rustup toolchain install 1.99.1-x86_64-pc-windows-msvc -c clippy -c rustfmt ; rustup default 1.99.1-x86_64-pc-windows-msvc
#    If SAC blocks the winget-installed rustup-init.exe: STOP and report to Marcos (SPEC S0: any block => stop and ask); only if he agrees, download it without Mark-of-the-Web and run it (curl.exe does not add a Zone.Identifier stream; check with: Get-Item <file> -Stream *):
#    curl.exe -L -o "$env:TEMP\rustup-init.exe" https://static.rust-lang.org/rustup/dist/x86_64-pc-windows-msvc/rustup-init.exe ; & "$env:TEMP\rustup-init.exe" -y --default-toolchain 1.99.1 --profile minimal
# 3) Node LTS (winget id verified: 26.13.0 "Lithium"; Vite 8 needs >= 20.19 / 22.12)
winget install --id OpenJS.NodeJS.LTS --source winget
# 4) STEP 0 GATE. One "Hello" proves little (1 of 10 fresh unsigned exes was blocked here), so the gate is representative. ANY block => stop and report to Marcos; do not change SAC yourself.
rustc -Vv ; cargo -V ; node -v ; npm -v
# 4a) Rust: build scripts + proc-macros (serde_derive, thiserror, windows) and five freshly linked exes; none may show "os error 4551"
$g = "$env:TEMP\ic-smoke" ; cargo new $g ; cd $g
cargo add serde --features derive ; cargo add thiserror@2 ; cargo add log@0.4 ; cargo add windows@0.62.2 --features Win32_Media_Audio
1..5 | ForEach-Object { (Get-Content src\main.rs) -replace 'Hello[^"]*', "Hello $_" | Set-Content src\main.rs ; cargo build ; if ($LASTEXITCODE -ne 0) { break } ; & ".\target\debug\ic-smoke.exe" }
# 4b) Frontend native binaries (the bundler, lightningcss, ... are unsigned .node/.exe files); answer the create-vite prompts without starting a server
cd $env:TEMP ; npm create vite@latest ic-fe-smoke -- --template react-ts ; cd ic-fe-smoke ; npm i ; npm i tailwindcss @tailwindcss/vite shadcn ; npm run build
# 4c) Evidence: any Code Integrity block in the last hour?
Get-WinEvent -LogName 'Microsoft-Windows-CodeIntegrity/Operational' -ErrorAction SilentlyContinue | Where-Object { $_.TimeCreated -gt (Get-Date).AddHours(-1) -and $_.Id -in 3033,3034,3076,3077,3118 } | Select-Object TimeCreated, Id, LevelDisplayName, Message | Format-List
# 5) Scaffold in a scratch dir, then copy (never into the non-empty repo). Flags from create-tauri-app README/args.rs; not executed end to end (unverified).
mkdir "$env:TEMP\ic-scaffold" ; cd "$env:TEMP\ic-scaffold"
npm create tauri-app@latest interviewer-copilot -- --manager npm --template react-ts --identifier com.interviewercopilot.app --yes
#    create-tauri-app 4.7.5 is a napi wrapper: node.exe loads an unsigned native module (optionalDependency create-tauri-app-win32-x64-msvc). If SAC blocks it, hand-write the scaffold from the template files
#    (tauri-apps/create-tauri-app, templates/template-react-ts: package.json.lte, vite.config.ts.lte, tsconfig*.json, src-tauri Cargo.toml.lte, tauri.conf.json, capabilities/default.json),
#    or use `cargo install create-tauri-app --locked` (crate 4.8.0), which meets the same SAC risk as any cargo output.
robocopy "$env:TEMP\ic-scaffold\interviewer-copilot" $repo /E /XC /XN /XO /XD node_modules target .git   # never overwrites existing files; merge the scaffold's .gitignore into ours by hand
cd $repo
npm i tailwindcss @tailwindcss/vite lucide-react ; npm i -D @types/node
# (config edits below), then shadcn. `-y` only skips the confirmation; without --base it still asks for the component library and without --preset it shows an interactive preset list;
# `-t vite` applies only to an empty directory and this one has a package.json. Non-interactive form (not executed here, unverified); if any prompt still appears, answer Radix + a Lucide-icon preset:
npx shadcn@latest init --base radix --preset nova -y ; npx shadcn@latest add button card badge select switch scroll-area tooltip -y
# keep TypeScript at the template's ~6.0.3 (npm latest 7.0.2 not validated with shadcn / Vite 8)
# 6) Rust dependencies (audioadapter-buffers is NOT a direct dependency: rubato re-exports it)
cd src-tauri
cargo add wasapi@=0.26.0 rtrb@0.4.1 rubato@5.0.1 thiserror@2 log@0.4 tauri-plugin-log@2
cargo add windows@0.62.2 --features Win32_Foundation,Win32_Media_Audio,Win32_System_Threading,Win32_System_Performance,Win32_UI_Shell_PropertiesSystem,Win32_System_Com_StructuredStorage,Win32_System_Variant   # last three: for the StableId read; adjust to what cargo check asks
```

Config edits after step 5 (TypeScript 6.0.3 is the template's pin, so these differ from older shadcn/Vite guides):
- `tsconfig.json`: add only `"paths": { "@/*": ["./src/*"] }` inside `compilerOptions`. **No `baseUrl`**: TypeScript 6.0 deprecates it (a deprecation error unless `"ignoreDeprecations": "6.0"`; removed in 7.0; migration = drop it and prefix each `paths` entry with `./src`), and the template's `npm run build` is `tsc && vite build`. The template has one `tsconfig.json` plus `tsconfig.node.json` (there is no `tsconfig.app.json`).
- `tsconfig.node.json`: add `"types": ["node"]` (TS 6.0 defaults `types` to `[]`; `noUncheckedSideEffectImports` is now on, which the template's `src/vite-env.d.ts` already satisfies for CSS imports).
- `vite.config.ts`: replace the template file with the following, and **delete the template's `// @ts-expect-error` line** above the `node:process` import (with `@types/node` installed it becomes an unused-directive error, TS2578):
  ```ts
  import { defineConfig } from "vite";
  import react from "@vitejs/plugin-react";
  import tailwindcss from "@tailwindcss/vite";
  import process from "node:process";
  import { fileURLToPath, URL } from "node:url";
  const host = process.env.TAURI_DEV_HOST;
  export default defineConfig(() => ({
    plugins: [react(), tailwindcss()],
    resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
    clearScreen: false,
    server: { port: 1420, strictPort: true, host: host || false, hmr: host ? { protocol: "ws", host, port: 1421 } : undefined, watch: { ignored: ["**/src-tauri/**"] } },
  }));
  ```
- `src/index.css`: `@import "tailwindcss";`. After `shadcn init`, confirm that it detected the `@/*` alias; if not, set the aliases in `components.json` by hand.

`src-tauri/Cargo.toml` additions (the template's release profile has `panic = "abort"`, which would defeat `catch_unwind`):

```toml
[profile.dev]
debug = "line-tables-only"          # smaller objects and link step; incremental stays on
[profile.dev.build-override]
opt-level = 0
debug = false
[profile.dev.package.rubato]   # DSP crates optimised even in dev; everything else stays at opt-level 0
opt-level = 3
[profile.dev.package.realfft]
opt-level = 3
[profile.dev.package.rustfft]
opt-level = 3
[profile.release]               # template default is fat LTO + 1 codegen unit (very slow/RAM-hungry here); not measured
lto = "thin"
codegen-units = 8
opt-level = 3
strip = true
panic = "unwind"
```
Also: on the 2-core laptop only, set `CARGO_BUILD_JOBS=2` in the shell (1 if the machine swaps); do not commit a `jobs` cap, h0wzy-pc keeps cargo's default; no custom linker; `rust-toolchain.toml` -> `channel = "1.99.1"`, `profile = "minimal"`, `components = ["clippy", "rustfmt"]`; commit `Cargo.lock` and `package-lock.json`. If `dspBusyPct` in a debug build exceeds 5 %, add `[profile.dev.package.<app>] opt-level = 1`.

**Tauri config** (`tauri.conf.json`, `capabilities/default.json`): `identifier com.interviewercopilot.app`, a real window title and size; CSP instead of the template's `null`: `default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src ipc: http://ipc.localhost` (directive shape from the Tauri CSP docs, whose own example also lists `'unsafe-inline'` in `style-src`; add only what the build needs, since Rust does all network I/O and the webview needs none). Why `style-src`: `default-src 'self'` blocks `<style>` elements and style attributes created at runtime, and shadcn's Radix `ScrollArea` injects a `<style>` element. Tauri 2.12.5 appends `'nonce-...'` and hashes to `style-src` only when the bundled HTML itself contains `<style>` elements or style hashes (verified in `crates/tauri/src/manager/mod.rs`, `replace_csp_nonce`); a Vite production `index.html` links its CSS and contains none, so `'unsafe-inline'` stays effective. **Do not add an inline `<style>` to `index.html`**: once a nonce is present in the directive, browsers ignore `'unsafe-inline'`. If a release build still shows broken styling, set `app.security.dangerousDisableAssetCspModification` to `["style-src"]` or avoid `ScrollArea` (plain `overflow-y: auto`). Test a **release** build with the scroll-area component early (S1), because the failure is visual and invisible in dev. Also: `app.security.headers` with the Permissions-Policy header from 3.4; capability `core:default` only until Phase 3. Remove the template's `greet` demo command, `tauri-plugin-opener` and its `opener:default` permission (unused surface, longer first build).

**Build performance (2 cores, 7.3 GB, 0.3-0.7 GB free):** close the browser, Spotify and Steam during the first build; use `cargo check` / `cargo test --lib` for iteration; avoid `cargo clean` (use `cargo clean -p <crate>`); keep `target/` on C: (~30 GB free; a Tauri debug target is several GB, unmeasured); `npm run tauri dev` for the loop. Optional, needs admin and your consent: Defender exclusions for `target\`, `node_modules\`, `%USERPROFILE%\.cargo` (commonly helps; unmeasured). **Expected times (estimates, not measured):** VS Build Tools install 10-30 min; first `tauri dev` 15-40 min; incremental rebuild 1-5 min; first `npm install` 2-5 min.

**Smart App Control notes.** Enforced here (policy state 1, Windows 11 25H2, build 26200.9457, read on 2026-11-01). Microsoft's guidance is to sign every exe/dll; its support FAQ says there is no way to bypass SAC for an individual app; reputation can change over time. Evidence: 1 of 10 locally built unsigned .NET exes was blocked (CodeIntegrity events 3033/3077/3118); `cargo.exe` of an official toolchain was blocked elsewhere (os error 4551, rust-lang/rust#160163); `rustup-init.exe` is unsigned (rustup#1568); a Tauri debug build compiles hundreds of build-script executables and proc-macro DLLs plus the final exe, so at roughly 10 % per fresh binary a block is likely sooner or later; yet the venv `python.exe` (PSF-signed) loaded unsigned wheels and an unsigned PyInstaller exe ran here. The frontend toolchain (the bundler, lightningcss and other native binaries from npm, and `create-tauri-app`'s native module) is unsigned native code too, which is why step 4 covers it.

**Switching SAC Off is effectively one-way.** Microsoft Learn ("Test your app's signature with Smart App Control") states: "Configuring Smart App Control to Off or On (enforcement) is a one-way operation. You can't change modes by using Windows Settings unless the current setting is Evaluation." Microsoft's support wording has varied during 2026 (a January 2026 KB note about turning it on and off without a clean install was withdrawn from the documentation in February), so do not rely on reversibility unless Settings itself offers "On" on this build. DRAFT v1's claim that "recent Windows updates can turn it back on" has no primary source and has been removed.

**Plan B (default; needs nothing from you):** keep SAC On, run the representative Step 0 gate (step 4), pin the toolchain, and on a block STOP: read Event Viewer > Applications and Services Logs > Microsoft > Windows > CodeIntegrity > Operational (read-only), report to Marcos, and only with his agreement run `rustup toolchain install 1.99.1 --force` and retry (SPEC S0: any block => stop and ask). Keep MSVC `link.exe`, not `rust-lld`. **Plan A (escape hatch):** turn SAC Off, only with your explicit consent and only after a documented block of a development tool that Plan B could not fix, accepting that it may be permanent; I will not change a security setting without consent. Shared builds need signing.

**Repo hygiene.** The repo's `.gitignore` already covers `.env*`, `ENG_CV_*.pdf` / `*CV*.pdf`, `systeminfo*` (personal data: email, IPs, product id), `*.wav`, build outputs (`node_modules/`, `dist/`, `target/`, `src-tauri/gen/`), venvs and `models/`. At S1 merge the scaffold's own `.gitignore` into it by hand and never commit personal data (CLAUDE.md).

## 8. Phase 2 implementation plan

| # | Step | Files | Done when | Parallel |
|---|---|---|---|---|
| S0 | Toolchain + SAC gate (representative, section 7 step 4) | none (machine) | versions logged; if SAC is On or in Evaluation (SPEC 2.2, otherwise skipped): five fresh exes ran, the scratch frontend built, no CodeIntegrity block events; any block => stop and ask you | blocks all |
| S1 | Scaffold + config | `package.json`, `vite.config.ts`, `tsconfig.json`, `tsconfig.node.json`, `src/index.css`, `components.json`, `src-tauri/{Cargo.toml,tauri.conf.json,capabilities/default.json}`, `rust-toolchain.toml`, `.gitignore` | `npm run tauri dev` opens the template window (start it early, in the background, 15-40 min); `npm run build` and `cargo check` pass; a **release** build renders the shadcn scroll-area with the CSP | serial |
| S2 | Contract commit | `audio/{mod,types,error,pipeline}.rs` (wire types in `types.rs`; the `PipelineFactory` / `PipelineHandle` traits in `pipeline.rs` so that `engine.rs` compiles against them), `src/lib/ipc.ts` (types), golden tests T10, `classify` test T11 | `cargo check`, `cargo test --lib`, `tsc --noEmit` pass | serial, short; unblocks P*, S3 |
| P1 | DSP | `audio/dsp/*`, `PcmHistory` (10 s), `wav.rs`, tests T1-T4 | `cargo test --lib dsp::` green (resampler + meter calibration included) | parallel group P |
| P2 | Timeline / convert / ring / platform | `audio/{timeline,convert,ring,platform}.rs`, tests T5, T6 and T7 (ring part) | green | P |
| P3 | Render-only layer | `audio/render_only.rs` (incl. stable ids, OS-queried flow check, device-list worker), `tests/no_mic_guard.rs`, `tests/hw_*.rs` | T8 green; `cargo test -- --ignored` passes H1-H5 on this PC | P (needs the machine) |
| P4 | Frontend on mock IPC | `src/lib/{ipc,mockIpc}.ts`, `src/features/audio/*`, `src/components/*`, `scripts/check-nomic.mjs` + `package.json` script `check:nomic` (ripgrep is not installed) | `npm run build`; mock mode shows moving meter, dropdown, banners (including Waiting and an unavailable pinned device) | P (Node only) |
| S3 | Engine + console proof | `audio/engine.rs`, `examples/loopback_console.rs` (supplies a console `PipelineFactory` that feeds `dsp::Meter` directly, so it needs no S4 code) | with audio playing the console meter rises, pause -> -90 and the stream stays a continuous zero stream (`synthFrames` ~0 on this driver); 5 min, no panic or drops | serial after S2, P1 (Meter), P2, P3 |
| S4 | Pipeline | `audio/pipeline.rs` (implementation: `ic-dsp` thread, MMCSS, history, dump) | T9 green | serial after P1, S3 |
| S5 | IPC, lifecycle, logging | `audio/ipc.rs`, `lib.rs` (incl. power-throttling opt-out), `tauri.conf.json` | in `tauri dev`: list/start/stop work from the devtools console; closing the window ends the engine < 3 s | serial |
| S6 | Frontend integration | swap mock -> real IPC, error and Waiting banners, visibility reporting, `audio:devices-changed` | M1, M2 by eye | serial after S5, P4 |
| S7 | Gate run | `docs/phase2-gate.md`, `tools/diagnostics/{check_wav.py,play-tone.ps1}` | T1-T11, H1-H5, M1-M11 filled with evidence (M11 may stay `pending` only as 3.9 allows); you confirm | serial; needs you |
| S8 | Optional, post-gate | `list_outputs(withPeek)`, process-loopback mode, `sink.rs` + `FrameAssembler` + 30 s history if they have not landed earlier | not part of the gate | later |

"Parallel" means parallel *authoring* of file-disjoint work. Cargo locks `target/`, so cargo commands run one at a time (2 cores, 7.3 GB); P1-P3 use `cargo test --lib <module>` from a single integrator. The console example in S3 uses only the engine API, so it does not touch the single-token rule. Dependency fix versus DRAFT v1: S3 no longer needs S4 (the pipeline traits exist from S2) and it needs the meter from P1.

## 9. Risks and open questions

| # | Risk | Sev | Mitigation |
|---|---|---|---|
| 1 | Smart App Control blocks cargo / rustc / rustup-init / build scripts / proc-macros / npm native binaries / our exe; blocks can appear days later; switching SAC Off is effectively one-way | High | Q1; representative Step 0 gate; Plan B first; toolchain pin; MSVC `link.exe`; reinstall procedure (7) |
| 2 | CPU starvation drops audio (22 ms buffer lost 38-174 ms per 7.4 s under 2x load; probe was managed .NET, indicative); Low QoS when the window is occluded on battery | High | 1 s buffer, lean capture thread, 2 s ring, device-position gap synthesis, MMCSS on both threads, process power-throttling opt-out, UI counters, M8 (occluded, battery and AC) |
| 3 | RAM: 0.3-0.7 GB free; WebView2 tree ~100-250 MB (guess, unmeasured), Python worker 185-363 MB; CPU: Whisper competes with the call | High | small buffers, tiny.en fallback, one worker at BELOW_NORMAL with `cpu_threads` 1 by default, `GlobalMemoryStatusEx` check before load (Phase 3), close Spotify/Steam, real-call gate |
| 4 | First build 15-40 min and memory thrash | Med (laptop) / Low (h0wzy-pc) | `CARGO_BUILD_JOBS=2` on the laptop, line-tables-only, `cargo check` loop, DSP crates -O3 only |
| 5 | Idle loopback behaviour on endpoint classes other than this Realtek (HDMI, USB, Bluetooth): verified only here (continuous zero packets) | Low-Med | design independent of it (watchdog, idle credit, level-only silence); M7 per class records it |
| 6 | `wasapi` churn (0.27 breaking; later `windows-core` 0.100 / MSRV 1.95) and `unwrap()`s | Med | exact pin, usage confined to `render_only.rs` / `engine.rs`, avoid panicking APIs, `catch_unwind`, vendoring (MIT, ~5k lines) or direct `windows` bindings as fallback |
| 7 | Bluetooth on Windows 11: a call flips the unified endpoint to HFP, changing the mix format (8/16 kHz mono, hurts STT); whether the loopback stream is invalidated is undocumented. Marcos will use a Bluetooth headset (Q3), so this is a real path, not an edge case | Med-High | reopen logic, indefinite `Waiting` retry, format shown in the UI (e.g. "headset in call mode: 16 kHz mono"), M11 required, call-mode clips in the Phase 3 test set; the speakers stay the reference path |
| 8 | Mic capture by mistake | High impact | 3.4: type, OS-queried + cached flow checks, single-token lint, runtime logs |
| 9 | Loopback mixes all apps; DRM-protected audio is withheld by trusted drivers; per-app mute silences it | Low-Med | UI hint + OS meter; process-loopback mode (S8) |
| 10 | STT / VAD quality on real call audio unproven (tests used clean TTS; earshot accuracy is vendor-reported) | High (P3) | recorded test set, tiny fallback, tunable thresholds, Silero escalation |
| 11 | Webview throttled when occluded | Low | no product logic there; `set_ui_visible` |
| 12 | Personal data in the repo (CV PDF, systeminfo.md) before the first commit | Med | 7, repo hygiene |
| 13 | Consent / terms: transcribing a call can require other parties' consent, many interview processes forbid AI help, and transcript text goes to Anthropic | Note | your decision, not an architecture constraint; first-run notice |
| 14 | Sleep/resume or a long stall produces a hole or invalidates the stream | Med | 500 ms cap with `Resync` reopen, idle credit, indefinite `Waiting`, M10 |
| 15 | WASAPI QPC stamps drift (~70 ppm) and bunch (up to 9 ms measured): any logic that edits PCM from them damages audio (DRAFT v1 did) | High if reintroduced | device position is the only continuity authority (3.3); T5 replay; M4 residual check |
| 16 | Endpoint ids change with driver updates, silently invalidating a saved Pinned device | Low | `stableId` + `endpointId` persisted; an unavailable pinned device shows a typed error and is never swapped automatically |

**Open questions for you**
1. **Smart App Control:** keep it On (Plan B, the default and the recommended first step) or turn it Off (Plan A)? Microsoft Learn documents Off as a one-way operation and 2026 support wording on re-enabling is inconsistent, so treat Off as permanent unless Settings offers "On" again on build 26200.9457. Plan A only after a documented CodeIntegrity block of a dev tool.
2. **Language: answered (2026-11-01):** English only (`.en` models).
3. **Output device: answered (2026-11-01):** laptop speakers and a Bluetooth headset; M11 is required, so pair the headset before the gate.
4. **Tests that need you:** OK to run M7 (60 s of silence per endpoint class), the M2b volume sweep, M8 on battery and on AC with the window covered, the M10 30 s sleep/wake, and M11 with your Bluetooth headset?
5. **Browser-only capture** (process loopback) to exclude Spotify/Steam: wanted right after the gate?
6. **Persona source (Phase 4):** editable text profile in the app (default) or parse the CV PDF?
7. **Anthropic: answered (2026-11-01):** key available, about US$120 of credit; the default model comes from the Phase 4 bake-off (latency, answer quality, cost per interview). The key is entered only in the app (Windows Credential Manager), never in chat, source or plain files.
8. **Distribution:** personal use only (unsigned is fine) or will you share builds (signing needed under SAC)?
9. **Installs:** accept the Visual Studio Build Tools license terms; allow optional Defender exclusions (admin)?
10. **OS-enforced loopback flag:** keep wasapi's `initialize_client` behind the checks of 3.4 (recommended for Phase 2), or own the `Initialize` call and the capture reads (about 80 lines of `unsafe`) so that Windows itself refuses anything but a render endpoint?
11. **Scope:** accept that `sink.rs` / `FrameSink`, the 30 s history, `FrameAssembler`, the activity peek and process loopback land after the gate (3.1), to keep the Phase 2 proof small?

## Appendix: Review log

Three adversarial reviews (Windows audio correctness; crates/versions/APIs; requirements and feasibility on this machine) produced 32 findings. Each was decided on the evidence. Where a finding conflicted with the draft, the primary source was re-read: Microsoft Learn (GetBuffer, GetPeakValue, Initialize, Loopback Recording, Bluetooth Classic audio, Quality of Service, PKEY_AudioEndpoint_StableId, Endpoint ID Strings, PROCESS_LOOPBACK_MODE, AvSetMmThreadCharacteristicsW, Smart App Control pages), the `wasapi` 0.26.0 source, the Tauri 2.12.5 source, the shadcn 4.23.0 `init.ts`, crates.io / docs.rs, the TypeScript 6.0 announcement, the Anthropic 2026-10-21 migration guide, the create-tauri-app template, and the reviewer's saved 240 s loopback trace. IDs: W = Windows-audio lens, C = crates/APIs lens, R = requirements lens, numbered in the order the reviewers listed them. Findings with a mixed outcome are split (a / b).

- **W1 ACCEPTED (blocker)** - QPC reconciliation re-checked against the 240 s trace (device position contiguous 23,853/23,853, stamps +68.5 ppm with +-9.4 ms bunching; DRAFT v1's logic made 11 pads and 3 deletions on a clean stream) and Learn's GetBuffer text; 3.3 now uses device position only, T5, M4 and the glossary were rewritten.
- **W2 ACCEPTED** - the drop branch deleted real audio and a TIMESTAMP_ERROR, zero or garbage stamp could cause an unbounded pad; PCM is now never trimmed, bad stamps mean "contiguous", synthesis is capped at 500 ms and larger holes become a Resync reopen (Learn: TIMESTAMP_ERROR only says the position is uncertain).
- **W3 ACCEPTED** - the 240 s trace answers E8 (continuous unflagged zero packets, 0 timeouts), so `active` could never be false on this box; renamed `packetsFlowing` (200 ms watchdog), silence defined by level in the UI, M7 per endpoint class, T9 covers zero-packet and no-packet modes, the helper render stream stays only a documented fallback.
- **W4 ACCEPTED** - arithmetic confirmed: with a 10 MHz QPC, `t * 10^7` overflows u64 after 184,467 s (51.2 h), after 12.8 min with a 2.4 GHz one; `qpc_to_100ns` uses u128 and is tested (T5 i), and QPC is no longer a clock for editing audio.
- **W5 ACCEPTED** - Learn says GetPeakValue "always retrieves the peak value recorded during the previous device period"; "since the previous call, volume-independent" came only from a wasapi doc comment, so `osPeakDb` is reworded, scheduled by deadline, kept as a shape/timing cross-check and no longer used as an oracle (M2b, M3).
- **W6 ACCEPTED** - verified in the wasapi 0.26.0 source (nonsense `get_buffer_size` in process loopback, `include_tree` doc contradicts the code) and Learn (Windows 10 build 20348+); the S8 notes now describe a separate open path with a bounded buffer, an explicit format and a typed unsupported error.
- **W7 ACCEPTED** - `register_notification_callback` returns a Result (checked in source), so the skeleton handles failure with a warning plus a 2 s default-device poll thread; CoInitializeEx failure is reported; ENDPOINT_CREATE_FAILED and E_NOTFOUND (Activate) map to DeviceLost and CPUUSAGE_EXCEEDED to a new retryable `Transient` (all three codes confirmed on Learn).
- **W8 ACCEPTED** - Learn (Bluetooth Classic audio) confirms the unified Windows 11 A2DP/HFP endpoint, HFP selection by microphone open or a Communications stream, and resampling to 8/16 kHz; the invalidation row, risk 7, T3 (8/16 kHz cases) and M11 were rewritten, and the helper-stream rejection no longer cites Bluetooth.
- **W9 ACCEPTED** - Learn's QoS table confirms Low QoS for minimized/occluded windows (inherited by descendants) with exemption only for Audible processes and MMCSS audio threads, and the box is a battery laptop (battery present when read); `ic-dsp` joins MMCSS, the process opts out of power throttling, M8 runs covered, on battery and on AC; whether the explicit tag overrides the visibility rule is undocumented, so M8 measures it.
- **W10 ACCEPTED** - Refresh does RPCs, allocations and (with peek) sleeps, which violates the capture-thread rule in CLAUDE.md; `list_outputs` now runs on a short-lived `ic-devlist` MTA worker with its own enumerator and `Ctl::ListDevices` is gone.
- **W11 ACCEPTED** - Learn (Endpoint ID Strings, PKEY_AudioEndpoint_StableId) confirms that endpoint ids change with driver updates and that `GetDevice` accepts the stable id from build 26100 (this box: 26200); `Target::Pinned` and `DeviceInfo` carry both ids; the PROPERTYKEY constant still has to be taken from the 26100 SDK header (not verified here).
- **W12a ACCEPTED** - confirmed in the wasapi source: `get_default_device_for_role` caches the direction we passed in and `initialize_client` sets LOOPBACK only for a cached Render direction, so an OS-queried `IMMEndpoint::GetDataFlow` check now precedes `Activate` and `Initialize`.
- **W12b REJECTED (deferred)** - owning the `Initialize` call and the capture reads so that Windows returns `AUDCLNT_E_WRONG_ENDPOINT_TYPE` costs about 80 lines of `unsafe` and wasapi's capture wrappers for a layer that would sit behind three existing checks on one choke point; the decision is recorded in 3.4 and put to you as open question 10.
- **C1 ACCEPTED** - TypeScript 6.0 announcement: `baseUrl` is deprecated (error unless `ignoreDeprecations: "6.0"`, removed in 7.0) and `types` defaults to `[]`; the template (TS ~6.0.3) has no `baseUrl`, one `tsconfig.json` plus `tsconfig.node.json`, and a `@ts-expect-error` line that breaks once `@types/node` is installed; step 5 rewritten.
- **C2 ACCEPTED** - shadcn 4.23.0 `init.ts`: `-y` only skips confirmation, `promptForBase()` runs without `--base`, `-t` matters only for an empty directory, and an interactive preset list also appears; the command is now `--base radix --preset nova -y` (marked unverified, not executed on this box).
- **C3 ACCEPTED** - create-tauri-app 4.7.5 is a napi wrapper whose native module is an unsigned optionalDependency (npm registry checked); a hand-scaffold / `cargo install` fallback is added to step 5 and the npm native binaries are part of the Step 0 gate.
- **C4 ACCEPTED (strengthened)** - the Tauri 2.12.5 source shows that a style nonce is appended to `style-src` only when the bundled HTML contains `<style>` elements or style hashes, so `style-src 'self' 'unsafe-inline'` works with a Vite build; the inline-`<style>` pitfall, the `dangerousDisableAssetCspModification` fallback and an early release-build test are recorded.
- **C5a ACCEPTED** - the crates.io feature table for reqwest 0.13.5 has `json` next to `native-tls`, `stream` and `system-proxy`, and `.json()` needs it; added, and the dropped `http2` / `charset` are documented.
- **C5b REJECTED** - the raw `x-api-key` header is correct: the 2026-10-21 migration guide's cURL example uses it and says "The Claude API accepts a key in either header" (only the SDKs and the CLI switched to Bearer); kept as a cheap Phase 4 smoke check, not a defect.
- **C6 ACCEPTED** - the Tauri command `list_render_devices` and `render_only::list_render_devices` collided by name; the command is now `list_outputs` (ipc.ts, contract tests, notes) and the `main.rs` wording follows the template's `<package>_lib::run()`.
- **C7 ACCEPTED** - docs.rs confirms rubato 5.0.1 re-exports `audioadapter_buffers`, so the direct dependency (and a possible version split) is dropped; `tauri-plugin-single-instance` is pinned to 2.5.5 (crates.io max_stable).
- **R1 ACCEPTED** - an unbounded idle pad after sleep/resume or a long stall was real in DRAFT v1; all synthesis is capped at 500 ms, bigger holes reopen the stream, idle pads are clamped and credited, tests T5 (d, h) and gate M10 were added; a power-event subscription is left out of Phase 2 until M10 shows a need.
- **R2a ACCEPTED** - giving up after ~5.7 s is the worst outcome mid-interview; after the fast phase the supervisor enters `Waiting` and retries the same target kind every 2 s with a persistent typed banner, `Failed` is reserved for non-retryable errors, and rule R2 still holds (never an input device, never an automatic device change).
- **R2b REJECTED** - the premise that a call makes Windows disable the "Stereo" endpoint and expose a different "Hands-Free" one is Windows 10 behaviour; Learn says Windows 11 unifies the endpoints (same id, changed format), so the "headset call mode changes the output device" hint is not added and M5 / M11 expect an unchanged endpoint id.
- **R3 ACCEPTED** - recommending SAC Off without the one-way warning was wrong; Learn says Off/On is one-way and 2026 support wording conflicts (a January 2026 KB claim was withdrawn in February), so Plan B comes first, Plan A needs your consent and a documented block, and the unsupported "updates turn it back on" sentence is deleted (this box: build 26200.9457, policy state 1).
- **R4 ACCEPTED** - one "Hello" says little at 1-in-10 fresh-exe blocks; Step 0 now builds a crate with build scripts and proc-macros, links five fresh exes, builds a scratch Vite/Tailwind/shadcn app, reads the CodeIntegrity log and offers a Mark-of-the-Web-free rustup bootstrap.
- **R5 ACCEPTED** - the scan covered only `src/`; it now covers the whole `src-tauri` tree with identifier allow-lists, bans glob imports and raw COM enumeration, asserts through `cargo tree` that no second audio stack exists, and is described as a lint while the runtime checks are the guarantee.
- **R6 ACCEPTED** - silently switching a missing pinned device to FollowDefault is a fallback under CLAUDE.md rule 2; the pinned device now stays selected with a typed error and an explicit "Use system default" button, and the Os16kMono path is called a format retry on the same endpoint.
- **R7 ACCEPTED (modified)** - worker at BELOW_NORMAL (IDLE while loading), child opted out of Low QoS, `cpu_threads` configurable with an initial default of 1 until a real-call measurement, tiny.en fallback on measured slowness; 1-thread latency is not measured (the benchmark would modify files in the research scratch directory, which this review step may not do), so the Phase 3 gate measures 1 vs 2 threads under a real call.
- **R8 ACCEPTED** - cold start, connection pre-warm, a configurable `T_end` with a "listening" state, and the dependence of the 0.7 s fast path on a "?" are added to the budget; 5-6 s is labelled a target to measure.
- **R9 ACCEPTED** - `engine.rs` did depend on the pipeline and the S3 proof on the meter; the `PipelineFactory` / `PipelineHandle` traits move into S2, S3 uses a Meter-only factory and depends on P1, S4 depends on S3.
- **R10 ACCEPTED** - `play-tone.ps1` is the deterministic source, M2 is judged from log timestamps with a 500 ms bound, M5 records the Realtek plug-in behaviour as data, M8 runs covered / on battery, and M4 gained a tone-residual check that actually detects inserted or deleted blocks.
- **R11 ACCEPTED (modified)** - `set_ui_visible` drops Level to 2 Hz while hidden and rAF paints only on new frames; full-occlusion detection is unverified, so it is an optimization; the process power-throttling opt-out is covered with W9.
- **R12 ACCEPTED** - 3.10 fixes the dump directory, retention, log rotation (`RotationStrategy::KeepSome` and `max_file_size` confirmed on docs.rs for tauri-plugin-log 2.10.1), no autostart and reload behaviour; the CSP / style issue is handled under C4.
- **R13 ACCEPTED (partly)** - `sink.rs` / `FrameSink`, the 30 s history, `FrameAssembler`, the activity peek and process loopback are marked optional / post-gate and the Phase 2 core is defined by what T1-T11 and M1-M10 need; the trait seam itself stays specified so Phase 3 can start without a contract change.


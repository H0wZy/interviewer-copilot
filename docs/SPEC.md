# Interviewer Copilot: Specification (Spec-Driven Development)

> **This is the master spec.** It says *what* the app must do, *how we know it works* (acceptance criteria and gates), *in which order* to build it, and *where the project stands*.
> The *how* (detailed design, code skeletons, evidence) lives in [`ARCHITECTURE.md`](ARCHITECTURE.md); the original intent lives in [`BRIEF.md`](BRIEF.md); measurements and research live in [`findings/`](findings/README.md).
> When the three disagree: this SPEC wins on scope, status and acceptance; ARCHITECTURE wins on technical detail, and its section 3.9 is normative for the exact thresholds of the Phase 2 gate (section 7.1 only summarises it; changing a threshold needs a decision-log entry); BRIEF is the historical source. Changes go through the decision log (section 8) first, then the code.
>
> **Version:** 1.0, 2026-11-01. **Owner/approver:** Marcos (GitHub `H0wZy`). **Repo:** `https://github.com/H0wZy/interviewer-copilot` (public).

## 0. Status at a glance

| Item | Status (2026-11-01) | Next |
|---|---|---|
| Phase 1: environment and audio diagnostics | **Done on the laptop** (loopback proven, see [`findings/phase1-diagnostics-laptop.md`](findings/phase1-diagnostics-laptop.md)) | **Repeat on h0wzy-pc** (Phase 0, section 6.0): different hardware, endpoints and Smart App Control state |
| Architecture v2 ([`ARCHITECTURE.md`](ARCHITECTURE.md)) | Researched by 4 agents, attacked by 3 adversarial reviewers (32 findings), revised. **Proposal, not yet approved** | Marcos approves or asks for changes (decision P-1) |
| Phase 2: Tauri shell + WASAPI loopback engine + device selector + level meter | Not started. **No application code exists yet** | Starts after P-1 and P-2 are answered (P-3 to P-5 have defaults, section 9), then S0 |
| Phase 3: local speech-to-text (faster-whisper) | Not started; designed as a seam | Only after the Phase 2 gate is signed off |
| Phase 4: Claude answers (streaming) + full UI | Not started; designed as a seam | Only after the Phase 3 gate |
| Phase 5: polish and packaging | Not planned in detail | After Phase 4 |

**The very next actions** (for the agent that opens this repo on h0wzy-pc):
1. Read `CLAUDE.md`, this file, then `ARCHITECTURE.md` sections 0-3 and 7-9. Talk to Marcos in Portuguese (pt-BR).
2. Run Phase 0 (section 6.0): environment report and audio diagnostics on h0wzy-pc. Read-only; installs nothing.
3. Present the report and ask Marcos the pending decisions P-1 to P-5 (section 9). Do not install or scaffold anything before his answers.
4. Then Phase 2 step S0 (toolchain + Smart App Control check), S1, and onward (section 6.2).

## 1. Product

### 1.1 Problem and vision
During a live technical interview over a video call, the candidate (Marcos) wants a discreet desktop helper that **listens to the interviewer through the computer's audio output**, transcribes the questions locally, and streams a structured, honest suggested answer that fits his real profile, with short study notes in Portuguese.

### 1.2 Usage scenario
1. Marcos joins a browser video call (Meet / Teams web / an assessment platform). The interviewer's voice plays on the **laptop speakers** or a **Bluetooth headset**.
2. He opens the app and presses **Start**. The app captures what Windows is *playing* (loopback), never a microphone.
3. Each interviewer question appears as a transcript; when the question ends, an answer card streams in: QUESTION / INTENT / SUGGESTED ANSWER (English) / KEY CONCEPTS / LEARNING NOTES (PT-BR).
4. He can force an answer (Enter / hotkey) or type a question manually.
The same flow serves mock-interview practice (e.g. playing a recorded interview).

### 1.3 In scope
- Windows 11 x64 desktop app: Tauri 2 (Rust) + React/TypeScript/Vite/Tailwind v4/shadcn/lucide.
- Audio from **render (output) endpoints only**, via WASAPI shared-mode loopback.
- Local STT (faster-whisper, English `.en` models), local VAD and question detection.
- Anthropic Messages API with token streaming; key stored in Windows Credential Manager.

### 1.4 Out of scope (non-goals)
- Any microphone / capture-endpoint audio, ever (including "Stereo Mix").
- Hiding the window from screen sharing, screen capture or proctoring tools.
- Languages other than English for the interview audio (decided D-008).
- Cloud STT by default (audio never leaves the machine; a cloud STT could only ever be an explicit opt-in).
- macOS / Linux / mobile.

### 1.5 Hard rules (non-negotiable, from the brief)
- **R1 Loopback only.** Audio comes exclusively from a render endpoint via `AUDCLNT_STREAMFLAGS_LOOPBACK`. Never open a capture endpoint.
- **R2 No fallback.** If loopback fails or the output device is unavailable, show an explicit typed error. Never switch to an input device, never switch device silently. No mock audio either (the brief's 'NO MOCK/FALLBACK TO MIC'): `mockIpc` is a dev-only, build-time switch for UI work, never a runtime fallback when the engine fails, and never counts as gate evidence.
- **R3 Phase gates.** Phase 2 must be proven with real desktop audio before any Whisper (Phase 3) or Anthropic (Phase 4) code. Every phase ends with a verification gate and Marcos's written confirmation.

### 1.6 Candidate persona (input to Phase 4 prompts; from the brief)
Marcos, Brazil. Technologist in Systems Analysis & Development (graduation June 2026). Trainee in 2026 (employer and dates kept out of this public repo, D-015): Scrum, APIs, collaborative development. Stack: C#, .NET, ASP.NET Core, TypeScript, React, Next.js, Python, SQL Server, PostgreSQL, Docker, Git.
**Constraint:** never fabricate seniority or production ownership. Answers are conversational, technically accurate, concise. The persona text is editable in the app (decision default for P-5); Marcos enters the full version there, and it is never committed.

## 2. Machines and environment

### 2.1 The laptop (where Phase 1 ran; performance floor)
ASUS Vivobook Go 15, AMD Athlon Silver (Zen 2, **2 cores / 2 threads**), **7.3 GB RAM with ~0.3-0.7 GB free**, Radeon iGPU (no CUDA), ~30 GB free disk, Windows 11 build 26200, **Smart App Control ON** (it blocked 1 of 10 freshly compiled unsigned test exes). Audio: "Speakers (Realtek(R) Audio)", 48 kHz / 2 ch / float32, 10 ms period. Full report: [`findings/phase1-diagnostics-laptop.md`](findings/phase1-diagnostics-laptop.md).

### 2.2 h0wzy-pc (development box from now on)
AMD Ryzen 7 5800X (8 cores / 16 threads), 32 GB RAM, NVIDIA RTX 3050 8 GB VRAM, SSD 1 TB + SSD 500 GB + HDD 1 TB. **Not measured yet.** Unknown until Phase 0: Windows edition/build, Smart App Control state, audio endpoints and mix formats, Bluetooth adapter, NVIDIA driver / CUDA version.
Consequences:
- Builds are fast (expect a few minutes for the first Tauri build instead of 15-40 min); **do not** commit a cargo `jobs` cap.
- Keep the clone, `target/` and `node_modules/` on an SSD, not the HDD, and outside OneDrive-synced folders.
- Phase 3 can evaluate faster-whisper on **CUDA** (RTX 3050 8 GB) in addition to CPU; the device and model stay configurable.
- If Smart App Control is Off there, Phase 2 step S0 shrinks to "install and record versions".

### 2.3 Runtime target (which machine runs the app during real interviews)
**Open (P-4).** Default assumption until Marcos answers: **both**. Therefore the laptop stays the performance floor: the capture/DSP pipeline must stay CPU-light (budgets in NFR-3), and Whisper defaults to CPU `base.en` int8. Note: an unsigned exe built on h0wzy-pc may be blocked by Smart App Control on the laptop (see risk K-2).

### 2.4 Toolchain pins (verified 2026-11-01; re-check on install)
| Area | Pin |
|---|---|
| Rust | stable MSVC **1.99.1** via rustup 1.29.1 (`rust-toolchain.toml`); Tauri needs >= 1.90 |
| C++ / SDK | Visual Studio Build Tools 18.11.2: workload components `VC.Tools.x86.x64` + `Windows11SDK.26100` |
| Node | LTS 26.13.0 (Vite 8 needs >= 20.19 / 22.12) |
| Tauri | `tauri` 2.12.5, `tauri-build` 2.7.4, `@tauri-apps/api` / `cli` 2.12.4 |
| Frontend | React ^19.1, TypeScript ~6.0.3 (no `baseUrl`), Vite 8.3.5, `tailwindcss` / `@tailwindcss/vite` 4.3.3, `shadcn` CLI 4.23.0, `lucide-react` 1.66.0 |
| Audio | `wasapi =0.26.0` (exact), `windows` 0.62.2, `rtrb` 0.4.1, `rubato` 5.0.1, `earshot =1.2.2` (Phase 3) |
| Infra crates | `thiserror` 2, `log` 0.4, `tauri-plugin-log` 2.10.1, `tauri-plugin-store` 2.5.0, `tauri-plugin-shell` 2.6.0, `tauri-plugin-single-instance` 2.5.5, `tauri-plugin-global-shortcut` 2.4.1 |
| Phase 4 | `reqwest` 0.13.5 (`native-tls`, `stream`, `system-proxy`, `json`), `keyring-core` 1.0.1 + `windows-native-keyring-store` 1.1.0 |
| Phase 3 (Python) | Python 3.12, `faster-whisper==1.2.1`, `ctranslate2==4.8.4` and the other pins in ARCHITECTURE section 4 |
Install commands: ARCHITECTURE section 7 (written for the laptop; identical on h0wzy-pc except where section 2.2 above says otherwise).

## 3. Functional requirements

Each requirement has acceptance criteria (AC). "Gate" IDs (T*, H*, M*) refer to the Phase 2 gate in section 7.1 and ARCHITECTURE 3.9.

### 3.1 Phase 2: capture engine, device selection, level meter
| ID | Requirement | Acceptance criteria |
|---|---|---|
| FR-C1 | Capture the mix of an **Active render endpoint** with WASAPI shared-mode loopback (event + 20 ms timeout, 1 s buffer, device mix format as-is). | Desktop audio (YouTube, test tone) moves the meter (M2); logs show `flow=Render loopback=true` for every open (M6); H3 passes. |
| FR-C2 | **Never** open a capture endpoint, structurally: `RenderEndpoint` newtype, OS-queried data-flow check before `Activate`, single `Direction::Capture` token, no fallback path, no `getUserMedia` in the webview. | T8 lint green; H4 (a capture endpoint id is refused with `NotARenderEndpoint`, nothing activated); no microphone indicator in the tray while capturing (M6). |
| FR-C3 | List render endpoints only (Active), with friendly names, default markers (console / multimedia / communications), stable id + endpoint id; Refresh; automatic `audio:devices-changed` event on hot-plug. Listing never runs on the capture thread. | H1, M1 (Refresh while capturing does not interrupt the level stream). |
| FR-C4 | Two target modes: **FollowDefault** (default render, eConsole role) and **Pinned** (stable id first, then endpoint id). A pinned device is never swapped automatically; if unavailable, the UI keeps the selection, shows the typed error and offers "Use system default" as an explicit action. | M5; H5; UI shows the banner in mock mode (step P4) and live (M5). |
| FR-C5 | Continuity from **device position only**: zero-fill measured holes up to 500 ms, reopen (`Resync`) on bigger holes, never delete or trim PCM, QPC stamps are metadata only; SILENT packets zeroed; an idle watchdog keeps time if packets stop. | T5 (trace replay: zero pads on a clean stream), T9, M4 (tone residual < -50 dBFS = no clicks), M7, M10. |
| FR-C6 | DSP: meter on raw frames (RMS/peak/hold/clip, 40 ms hop), downmix to mono, resample to 16 kHz (`rubato` FFT), convert to i16; keep a 10 s `PcmHistory`. Output format for STT: **PCM 16 kHz, 16-bit, mono**. | T1-T4, T6, T7, T9. |
| FR-C7 | Stream a `LevelFrame` at 25 Hz over a Tauri `Channel` (2 Hz while the window is hidden); 1 Hz `EngineStats`. | T9 (25 +-2 Hz), M2 (level events at 25 +-2 Hz measured by `seq`). |
| FR-C8 | Recovery: on device loss / default change / format change / resync, reconnect with a fast backoff (6 attempts, ~5.7 s), then **Waiting** (retry the same target kind every 2 s, indefinitely, persistent banner with typed cause, "Use system default" and "Stop"). `Failed` only for terminal errors. Never an input device. | M5, M10, M11; T11 (`classify`). |
| FR-C9 | **Bluetooth headset** support (decided D-009): A2DP playback and call mode (HFP) on the Windows 11 unified endpoint (same endpoint id, mix format changes to 8 or 16 kHz mono). The stream recovers across the profile flip and the UI shows the current format. | **M11 required** (section 7.1). |
| FR-C10 | Typed errors (`AudioError` union mirrored in TypeScript) with user-facing messages and actions. | T10 (golden JSON for every variant), ARCHITECTURE 3.4 table. |
| FR-C11 | Diagnostics: stats panel, "Dump last 10 s WAV" (16 kHz mono 16-bit) with path + copy button, rotating log files. | M4; ARCHITECTURE 3.10. |
| FR-C12 | Lifecycle: capture starts only on **Start** (never autostart); Stop is idempotent; closing the window stops the engine in < 3 s with no leftover process; a UI reload rebinds to the running engine. | M9; S5 done-criteria. |
| FR-C13 | Phase 2 UI: device selector (System default + render devices + Refresh), Start/Stop, level meter (-90..0 dBFS, hold tick, CLIP, "no packets" / "silence" labels), status/banners, stats panel, WAV dump. | M1, M2, S6 done-criteria. |

### 3.2 Phase 3: local speech-to-text and question detection
| ID | Requirement | Acceptance criteria |
|---|---|---|
| FR-T1 | VAD + segmentation in Rust (`earshot` + energy gate): speech start/stop, 320 ms pre-roll, 600 ms hangover, utterances <= 12 s (soft cut at a pause after 8 s). | Recorded test set segments correctly (Phase 3 gate). |
| FR-T2 | STT worker: Python `faster-whisper` 1.2.1 as a child process, stdin binary frames / stdout JSON lines (protocol v1, ARCHITECTURE section 4). Default `base.en` int8 on CPU, beam 1, low priority (BELOW_NORMAL), `cpu_threads` configurable (default 1 until measured). **Device and model configurable**; on h0wzy-pc evaluate CUDA (see D-013). | Protocol self-test; latency p50/p95 measured under a real call; no orphan process after exit or kill. |
| FR-T3 | Post-processing: technical-term fixes ("C sharp" -> C#, "dot net" -> .NET, ...), hallucination filters (no-speech prob, low log-prob, compression ratio, blocklist like "Thank you."). | No hallucinated questions on the silence / music / chime clips. |
| FR-T4 | Turn assembly and question detection: a turn closes after `T_end` (1.4 s; 0.7 s after a "?"), `isQuestion` from punctuation or interrogative/imperative starters; manual "Answer now" override; a later turn supersedes a premature answer. | Unit tests on the pure `TurnAssembler`; gate test set. |
| FR-T5 | Transcript UI: timestamped utterances in the collapsible left sidebar, turn markers, filtered items dimmed; Whisper status in the top bar (loading / ready / busy / crashed + last proc ms). | Phase 3 gate by eye. |
| FR-T6 | Worker lifecycle: spawn on Start, exit 5 min after Stop, watchdog `max(15 s, 4x audio)`, restart backoff 1/2/4/8/15 s, give up after 5 restarts in 60 s; single instance. | Phase 3 gate. |

### 3.3 Phase 4: answers from Claude
| ID | Requirement | Acceptance criteria |
|---|---|---|
| FR-L1 | Trigger automatically on `turnClosed{isQuestion}`, or manually (Enter, global hotkey, manual text input). A new turn aborts the answer in flight. | Phase 4 gate. |
| FR-L2 | Rust-side Anthropic Messages client with SSE streaming (raw HTTP via `reqwest`); model is a **setting**, never hard-coded. Before writing any Phase 4 code, load the `claude-api` skill and re-verify model ids, prices, API version and request rules (the brief's `claude-3-5-sonnet` is retired). | One raw request returns 200 in task L-1; streaming deltas render. |
| FR-L3 | Output contract: five sections in fixed order: **QUESTION**, **INTENT**, **SUGGESTED ANSWER** (English), **KEY CONCEPTS**, **LEARNING NOTES (PT-BR)**; parsed incrementally into section events. | Section parser unit tests; UI shows sections while streaming. |
| FR-L4 | Prompt: persona (1.6) + hard constraint (no fabricated seniority / production ownership) + previous 2 turns + the closed turn (or manual text). Persona editable in the app. | Bake-off answers judged by Marcos. |
| FR-L5 | API key stored only in **Windows Credential Manager** (`keyring-core`); commands `set_api_key`, `has_api_key`, `clear_api_key`; no command returns the key; the key crosses the webview only once, on entry (password field -> `set_api_key`, then cleared from UI state) and is never stored in JS, returned, displayed back, logged or written to source or plain files. Dev-only fallback: env var `ANTHROPIC_API_KEY` set by Marcos in his own shell session, read in Rust under `debug_assertions`, never loaded from a file. | Code review + grep; Phase 4 gate. |
| FR-L6 | Errors: 400/401/403/404/413 not retried; 429/500/529 retried up to 2x **only before the first token** (honour `retry-after`); after that, surface the error and offer Retry. | Unit tests with a mock server. |
| FR-L7 | Cost awareness (Marcos has about US$120 of credit, D-010): record tokens per answer (`message_delta` usage) and show cost per interview; the default model is chosen by a 5-question bake-off (time-to-first-token, answer quality, cost). | Bake-off report in the Phase 4 gate. |
| FR-L8 | Privacy: only text (turns + persona) leaves the machine; a first-run notice says so. Connection pre-warm on Start (no key in the warm-up request). | Phase 4 gate. |

### 3.4 UI (all phases; the brief's "dark minimalist, Raycast/Linear" aesthetic)
| Region | Content | Phase |
|---|---|---|
| Top bar | device status (name, format, follow/pinned, reconnecting / waiting / failed), capture indicator (pulsing dot, mini meter), Whisper status, LLM model selector | 2 / 3 / 4 |
| Left sidebar (collapsible) | live transcript feed with timestamps and turn markers | 3 |
| Main view | active question card, streaming suggested answer, key concepts, PT-BR notes | 4 |
| Footer | device switch + Refresh, Start/Stop, manual text input (fallback / override) | 2 / 3-4 |
| Phase 2 extras | dBFS bar, stats panel, WAV dump | 2 |
Rendering rules: the 25 Hz level handler writes to a ref and `requestAnimationFrame` paints only on new frames; lifecycle state in one `useReducer`; no product logic in the webview (it can be throttled when occluded). Dark mode by default.

## 4. Non-functional requirements
| ID | Requirement | How it is checked |
|---|---|---|
| NFR-1 Privacy | Audio never leaves the machine. WAV dumps live in `app_data_dir()/dumps`, keep the last 5 and delete anything older than 24 h (they contain other people's voices). Logs never contain audio, transcripts at `info`, or keys. `*.wav` is never committed. | Code review, `.gitignore`, gate M4 notes |
| NFR-2 Security | Strict CSP (`default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src ipc: http://ipc.localhost`), capabilities `core:default` only until needed, `Permissions-Policy: microphone=(), camera=()` (best-effort defence in depth, ARCHITECTURE 3.4 layer 5; not a gate item), no media APIs in the webview, key only in Credential Manager. | T8 (`check:nomic`), release-build check in S1 |
| NFR-3 Performance (laptop floor) | Capture thread: no allocation, no locks per packet; DSP < 3 % of one core (`dspBusyPct`); app + WebView2 < 5 % of the laptop's CPU during a call; zero dropped frames in a 5-minute stress run (M8). Latency target from end of question to first answer token: ~3.6 s idle / ~5-6 s loaded on the laptop (to be measured; h0wzy-pc expected faster). | M8, Phase 3 and 4 gates |
| NFR-4 Reliability | No audio ever deleted; holes <= 500 ms zero-filled; larger holes reopen; indefinite Waiting instead of giving up mid-interview; engine panic caught (`catch_unwind`, release profile `panic = "unwind"`) and reported. | T5, M5, M10, M11 |
| NFR-5 Observability | `log` + `tauri-plugin-log` (1 MB x 5 files), target `ic::audio`; 1 Hz stats (packets, silent packets, discontinuities, resyncs, synth/dropped frames, packet age, heartbeat, DSP busy); `audio:warning` events for degraded modes. | Gate evidence |
| NFR-6 Platform | Windows 11 x64 only (build 22000+; process loopback, if added later, needs build 20348+ and is gated by a typed error). | Phase 0 report |
| NFR-7 Build hygiene | Pinned toolchain (`rust-toolchain.toml`), `Cargo.lock` and `package-lock.json` committed, Smart App Control-aware setup (section 6.2 S0), no personal data in the repo. | S0, S1, repo review |
| NFR-8 Quality | `cargo test` (T1-T11 + ignored hardware tests H1-H5), `cargo clippy`, `cargo fmt --check`, `tsc --noEmit`, `npm run build`, `npm run check:nomic`. | Every task's definition of done (section 10) |

## 5. Architecture summary
Full design: [`ARCHITECTURE.md`](ARCHITECTURE.md) (sections in parentheses below).

```
Windows audio engine: mix of ALL apps on one render endpoint
   | WASAPI loopback (shared, event + 20 ms timeout, 1 s buffer, device format)
   v
[ic-wasapi thread, MTA, MMCSS]  read -> zero SILENT/holes (device position) -> push      (3.3)
   | rtrb SPSC ring, 2 s, wait-free
   v
[ic-dsp thread, MMCSS]  meter -> mono -> rubato 48k->16k -> i16 -> PcmHistory -> sink    (3.5)
   | Channel<CaptureEvent> 25 Hz               | Phase 3: Segmenter -> utterances
   v                                           v
React UI (render only)                   ic-stt child process (faster-whisper)            (4)
                                               | TurnClosed{isQuestion}
                                               v
                                         Rust LLM client -> api.anthropic.com (SSE)        (5)
```

| Decision | Choice | Where |
|---|---|---|
| WASAPI binding | `wasapi =0.26.0` (+ `windows` 0.62.2 for QPC/MMCSS/HRESULTs); `cpal` rejected | 0, 3.3 |
| Threads | one MTA thread owns every long-lived COM object; DSP on its own thread; device list on a short-lived worker | 2, 3.2, 3.3 |
| Continuity | device position only; QPC never edits audio | 3.3 |
| No-mic guarantee | type + OS-queried flow check + cached check + lint + logs | 3.4 |
| IPC | `tauri::ipc::Channel` for streams, `emit` for rare broadcasts, serde-tagged unions mirrored in TS | 3.7 |
| Errors | `AudioError` taxonomy, `terminal()` vs retryable, `classify()` of HRESULTs | 3.8 |
| STT | Python faster-whisper child process, stdin/stdout protocol v1, Rust owns VAD and turns | 4 |
| LLM | Rust SSE client, key in Credential Manager | 5 |

Module map (Phase 2): `src-tauri/src/audio/{mod,types,error,render_only,platform,timeline,convert,ring,engine,pipeline,ipc}.rs`, `audio/dsp/{mod,downmix,resample,meter,pcm,wav}.rs`, optional `audio/sink.rs`; Phase 3 adds `audio/vad.rs`, `stt/`; Phase 4 adds `llm/`. Only `audio/ipc.rs` may import `tauri`; only `audio/render_only.rs` may name `Direction`.

Target repo layout:
```
README.md  CLAUDE.md  .gitignore  rust-toolchain.toml (S1)  package.json (S1)
docs/        SPEC.md  ARCHITECTURE.md  BRIEF.md  findings/  phase2-gate.md (S7)
tools/       diagnostics/ (audio-probe.ps1, WasapiProbe.cs; play-tone.ps1 and check_wav.py at S7)
src/         React UI (lib/ipc.ts, features/audio/*, components/*)
src-tauri/   Rust core (src/audio/*, later src/stt/*, src/llm/*), tests/
stt-worker/  Phase 3 Python worker (venv and models live outside the repo)
```

## 6. Plan and tasks

Work proceeds strictly in order; parallel work is only *authoring* of file-disjoint pieces (cargo commands run one at a time). Every task ends with its done-criteria checked and this section's checkbox updated.

**ID legend** (hyphens matter): `P0.n` Phase 0 tasks; `P1`-`P4` Phase 2 parallel steps (6.2); `P-1`-`P-5` pending decisions for Marcos (section 9); `T-1`-`T-8` Phase 3 tasks and `L-1`-`L-6` Phase 4 tasks, as opposed to `T1`-`T11` unit tests, `H1`-`H5` hardware tests and `M1`-`M11` manual gate checks (7.1); `FR-*` / `NFR-*` requirements; `D-nnn` decisions; `K-n` risks; `Q<n>` open questions numbered in ARCHITECTURE section 9 (its `R1`-`R3` are constraints, its `R1`-`R13` review findings).

### 6.0 Phase 0: onboarding h0wzy-pc (read-only, no installs)
- [ ] **P0.1** The repo is cloned (by Marcos) onto an SSD path outside OneDrive; verify it with P0.2 (clone path vs `$env:OneDrive`, `Get-PhysicalDisk`) and confirm `git log` shows the docs commit. If the path is on the HDD or inside OneDrive, ask Marcos to re-clone elsewhere; do not move the folder you are running in.
- [ ] **P0.2** Environment report (read-only, PowerShell): Windows build (`[Environment]::OSVersion` and `Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select DisplayVersion, CurrentBuild, UBR`; do not use `winver`, it only opens a window, and do not paste `systeminfo` output), Smart App Control state (`Get-MpComputerStatus | Select SmartAppControlState` and registry `HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy` `VerifiedAndReputablePolicyState`; on the laptop they read `On` and `1`; treat `Evaluation` like `On`), CPU/RAM (`Get-CimInstance Win32_Processor`; `Get-CimInstance Win32_ComputerSystem`), disks and which drive letter is SSD or HDD (`Get-PhysicalDisk | Select MediaType, BusType, Size`; `Get-Volume`), whether the clone path is under OneDrive (compare `$PWD` with `$env:OneDrive`), GPU + driver + CUDA version (`nvidia-smi`, if present), Bluetooth adapter (`Get-PnpDevice -Class Bluetooth`), WebView2 runtime (registry `HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`, value `pv`), and which tools exist (`Get-Command git, python, node, rustc, cargo, winget`; do not run `python --version` or `winget list`, they can open the Microsoft Store or a winget source-agreement prompt).
- [ ] **P0.3** Audio diagnostics, from the repo root: `powershell -ExecutionPolicy Bypass -File tools/diagnostics/audio-probe.ps1 -Seconds 3` (forward slashes: backslashes break in Git Bash) while audio plays. Ask Marcos (in Portuguese) to start YouTube and keep it playing; you cannot produce desktop audio yourself. Record every render endpoint, defaults, state, mix format, loopback result. If Marcos can pair a Bluetooth headset (A2DP), repeat with it; pairing or changing the default output device is his action (CLAUDE.md rule 4). Note whether the PC has a Bluetooth adapter (`Get-PnpDevice -Class Bluetooth`). If loopback fails or no render endpoint is Active, stop and report; do not work around it.
- [ ] **P0.4** Write `docs/findings/phase1-diagnostics-h0wzy-pc.md` (same structure as the laptop report). Personal data stays out (no product IDs, IPs, e-mails, user names, serial numbers); replace endpoint GUIDs and the Windows user name with placeholders such as `<endpoint-guid-1>`, as the laptop evidence does, and never paste raw `systeminfo` / probe output. Do not commit it until Marcos asks.
- [ ] **P0.5** Present the report to Marcos in Portuguese, with a short pt-BR digest of ARCHITECTURE section 0 (TL;DR) and of the section 6.2 plan, because ARCHITECTURE.md is long and English-only and P-1 must be an informed approval; then ask the pending decisions P-1 to P-5 (section 9) and wait for his answers. Done when the report is written and sanitised, P-1 and P-2 are answered, P-3 to P-5 are answered or Marcos has said to use their defaults, and each answer is a new D-0xx row in section 8 with section 0 and the other status copies updated (CLAUDE.md).

### 6.1 Phase 1 (done on the laptop)
- [x] Environment check, endpoint enumeration (Core Audio), loopback proof with real audio, the brief's GUID bug fixed in `tools/diagnostics/audio-probe.ps1`. Evidence: [`findings/phase1-diagnostics-laptop.md`](findings/phase1-diagnostics-laptop.md).

### 6.2 Phase 2: shell + loopback engine + selector + meter (ARCHITECTURE 3 and 8)
| Step | What | Done when | Depends on |
|---|---|---|---|
| [ ] **S0** | Toolchain install (VS Build Tools C++ x64 + Win11 SDK 26100, rustup + 1.99.1, Node LTS) **after Marcos authorizes**; if Smart App Control is **On or in Evaluation** (treat Evaluation like On; never toggle it): the representative smoke test of ARCHITECTURE 7 step 4 (build scripts + proc-macros, five fresh exes, a scratch Vite/Tailwind/shadcn build, CodeIntegrity log). Any block: stop and ask Marcos; never change Smart App Control without his consent. | versions logged in the h0wzy-pc report (`docs/findings/phase1-diagnostics-h0wzy-pc.md`, section Toolchain) and the pins of section 2.4 re-checked; no block (or Marcos decided) | P-1, P-2, P-3 |
| [ ] **S1** | Scaffold in a scratch dir, copy into the repo without overwriting; configure Vite/TS 6/Tailwind v4/shadcn; Cargo profiles (`panic = "unwind"`, dev opt-levels); `rust-toolchain.toml`; CSP; remove template demo code; extend `.gitignore` | `npm run tauri dev` opens the window; `npm run build` + `cargo check` pass; a **release** build renders a shadcn scroll-area under the CSP | S0 |
| [ ] **S2** | Contract: `audio/{mod,types,error,pipeline}.rs` (wire types, `PipelineFactory`/`PipelineHandle` traits), `src/lib/ipc.ts`, golden tests T10, `classify` test T11 | `cargo check`, `cargo test --lib`, `tsc --noEmit` pass | S1 |
| [ ] **P1** | DSP: `audio/dsp/*`, 10 s `PcmHistory`, `wav.rs`; T1-T4 | `cargo test --lib dsp::` green | S2 |
| [ ] **P2** | Timeline / convert / ring / platform; T5, T6, T7 | green | S2 |
| [ ] **P3** | Render-only layer (stable ids, OS-queried flow check, device-list worker), `tests/no_mic_guard.rs` (T8), hardware tests H1-H5 | T8 green; `cargo test -- --ignored` passes H1-H5 on this PC | S2 |
| [ ] **P4** | Frontend on mock IPC (`ipc.ts`, `mockIpc.ts`, features/audio, components, `scripts/check-nomic.mjs`) | `npm run build`; mock mode shows meter, dropdown, banners (Waiting, unavailable pinned device) | S2 |
| [ ] **S3** | Engine + console proof (`audio/engine.rs`, `examples/loopback_console.rs` with a Meter-only factory) | console meter follows audio; pause -> -90 dBFS; 5 min without panic or drops | S2, P1, P2, P3 |
| [ ] **S4** | Pipeline (`ic-dsp` thread, MMCSS, history, dump) | T9 green | P1, S3 |
| [ ] **S5** | IPC, lifecycle, logging, power-throttling opt-out (`audio/ipc.rs`, `lib.rs`) | list/start/stop from devtools; window close ends the engine < 3 s | S4 |
| [ ] **S6** | Frontend integration (real IPC, banners, visibility reporting, devices-changed) | M1, M2 by eye | S5, P4 |
| [ ] **S7** | Gate run: `tools/diagnostics/{play-tone.ps1,check_wav.py}`, evidence in `docs/phase2-gate.md` | section 7.1 all green + Marcos signs off | S6 |
| [ ] **S8** | Optional, post-gate: activity peek, process loopback ("browser only"), `sink.rs` + `FrameAssembler` + 30 s history | not part of the gate | S7 |

### 6.3 Phase 3: STT (only after the Phase 2 sign-off)
- [ ] **T-1** Python 3.12 (python.org build with the PSF-signed `python.exe`, as in the SAC evidence; install only after Marcos authorizes it, P-2; no install command exists yet, so write it into ARCHITECTURE section 4 first) venv outside the repo (`%LOCALAPPDATA%\interviewer-copilot\stt-venv`), pinned packages; models in `%LOCALAPPDATA%\interviewer-copilot\models`.
- [ ] **T-2** `stt-worker/ic_stt_worker.py` implementing protocol v1 (a research spike exists in [`findings/evidence/stt-worker-spike/`](findings/evidence/stt-worker-spike/README.md) as reference, not as code to paste).
- [ ] **T-3** Rust side: `stt/` process management (BELOW_NORMAL, power-throttling opt-out, job object, watchdog, restart backoff), writer thread, JSON-lines reader.
- [ ] **T-4** `audio/vad.rs` (`earshot` Segmenter as `FrameSink`), `FrameAssembler`, 30 s history.
- [ ] **T-5** Post-processing + `TurnAssembler` (pure, unit-tested).
- [ ] **T-6** UI: transcript sidebar, Whisper status.
- [ ] **T-7** Benchmarks: CPU (`tiny.en` / `base.en` / `small.en`, `cpu_threads` 1 vs 2) on the runtime machine(s); on h0wzy-pc also CUDA (`float16` / `int8_float16`), recording latency, VRAM/RAM and the effect on a concurrent video call.
- [ ] **T-8** Phase 3 gate (section 7.2) + Marcos signs off.

### 6.4 Phase 4: answers (only after the Phase 3 sign-off)
- [ ] **L-1** Load the `claude-api` skill; re-verify model ids, prices, API version header and request rules; smoke-test one raw request.
- [ ] **L-2** `llm/secrets.rs` (Credential Manager) + settings UI for the key (never displayed back).
- [ ] **L-3** `llm/client.rs` (reqwest SSE parser, retries before first token, abort on new turn), `llm/prompt.rs` (persona + constraint + context), `llm/sections.rs` (incremental section parser).
- [ ] **L-4** `Channel<AnswerEvent>`, triggers (auto, Enter, global hotkey, manual input), main-view cards.
- [ ] **L-5** Cost tracking + 5-question model bake-off.
- [ ] **L-6** Phase 4 gate (section 7.3) + Marcos signs off.

### 6.5 Phase 5: polish and packaging (later)
Release build and installer (Tauri bundler), signing decision for any machine with Smart App Control on, settings screen (persona, model, thresholds, hotkey), first-run privacy notice, compact / always-on-top window option, startup performance.

## 7. Verification gates

### 7.1 Phase 2 gate (exact pass criteria: ARCHITECTURE 3.9)
| Group | Checks |
|---|---|
| Automated, no hardware | T1 downmix, T2 `to_i16`, T3 resampler (incl. 8/16 kHz Int16 cases for Bluetooth call mode), T4 meter calibration, T5 Timeline (trace replay, holes, resync, bad stamps, idle credit, QPC overflow), T6 convert, T7 ring, T8 no-mic lint (Rust + TS), T9 pipeline in both idle modes, T10 serde golden + `tsc`, T11 `classify` |
| Hardware (`cargo test -- --ignored`) | H1 device list, H2 resolve default, H3 open loopback 1 s (contiguous device position), H4 capture id refused, H5 stable id |
| Manual (Marcos at the keyboard) | M1 list + Refresh, M2 tone/YouTube latency from logs (<= 500 ms) + 25 Hz, M3 OS meter cross-check, M4 WAV dump (format, 1 kHz tone, residual < -50 dBFS), M5 plug/unplug + pinned behaviour, M6 no capture endpoint opened, M7 60 s idle per endpoint class (speakers **and** Bluetooth headset), M8 5-minute stress with video + webcam + covered window (battery and AC on a laptop), M9 exit < 3 s, M10 sleep/resume 30 s, **M11 Bluetooth headset A2DP -> call mode -> back** |
Pass = all green + Marcos's written confirmation. If the Bluetooth headset is unavailable on gate day, the gate may close for the speakers only with M11 recorded as **pending**, and the headset must not be relied on in a real interview until M11 passes. If the laptop is a runtime target (P-4), M8 must also be run on the laptop before relying on it there.

### 7.2 Phase 3 gate (ARCHITECTURE section 4)
Protocol self-test with a bundled WAV; 20 interviewer-style utterances (clean TTS, compressed call audio, Bluetooth call-mode audio dumped in M11, chimes, music, silence + noise) with acceptable transcripts and no hallucinated questions; p50/p95 latency **under a real browser video call with the webcam on** on each runtime machine, for the chosen device/model/threads, plus the effect on call quality; cold-start time; turn assembly (`isQuestion`, 'Answer now') and the transcript sidebar / Whisper status work (FR-T4, FR-T5); worker lifecycle (exit 5 min after Stop, restart backoff, no orphan Python process after exit or after killing the app, FR-T6).

### 7.3 Phase 4 gate
Raw request smoke test (200); the automatic trigger, Enter, the global hotkey (default binding agreed with Marcos) and manual input each start an answer (FR-L1); first-run privacy notice and key-less connection pre-warm (FR-L8); streamed answer renders the five sections in order; time-to-first-token measured; abort-on-new-turn works; retry policy verified (before / after first token); after entry the key is never visible in the UI, logs, files, `localStorage` / the store or React state (grep + review); cost per interview reported; bake-off result recorded and the default model chosen by Marcos.

## 8. Decision log
| ID | Date | Decision | Why / source |
|---|---|---|---|
| D-001 | 2026-11-01 | Loopback only, no fallback, phase gates (R1-R3) | Brief |
| D-002 | 2026-11-01 | `wasapi =0.26.0` + `windows` 0.62.2; `cpal` rejected | research-wasapi-rust; ARCHITECTURE 0 |
| D-003 | 2026-11-01 | 1 s endpoint buffer, event + 20 ms timeout | Measured: 22 ms buffer lost 38-174 ms per 7.4 s under 2x CPU load; 1 s lost none |
| D-004 | 2026-11-01 | Continuity from device position only; QPC is metadata | Review W1 (blocker): QPC drifts ~70 ppm and bunches up to 9.4 ms; the draft would have edited a clean stream |
| D-005 | 2026-11-01 | FollowDefault (eConsole) + Pinned (stable id); the user's selected target is never changed automatically (FollowDefault re-resolves to the new Windows default and shows it; Pinned never moves) | Brief R2; review R6 |
| D-006 | 2026-11-01 | Indefinite `Waiting` retry instead of giving up | Review R2a |
| D-007 | 2026-11-01 | Smart App Control: keep On first (Plan B); turning it Off only with Marcos's consent after a documented block, treated as permanent | Review R3; Microsoft Learn: Off/On is one-way |
| D-008 | 2026-11-01 | Interviews are English only: Whisper `.en` models | Marcos (Q2) |
| D-009 | 2026-11-01 | Outputs used in interviews: laptop speakers **and** a Bluetooth headset; M11 required | Marcos (Q3) |
| D-010 | 2026-11-01 | Marcos has an Anthropic key and ~US$120 of credit; model chosen by bake-off incl. cost | Marcos (Q7) |
| D-011 | 2026-11-01 | Project renamed **interviewer-copilot**; internal prefix `ic-` | Marcos |
| D-012 | 2026-11-01 | Development continues on **h0wzy-pc**; the laptop stays the performance floor until P-4 is answered | Marcos |
| D-013 | 2026-11-01 | STT device/model configurable; CPU `base.en` int8 default; CUDA evaluated on h0wzy-pc in T-7 (unverified: faster-whisper GPU needs CUDA 12 cuBLAS + cuDNN 9 libraries) | New hardware; to verify in Phase 3 |
| D-014 | 2026-11-01 | Personal files (CV PDF, `systeminfo.md`) never committed; repo is public | Privacy |
| D-015 | 2026-11-01 | Public copies redact the employer, internship dates and the named assessment platform (BRIEF, SPEC, ARCHITECTURE); the full persona lives only in the app settings. Marcos may reverse this | Publishing is one-way; review finding on the CV-derived persona |

## 9. Open questions / pending decisions (owner: Marcos)

Ask these in Portuguese, after the Phase 0 report. P-1 and P-2 block all code and installs until Marcos answers. For P-3 to P-5 a default applies only after Marcos has seen the question and answered or said to use the defaults (silence is not consent). Record each answer as a new D-0xx row in section 8 and update section 0.

| ID | Question | Default if not answered |
|---|---|---|
| P-1 | Approve ARCHITECTURE v2 (or list changes)? | **No code until answered** |
| P-2 | Authorize the installs on h0wzy-pc (VS Build Tools incl. the Microsoft license, rustup, Node LTS; later Python packages and possibly NVIDIA CUDA/cuDNN runtime libraries for Phase 3)? | **No installs until answered** |
| P-3 | Smart App Control on h0wzy-pc: after Phase 0 reports its state, proceed with Plan B if it is On? Optional Defender exclusions for build folders (admin)? | Plan B; no exclusions |
| P-4 | Which machine runs the app during real interviews: laptop, h0wzy-pc, or both? | Both (laptop = performance floor) |
| P-5 | Defaults for the remaining architecture questions: keep `wasapi`'s `initialize_client` behind the three checks (no extra `unsafe`, ARCHITECTURE Q10); keep Phase 2 lean (ARCHITECTURE Q11); browser-only capture after the gate (Q5); persona as editable text (Q6); personal use, unsigned (Q8); gate tests at the keyboard OK (Q4) | The defaults listed |

## 10. How we work (SDD loop)
1. **Spec first.** A task starts from a row in section 6 and its acceptance criteria. If the work reveals that the spec or the architecture is wrong, update them (and the decision log) before or together with the code, never silently.
2. **Plan and implement with teammates.** Orchestrate with the Workflow tool; every teammate is **Sonnet 5.5** (`model: 'sonnet'`), effort `high` for routine work and `max` for the audio engine, the Timeline, the no-mic guarantee, verification and final reviews. Parallelize only file-disjoint authoring; run cargo serially.
3. **Verify adversarially.** Tests first-class; for engine-critical work add an independent reviewer that tries to refute correctness. Never claim a check passed without its output.
4. **Definition of done (per task):** its done-criteria met; `cargo fmt --check`, `cargo clippy`, `cargo test` (incl. relevant `--ignored` hardware tests), `tsc --noEmit`, `npm run build`, `npm run check:nomic` green where they exist; this SPEC's checkbox and status table updated.
5. **Gates.** At the end of a phase, collect evidence in `docs/phase<N>-gate.md` (N = 2, 3, 4), present it to Marcos in Portuguese, and wait for his written confirmation, pasted with its date into that file, before starting the next phase.
6. **Git.** Commit and push only when Marcos asks. Work on one branch per phase step (e.g. `phase-0/h0wzy-pc-report`, `phase-2/s1-scaffold`), merged into `main` only when Marcos says so; never commit personal data, secrets, WAV recordings, models, venvs or build outputs; commit messages end with the co-author line given by the environment.

## 11. Risks (top; full list in ARCHITECTURE section 9)
| ID | Risk | Mitigation |
|---|---|---|
| K-1 | Smart App Control blocks unsigned build outputs (cargo build scripts, proc-macros, npm native binaries, the app exe) on a machine where it is On | Phase 0 reports the state; S0 representative test; Plan B; consent before any change |
| K-2 | Running a build on the laptop: an unsigned exe from h0wzy-pc may be blocked there by Smart App Control | Decide with P-4; options: interviews on h0wzy-pc, code signing, or (with consent) Smart App Control Off on the laptop |
| K-3 | CPU/RAM pressure on the laptop during a call (audio drops, slow Whisper) | 1 s buffer, lean capture thread, MMCSS, power-throttling opt-out, low-priority worker, M8 and Phase 3 real-call measurements |
| K-4 | Bluetooth call mode (HFP) degrades audio to 8/16 kHz mono and may invalidate the stream | Reopen logic, Waiting state, format in the UI, M11, call-mode clips in the STT test set |
| K-5 | Accidental microphone capture | Structural guarantee (FR-C2), lint, runtime checks, M6 |
| K-6 | STT/VAD quality on real call audio | Recorded test set, filters, tunable thresholds, model/device options |
| K-7 | Use policy: many interview processes forbid or monitor AI assistance, and transcribing a call may require the other party's consent | Marcos's responsibility; first-run notice; the app also serves mock-interview practice |

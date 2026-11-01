# Phase 1 diagnostics: what was measured on the laptop

Date of all measurements: 2026-11-01. Audience: the agent that continues on the next machine (h0wzy-pc) and Marcos (owner).
Scope: Phase 1 research only. No application code exists yet. Evidence files are in [`evidence/`](evidence/README.md); links below are relative.

Reading guide: "measured" = a number printed by a probe in `evidence/`; "reported" = a number from the research agents' reports or the orchestrator that has no raw file here; "inferred" = my reading of timestamps or code. Probes are managed .NET (C#), so GC pauses can exist: treat timing numbers as indicative for the Rust engine.

## 1. Machine and OS

| Item | Value |
|---|---|
| Model | ASUS Vivobook Go E1504FA, battery laptop |
| CPU | AMD Athlon Silver (Zen 2, Family 23 Model 160), 2 cores / 2 threads |
| RAM | 7.3 GB total, about 0.3-0.7 GB free during work, heavy paging |
| GPU | AMD Radeon iGPU, no CUDA |
| Disk | 117 GB, about 30 GB free |
| OS | Windows 11 Home Single Language 10.0.26200 (build 26200, pt-BR) |
| Smart App Control | ON (see section 10) |

The machine runs a browser video call at the same time in real use, so every number below competes for 2 cores.

## 2. Toolchain state at the time

| Present | Absent |
|---|---|
| Python 3.12.10, Git 2.56, WebView2 156.0.4314.58, winget 1.29.380, `csc.exe` from .NET Framework 4.x, Windows PowerShell 5.1 | Rust, MSVC Build Tools + Windows SDK, Node.js, CMake, ffmpeg, uv |

Consequence: no Rust code was compiled on this machine. The `wasapi` crate, `windows`, Tauri and the cargo build-time behaviour under Smart App Control are untested. Everything measured here used C# via COM interop or Python.

## 3. Audio endpoints (Core Audio, COM)

Source: [`probe_run1.txt`](evidence/loopback-probes/out/probe_run1.txt) (measured), names from the diagnostics script (reported).

- 11 render endpoints in all states: **1 ACTIVE**, **1 UNPLUGGED**, **9 NOTPRESENT**.
- The ACTIVE one is "Speakers (Realtek(R) Audio)", default for Console, Multimedia and Communications. Master volume 0.620, not muted.
- "Headphones (Realtek(R) Audio)" (3.5 mm jack) is UNPLUGGED.
- NOTPRESENT: an LG TV over HDMI, two USB "Alto-falantes", HDMI/HD Audio, older Realtek entries.
- No Bluetooth endpoints (no headset paired yet).
- Default capture is "Microphone Array (AMD Audio Device)"; "Stereo Mix (Realtek)" is DISABLED. **Neither may ever be opened.** They were only named, never activated.
- The registry `DeviceState` heuristic is wrong for state: the high bits are flags, so Stereo Mix looked "active". The COM state (`IMMDevice::GetState`) is authoritative.
- The probe printed endpoint names as `?` (its property-store read returned nothing; not investigated), so names in this report come from the diagnostics script, not from `Probe.cs`.

## 4. Mix format

Default render endpoint, shared mode (measured, `probe_run1.txt`):

| Item | Value |
|---|---|
| Mix format | 48000 Hz, 2 ch, 32-bit float, WAVE_FORMAT_EXTENSIBLE, channel mask 0x3, blockAlign 8 |
| Device format (PKEY_AudioEngine_DeviceFormat) | 48000 Hz, 2 ch, 16-bit, mask 0x3 |
| Device period | 10.00 ms default, 3.00 ms minimum |

48 kHz to 16 kHz is an exact 3:1. Packets arrive as 480 frames (10 ms).

## 5. Loopback proof (short runs)

`AUDCLNT_STREAMFLAGS_LOOPBACK | EVENTCALLBACK` on the default render endpoint initialized OK in shared mode.

- Buffer request vs granted size (measured): hns 0, 100000, 200000 give 1056 frames (22.0 ms); 500000 gives 2400 (50 ms); 2,000,000 gives 9600 (200 ms); 10,000,000 gives 48000 (1 s).
- Orchestrator runs with real audio playing, 200 ms buffer (9600 frames) (reported, raw output not kept): 2.5 s run at 99 % wall-clock coverage; 1.5 s run at 82 % coverage with 2 discontinuities (source paused or a thread stalled); 1 s run at 99 % (99 packets, 47,520 frames, 1 discontinuity = the normal first packet, first packet after 45 ms, peak -0.1 dBFS, RMS -15.8 dBFS, OS meter 0.970). Other runs: RMS -10 to -16 dBFS.
- Probe runs while desktop audio played (measured, [`probe_run1.txt`](evidence/loopback-probes/out/probe_run1.txt)): event-driven, 22 ms buffer: 99.7-100.2 % coverage per phase, 0 event timeouts, 0 SILENT-flagged packets. Event-driven, 1 s buffer: 99.8-100.7 % coverage, no device-position or QPC jumps. Polling every 10 ms, 1 s buffer: 99.4-102.9 %, six 65-84 ms polling gaps without any device-position jump (no audio lost).
- First packet 15-227 ms after `Start()` (15 ms in the age run, 59-227 ms in the scenario runs) and carries the DATA_DISCONTINUITY flag.
- Caveat: the "idle / render zeros" phase labels in that file describe what the probe itself played; another app was playing (peak 0.985-0.987), so they do not show silence behaviour. Section 7 does.

## 6. Buffer size under CPU stress (22 ms vs 1 s)

Measured, [`probe_stress2.txt`](evidence/loopback-probes/out/probe_stress2.txt) and [`probe_stress3.txt`](evidence/loopback-probes/out/probe_stress3.txt): 4 busy threads on 2 logical CPUs, 8 s per config, first 600 ms excluded (so 7.4 s analysed).

| Config | Discontinuities | Audio lost | Max loop gap |
|---|---|---|---|
| 22 ms, no MMCSS | 5 | ~38 ms | 60.6 ms |
| 22 ms, MMCSS "Audio" | 6 | ~136 ms | 78.8 ms |
| 22 ms, MMCSS "Pro Audio" | 6 | ~174 ms | 111.4 ms |
| 22 ms, MMCSS "Capture" | 4 | ~46 ms | 57.7 ms |
| 200 ms, no MMCSS | 1 | ~99 ms (one 308.6 ms stall) | 308.6 ms |
| 1 s, no MMCSS | 0 | ~0 ms | 142.8 ms |
| 1 s, MMCSS "Pro Audio" | 0 | ~0 ms | 78.2 ms |
| 1 s, no MMCSS (repeat) | 0 | ~0 ms | 147.8 ms |

Findings: the 1 s buffer absorbed stalls up to about 148 ms and lost nothing in three runs; the 22 ms buffer lost 38-174 ms per run and MMCSS did not fix it; the 200 ms buffer lost one 99 ms chunk to a 308 ms stall. Decision recorded in the architecture: 1 s endpoint buffer. Coverage percentages are misleading here (22 ms without MMCSS shows 99.31 %); use the lost-audio column. `probe_stress1.txt` (2 busy threads, startup not excluded) is confounded and should not be quoted. Managed .NET: GC pauses may add stalls.

## 7. Idle behaviour (240 s trace)

Source: [`idle240.txt`](evidence/idle-trace-240s/idle240.txt), `LOOPBACK | EVENTCALLBACK`, 1 s buffer (48000 frames), default Realtek endpoint, all 5 render sessions inactive, endpoint meter and sample peak 0.0000 in every second.

- Packets: 23,854 in 240 s (per-second table sums to 23,853; the 1-packet difference is unexplained). 84 to 101 packets per second. Sizes: 480 frames x 23,709 and 960 frames x 145.
- Wake-ups: 23,122, all signalled, **0 timeouts**.
- **0 SILENT-flagged packets.** The driver kept delivering about 100 packets per second of unflagged zeros while nothing played. Silence must therefore be decided by level, never by the SILENT flag or by packets stopping.
- 1 discontinuity-flagged packet (the first one); 0 timestamp errors.
- Device position: 23,853 contiguous, 0 non-contiguous (first devPos 480).
- QPC drift: packet QPC stamps minus frame-count time drift at **68.5 ppm** (0.0685 ms per s); detrended jitter std 0.148 ms, max deviation 9.427 ms; raw offset grew from 0.000 to 16.610 ms over 240.0 s; 21,141 of 23,854 packets were more than 2 ms off the first packet's extrapolation. QPC stamps are metadata; the device position is the continuity authority.
- Packet age (read time minus stamp): min 0.05, p50 0.92, p90 5.46, p99 45.79, max 169.84 ms. A separate 6 s run ([`probe_age.txt`](evidence/loopback-probes/out/probe_age.txt), 552 packets) gave p50 0.1, p99 2.8, max 12.9 ms; different probe, duration and load, both valid.
- QPC counter: frequency 10,000,000 Hz; a naive u64 `raw * 10_000_000 / freq` overflows above raw 1.845E+12 (about 49.3 h of margin at the moment of the run, 2.0 h after the QPC epoch). Use u128 or split the multiplication.
- Not verified: HDMI, USB and Bluetooth endpoints may stop delivering packets when idle (Chromium and OBS code suggest so). Test per endpoint class.

## 8. OS format conversion (AUTOCONVERTPCM, 16 kHz mono s16)

Measured, [`probe_conv1.txt`](evidence/loopback-probes/out/probe_conv1.txt), [`probe_ext1.txt`](evidence/loopback-probes/out/probe_ext1.txt); also reproduced by `FmtProbe` (reported, output not saved).

- 16 kHz, mono, s16 plus `AUTOCONVERTPCM | SRC_DEFAULT_QUALITY` on the loopback stream: Initialize OK; 401 packets in 4.0 s, 64,129 frames, avg 159.9 frames per packet (expected 160), coverage 100.1 %.
- The same format **without** AUTOCONVERTPCM fails with `0x88890008` (AUDCLNT_E_UNSUPPORTED_FORMAT).
- Channel mask matters: WAVEFORMATEXTENSIBLE with mask 0x0 and plain WAVEFORMATEX gave RMS 0.35190 / peak 0.9893; mask 0x1 and 0x4 gave RMS 0.23437 / peak 0.8836 (about -3.5 dB, a different down-mix matrix). Use mask 0 and normalise downstream. The `wasapi` crate's `WaveFormat::new` with 1 channel and no mask defaults to 0x1.
- Only level and continuity were checked, not spectrum or word-error rate: the quality of the OS resampler for Whisper is untested. `Native` (own resampler) must stay a switchable path.

## 9. Process loopback

Measured, [`probe_proc1.txt`](evidence/loopback-probes/out/probe_proc1.txt): `ActivateAudioInterfaceAsync("VAD\\Process_Loopback")`, supplied f32 48 kHz 2 ch, 480-frame buffer, 5 s per mode. It is loopback of an app's render stream, not a capture device.

| Mode | Packets | Coverage | Peak |
|---|---|---|---|
| INCLUDE own pid (silent) | 500 | 99.8 % | 0.0000 |
| INCLUDE pid tree of the playing app | 493 | 98.6 % (1 event timeout) | 0.7789 |
| EXCLUDE that pid tree | 501 | 100.0 % | 0.0000 (nothing else was playing) |

- Works on build 26200. Silent targets still produce a continuous stream of all-zero, unflagged packets. Device position is always 0, so a "gap" detector based on it reports false gaps (36 lines in that file).
- `wasapi` 0.26.0 maps `include_tree = false` to EXCLUDE (the crate's doc comment says the opposite). `get_buffer_size()` returns nonsense for this client (do not allocate `size * blockalign`), `GetMixFormat` is unsupported. Microsoft's pages disagree on the minimum build (20348 on the enum/struct pages, 20438 on `ActivateAudioInterfaceAsync`); 26200 is above both.
- Post-gate option only (S8 in the architecture).

## 10. Smart App Control evidence

- State: ON. `VerifiedAndReputablePolicyState=1`, Defender `SmartAppControlState` On.
- `hello.exe` (unsigned, compiled locally with `csc`) ran fine ([`sac-test/`](evidence/sac-test/README.md)).
- **1 of 10 freshly compiled unsigned probe executables was blocked** (reported by the wasapi research run): `probe9.exe`, while the other nine ran. CodeIntegrity events 3033, 3077 and 3118 ("Smart App Control Block Details") were logged at 11:37:27. Inferred from file timestamps and sizes: `probe9.exe` (built 11:37, the build containing the `age` mode) was the blocked one, and `probe10.exe` (same size, 11:38) built from the same source ran and produced `probe_age.txt` at 11:38.
- `cargo.exe` was blocked on other machines (rust-lang/rust#160163, reported by research); Microsoft offers no per-app exemption; reputation can change days later. With cargo producing hundreds of unsigned build-script exes and proc-macro DLLs, a block is likely at some point. No Rust build was attempted here.
- Phase 3 spike under SAC (reported, [`stt-worker-spike/`](evidence/stt-worker-spike/README.md)): the PSF-signed venv `python.exe` loaded unsigned `ctranslate2.dll`, `libiomp5md.dll` and other wheels; the locally built unsigned PyInstaller `iw-stt.exe` ran the full protocol; a copy with mark-of-the-web (ZoneId=3) also started. Nothing blocked. Covers the exe only, not every DLL, and only on this PC.
- Turning SAC off is documented as one-way (Windows Settings can re-enable only from Evaluation mode). The plan of record: keep it On, run a representative Step-0 gate (build scripts, proc-macros, several fresh exes, npm native binaries, CodeIntegrity log), stop and report to Marcos on any block. Never change it without his decision.

## 11. STT benchmark on this CPU

faster-whisper 1.2.1, ctranslate2 4.8.4, int8, `cpu_threads=2`, English, no VAD, temperature 0. Synthetic Microsoft "Zira" TTS clips of 3.8 / 9.6 / 17.9 s, median of 3 after warm-up. Full table and method: [`stt-benchmark/README.md`](evidence/stt-benchmark/README.md). Reported by the research agent (raw JSON not kept).

| Model | Beam 1 latency (3.8 / 9.6 / 17.9 s) | RSS after load / peak | Verdict |
|---|---|---|---|
| tiny.en | 0.90 / 1.13 / 1.25 s | 148 / 262 MB | fallback; garbled "ASP.NET" and "PostgreSQL" on noisy speech |
| base.en | 1.86 / 2.05 / 2.42 s | 185 / 363 MB | default on this CPU |
| small.en | 5.40 / 7.42 / 9.48 s | 362 / 759 MB | too slow here (real-time factor about 0.8 at 10 s) |

- Beam 5 added about 0.1-1 s for tiny and base; small.en was noisy (10.3 / 7.8 / 12.5 s).
- Latency is nearly flat in clip length: the encoder always processes a 30 s padded window.
- One core kept busy: base.en 2.7 / 3.3 / 3.6 s. Run-to-run noise about +/-30 %.
- Footprint: venv 289 MB, PyInstaller onedir 238 MB. Cold exe start: ready after 9.0 s; warm venv: 2.5 s.
- Short speech fragments (0.6 s) gave plausible hallucinations; silence and noise gave none. No WER on real call audio was measured.

## 12. The brief's GUID bug

`docs/BRIEF.md` reads endpoint names from the registry with the property `{a45c254e-df1c-4ed4-a1e2-6d63d6a303f2},2`. That GUID is mistyped, so every device printed "Unknown". The correct keys are:

| Property | Key |
|---|---|
| PKEY_Device_DeviceDesc | `{a45c254e-df1c-4efd-8020-67d146a850e0},2` |
| PKEY_Device_FriendlyName | `{a45c254e-df1c-4efd-8020-67d146a850e0},14` |
| Adapter name | `{b3f8fa53-0004-438e-9003-51a46e139bfc},6` |

Fixed in `tools/diagnostics/audio-probe.ps1`. Also: do not trust the registry `DeviceState` DWORD as a plain enum (see section 3).

## 13. What must be re-measured on a different PC (h0wzy-pc)

Target: Ryzen 7 5800X, 32 GB RAM, RTX 3050 8 GB, SSD 1 TB + SSD 500 GB + HDD 1 TB. Nothing in this report transfers as a number.

| Area | Re-measure | Why it will differ |
|---|---|---|
| Endpoints | Run `tools\diagnostics\audio-probe.ps1` and `Probe.cs devices` | Different drivers; endpoint IDs, names, default roles, HDMI/USB/Bluetooth classes unknown. The Realtek "keeps delivering zeros" behaviour (section 7) may not hold. |
| Mix format | Section 4 table | May be 44.1 kHz or 24-bit, other channel counts; Bluetooth HFP is 8 or 16 kHz mono. The engine must not assume 48 kHz stereo f32. |
| Loopback proof | Default scenario run and `stress2` | The 1 s buffer decision rests on a 2-core machine under load; check it holds (and whether a smaller buffer is acceptable) rather than assuming. |
| Idle trace | `idle-trace-240s/run.ps1 -Seconds 240` per endpoint class | The packets-while-idle behaviour is driver specific. |
| AUTOCONVERTPCM | `conv` and `ext` modes | Resampler and mask behaviour are driver/OS-build dependent. |
| Smart App Control | State, CodeIntegrity log, representative Step-0 gate | It may be Off, Evaluation or On. |
| Toolchain and build times | Install Rust, MSVC, Node; time a first and an incremental Tauri build | The laptop estimate was 15-40 min for the first build (estimate, never measured). |
| STT on CPU | `bench.py` for tiny, base, small, plus larger models | More cores and RAM; a larger default model may become viable, but nothing was measured. |
| STT on CUDA | Install the CUDA build of CTranslate2, `device="cuda"`, `compute_type` float16 / int8_float16, VRAM use on 8 GB | No CUDA existed on the laptop; zero measurements. Note the "no CUDA" rule E3 in the architecture is laptop-specific. |
| Memory and CPU budget | Whisper plus a browser call plus webcam | The 0.3-0.7 GB free-RAM constraint is laptop-specific. |
| Power and QoS | Plugged desktop, no battery (if it is a desktop) | The battery part of the Low-QoS risk (E9) would not apply; the occluded-window part still does. |

Order of work on the new PC: run the read-only probes first, save the outputs beside these ones under a new folder, then diff the numbers before changing any design decision. The rules in `CLAUDE.md` (loopback only, no fallback to an input device, phase gates) do not change with the hardware.

## Source notes and disagreements

1. Default capture device name: resolved. The registry lists "Microphone Array (AMD Audio Device)" (read-only check of names, 2026-11-01), and `docs/ARCHITECTURE.md` E6 was corrected from "Microphone (AMD Audio Device)". The device is never opened either way.
2. Stress run length: the probe headers say 8 s per config with the first 600 ms excluded; ARCHITECTURE and the research report say "7.4 s". Same thing (8 - 0.6). `probe_stress1.txt` is about 5.6 s and does not exclude startup.
3. Lost audio for 200 ms buffer: file says ~99 ms, research summary says ~100 ms (rounded).
4. Coverage range for event-driven runs: the research summary says 99.8-100.2 % over 3 runs; the raw file spans 99.7-100.7 % across phases (the 100.7 % value is a 0.6 s phase).
5. Packet age: 240 s trace (p50 0.92, p99 45.79 ms) vs 6 s `age` run (p50 0.1, p99 2.8 ms): different probes, durations and load.
6. Total packets 23,854 vs per-second table sum 23,853 and 23,853 contiguous device positions: one packet unexplained (probably counted outside the per-second loop).
7. 16 kHz mono buffer: 356 frames (`conv`, buffer 0) vs 16,000 frames (`FmtProbe`, hns 10,000,000). Different requested buffer, no conflict.
8. PyInstaller output: 238 MB (research report) vs 246 MB seen later with `du` of `dist/` which also contains the 8.9 MB MOTW copy of the exe (consistent, not verified).
9. Process-loopback minimum build: 20348 vs 20438 in Microsoft's own pages.
10. Several orchestrator numbers in section 5 (2.5 s / 1.5 s / 1 s runs) have no raw file in `evidence/`; they are reported.
11. Raw outputs of `FmtProbe`, `bench.py` and `halluc.py` were not saved; their results are reported by the research agents.

## Evidence index

- [`evidence/README.md`](evidence/README.md): index and re-run guide.
- [`evidence/loopback-probes/`](evidence/loopback-probes/README.md): sections 3-6, 8, 9.
- [`evidence/idle-trace-240s/`](evidence/idle-trace-240s/README.md): sections 7, 8.
- [`evidence/stt-benchmark/`](evidence/stt-benchmark/README.md): section 11.
- [`evidence/stt-worker-spike/`](evidence/stt-worker-spike/README.md): sections 10, 11.
- [`evidence/sac-test/`](evidence/sac-test/README.md): section 10.
- Capture-endpoint check: no source file in `evidence/` activates a capture endpoint (details per folder).

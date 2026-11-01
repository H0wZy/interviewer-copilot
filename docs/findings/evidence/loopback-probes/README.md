# loopback-probes

Single-file C# WASAPI probe and its raw measurement outputs. Written and run on the laptop on 2026-11-01 between about 11:17 and 11:39 local time.

## Files

| Path | What |
|---|---|
| `Probe.cs` | Final version of the probe (about 1,000 lines, C# 5, COM interop to Core Audio, no external references). `Main` has `[MTAThread]` and dispatches on `args[0]`. |
| `patches/patch_*.py` | The Python scripts that grew `Probe.cs` step by step (conv, ext, stress, stress2, stress3, age; this is also the order of their timestamps). They are history, kept because they show what each variant measures. They patch `Probe.cs` in place and assert on anchor strings, so they cannot be re-applied to the final file. The first base version of `Probe.cs` was not preserved. |
| `out/probe_*.txt` | Raw outputs. Decimal comma is pt-BR (`0,9865` = 0.9865). Some have a UTF-8 BOM. |
| (not copied) | `probe.exe` ... `probe10.exe`, the compiled builds. Binaries are never committed. |

## Command

The original compile and run lines were not recorded. Reconstructed from the source header (C# 5, .NET Framework csc):

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /platform:x64 /out:probe.exe Probe.cs
probe.exe                        # default scenario run
probe.exe devices                # list render endpoints and default roles
probe.exe sessions               # audio sessions on the default render endpoint
probe.exe idle [maxWaitSec]      # wait for a truly idle endpoint, then record loopback
probe.exe age [seconds]          # packet age, 1 s buffer, event driven    (default 6)
probe.exe stress [seconds]       # 2 busy threads                           (default 6)
probe.exe stress2 [s] [threads]  # 4 busy threads, first 600 ms excluded    (defaults 8, 4)
probe.exe stress3 [s] [threads]  # MMCSS task comparison                    (defaults 8, 4)
probe.exe conv                   # AUTOCONVERTPCM 16 kHz mono s16 vs the mix format
probe.exe ext                    # same, WAVEFORMATEXTENSIBLE with channel masks 0x0/0x1/0x4
probe.exe proc                   # process loopback (VAD\Process_Loopback)
```

Which output came from which invocation is inferred from the header line of each file and the default arguments (the invocation lines were not saved):

| Output | Mode (inferred) |
|---|---|
| `probe_run1.txt` | no arguments |
| `probe_idle1.txt`, `probe_idle2.txt` | `idle 45`, `idle 1200` (headers show maxWait 45 s and 1200 s) |
| `probe_stress1.txt`, `probe_stress2.txt`, `probe_stress3.txt` | `stress`, `stress2`, `stress3` |
| `probe_age.txt` | `age 6` |
| `probe_conv1.txt`, `probe_ext1.txt` | `conv`, `ext` |
| `probe_proc1.txt` | `proc` |

Side effects to know before running: the default scenario run opens a render client on the default output and writes silence plus a 440 Hz sine at amplitude 0.01 (very quiet) for a few seconds. Every other mode only reads. No mode changes volume.

## Capture-endpoint check

Result: no violation. Searched in `Probe.cs` and all six patches:

- Every `EnumAudioEndpoints` call passes data flow `0` (eRender). Every `GetDefaultAudioEndpoint` call passes `0` as the data flow; the `1` and `2` in `GetDefaultAudioEndpoint(0, 1, ...)` and `(0, 2, ...)` are the role argument (eMultimedia, eCommunications), not the flow.
- `AssertRender(IMMDevice)` calls `IMMEndpoint.GetDataFlow` and throws if the flow is not eRender. It runs on the device before every `Activate(IAudioClient)` path (all modes, plus the default scenario at line 996).
- `devices` mode enumerates render endpoints only and activates nothing.
- The only "Capture" identifiers are `IAudioCaptureClient` (the WASAPI interface that reads packets from a loopback stream, obtained by `GetService` on an `IAudioClient` initialized with `AUDCLNT_STREAMFLAGS_LOOPBACK`) and the class name `CaptureRun`. They do not open a capture device.
- Process loopback (`proc`) uses `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ...)` with `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`. That is loopback of an app's render stream, not a microphone.
- No match for `eCapture` as code, `DEVINTERFACE_AUDIO_CAPTURE`, `microphone` or `Stereo Mix`. Two source comments contain the word eCapture and say it is never used.

## What was measured

### Endpoints and formats (`probe_run1.txt`)
- 11 render endpoints in all states: 1 ACTIVE (0x1, default for Console, Multimedia and Communications), 9 NOTPRESENT (0x4), 1 UNPLUGGED (0x8). The probe printed every name as `'?'` (its property-store name read returned nothing; not investigated). Names come from the diagnostics script instead.
- PKEY_AudioEngine_DeviceFormat: 48000 Hz, 2 ch, 16-bit, mask 0x3. Mix format: 48000 Hz, 2 ch, 32-bit float, WAVE_FORMAT_EXTENSIBLE, mask 0x3, blockAlign 8. Default period 10.00 ms, minimum 3.00 ms. Master volume 0.620, not muted.
- Buffer table (`LOOPBACK | EVENTCALLBACK`, shared): hnsBufferDuration 0, 100000 (10 ms) and 200000 (20 ms) all give 1056 frames (22.0 ms). 500000 gives 2400 frames (50.0 ms), 2,000,000 gives 9600 (200.0 ms), 10,000,000 gives 48000 (1000.0 ms).

### Loopback while desktop audio played (`probe_run1.txt`, three scenario runs)
- Event-driven, buffer 0 (1056 frames): per-phase coverage 99.7-100.2 %, about 400 event signals per 4 s, 0 timeouts, 0 SILENT-flagged packets. One startup gap at 231 ms (device position jump 8544 frames = 178 ms, flags=1), which is the first packet.
- Event-driven, buffer 1 s: coverage 99.8-100.7 %, no device-position or QPC jumps at all.
- Polling every 10 ms, buffer 1 s: coverage 99.4-102.9 %; six wall-clock gaps of 65-84 ms (polling stalls) with no device-position or QPC jump, so no audio was lost.
- First packet arrived +227 ms, +59 ms and +67 ms after run start in the three runs.
- Caveat: the phase labels "idle", "render zeros", "render stream closed" describe what the probe itself rendered. Another app was playing audio the whole time (`peakNonSilentData` 0.985-0.987, OS meter readings 0.63-0.99), so these phases do NOT show silence behaviour. The 240 s trace in `../idle-trace-240s/` does.

### Buffer size under CPU stress (`probe_stress1/2/3.txt`)
`Lost` is the audio lost, measured from QPC jumps on discontinuity packets (`lostByQpc`). Coverage alone is misleading (22 ms without MMCSS shows 99.31 % yet lost about 38 ms).

| Config (stress2/3: 4 busy threads, 8 s, first 600 ms excluded) | Coverage | Discontinuities | Lost | Max loop gap |
|---|---|---|---|---|
| 22 ms, no MMCSS | 99.31 % | 5 | ~38 ms | 60.6 ms |
| 22 ms, MMCSS "Audio" | 98.30 % | 6 | ~136 ms | 78.8 ms |
| 22 ms, MMCSS "Pro Audio" | 97.73 % | 6 | ~174 ms | 111.4 ms |
| 22 ms, MMCSS "Capture" | 99.59 % | 4 | ~46 ms | 57.7 ms |
| 200 ms, no MMCSS | 98.81 % | 1 | ~99 ms (one 308.6 ms stall) | 308.6 ms |
| 1 s, no MMCSS | 100.15 % | 0 | ~0 ms | 142.8 ms |
| 1 s, MMCSS "Pro Audio" | 100.13 % | 0 | ~0 ms | 78.2 ms |
| 1 s, no MMCSS (repeat) | 100.17 % | 0 | ~0 ms | 147.8 ms |

`probe_stress1.txt` (2 busy threads, about 5.6 s per config, startup NOT excluded) is confounded: even "no load, 22 ms" shows 3 discontinuities, the startup one among them. Max loop gaps there: 17.6 ms (no load), 33.8 ms (22 ms, load), 51.7 and 54.8 ms (1 s, load). Use stress2/3 for conclusions: a 1 s endpoint buffer lost nothing in any run, and MMCSS did not rescue the 22 ms buffer.

Caveat: managed .NET probe, so GC pauses may add to the stalls. Indicative for Rust, not exact.

### Packet age (`probe_age.txt`, 1 s buffer, event driven, 552 packets)
Age of packet start (read time minus packet QPC stamp): min 0.1, p50 0.1, p99 2.8, max 12.9 ms. Age of packet end: p50 -9.9, p99 -7.2, max 2.9 ms (negative because the stamp is the packet start and the packet spans the next 10 ms). First packet 15 ms after `Start()`, flags=1, devPos=480.

This disagrees with the 240 s trace (p50 0.92, p99 45.79, max 169.84 ms): different probe, different run length (6 s vs 240 s) and different machine load. Both are valid for their own conditions; the long run catches rare scheduling spikes.

### OS format conversion (`probe_conv1.txt`, `probe_ext1.txt`)
- Control (mix format f32/48k/2ch): 401 packets in 4.0 s, 192,480 frames, coverage 100.1 %.
- 16 kHz, mono, s16 plus `AUTOCONVERTPCM | SRC_DEFAULT_QUALITY`: Initialize OK, buffer 356 frames, 401 packets, 64,129 frames (avg 159.9 per packet, expected 160 per 10 ms), coverage 100.1 %.
- Same format WITHOUT AUTOCONVERTPCM: `0x88890008` (AUDCLNT_E_UNSUPPORTED_FORMAT).
- Channel mask, same run and same music: plain WAVEFORMATEX and EXTENSIBLE mask 0x0 gave RMS 0.35190, peak 0.9893; mask 0x1 and 0x4 gave RMS 0.23437, peak 0.8836, about -3.5 dB. All four: 402 packets, 64,320 frames. RMS values are comparable only inside one output file (`probe_conv1` shows RMS 0.32750 for a different moment of audio).

### Process loopback (`probe_proc1.txt`, 5 s per mode, supplied f32/48k/2ch, 480-frame buffer)

| Mode | Packets | Frames | Coverage | Peak |
|---|---|---|---|---|
| INCLUDE own pid (silent process) | 500 | 240,000 | 99.8 % | 0.0000 |
| INCLUDE the pid tree of the app that was playing | 493 | 236,640 | 98.6 % (1 event timeout) | 0.7789 |
| EXCLUDE that pid tree (system mix minus that app) | 501 | 240,480 | 100.0 % | 0.0000 |

Silent targets still deliver a continuous stream of all-zero packets with flags=0 (not flagged SILENT). Device position is always 0 in this mode, so the 36 "gap" lines (`devPosJump=-480`) in that file are an artifact of a detector comparing a position that never advances; QPC is contiguous (`qpcJump=0.0ms`). Ignore those lines.

### Idle wait (`probe_idle1.txt`, `probe_idle2.txt`)
The endpoint never went quiet: 45 s (413 polls) and 1200 s (9734 polls) both ended with `idleReached=False`, max OS meter 0.9874 and 0.9868 (something was playing). That is why the dedicated 240 s trace was taken with all sessions inactive.

## Caveats
- Managed .NET code, one loop per config, a browser and other apps open, 2 cores.
- Desktop audio content varied between runs.
- The endpoint here is a Realtek driver. HDMI, USB and Bluetooth endpoints were not present and are untested.

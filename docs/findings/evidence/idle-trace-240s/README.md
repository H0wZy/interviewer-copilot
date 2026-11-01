# idle-trace-240s

Two small probes written by a review agent to check claims in the first architecture draft, plus the saved output of the 240 s run. Run on the laptop on 2026-11-01 (files dated 12:31-12:43 local).

| File | What |
|---|---|
| `LoopProbe.cs` | Opens ONE shared-mode `LOOPBACK | EVENTCALLBACK` stream on the default render endpoint (eRender/eConsole hard-coded), records every packet for N seconds and prints per-second statistics, packet age, device-position continuity, QPC-vs-frame-count drift, session list, and a simulation of a draft timeline algorithm. Plays nothing, changes no volume. C# 5. |
| `FmtProbe.cs` | Tries six formats on the default render endpoint with `LOOPBACK | EVENTCALLBACK` (with and without `AUTOCONVERTPCM`) for 1 s each: 16 kHz mono s16 as EXTENSIBLE mask 0x0 / 0x1 / 0x4, as plain WAVEFORMATEX, 16 kHz mono without autoconvert, and 48 kHz stereo s16 without autoconvert. Reads only. |
| `run.ps1`, `runfmt.ps1` | Runners. They compile the `.cs` in memory with `Add-Type` under Windows PowerShell 5.1, so no `.exe` is produced (and nothing for Smart App Control to judge). |
| `idle240.txt` | Output of the 240 s run. |

## Command

```
powershell -ExecutionPolicy Bypass -File run.ps1 -Seconds 240 -Hns 10000000 -UseEvent 1 > idle240.txt
powershell -ExecutionPolicy Bypass -File runfmt.ps1
```

`run.ps1` defaults are `-Seconds 12 -Hns 10000000 -UseEvent 1`; the saved output proves 240 s, hns 10,000,000 and event mode. The exact line used was not recorded. Before running, silence every audio app and check the session list printed at the top (all `state=0`). The output of `runfmt.ps1` was not saved; its results are known only from the review agent's report (below).

## Capture-endpoint check

Result: no violation. In `LoopProbe.cs` and `FmtProbe.cs` the only `GetDefaultAudioEndpoint` call passes data flow `0` with the comment "eRender ONLY"; `EnumAudioEndpoints` is declared in the interface but never called. The only "Capture" identifier is `IAudioCaptureClient`, the packet reader obtained from a loopback `IAudioClient`. `run.ps1` and `runfmt.ps1` only compile and call those classes. No microphone, "Stereo Mix" or eCapture reference exists.

## What the 240 s trace measured (`idle240.txt`)

Setup: default render endpoint (Realtek), mix 48000 Hz, 2 ch, 32-bit float, extensible, blockAlign 8; default period 10.00 ms, minimum 3.00 ms; 5 render sessions on the endpoint (system sounds plus 4 apps) all `state=0` (inactive); `Initialize(SHARED, LOOPBACK|EVENTCALLBACK, hns=10000000)` returned OK, `GetBufferSize` = 48000 frames = 1000.0 ms. Endpoint meter and sample peak were 0.0000 in every one of the 240 seconds.

| Measurement | Value |
|---|---|
| Wake-ups | 23,122, all signalled, 0 timed out |
| Packets | 23,854 total (the per-second table sums to 23,853; the one-packet difference is unexplained) |
| Packets per second | 84 to 101 (about 99.4 on average); wake-ups per second fell as low as 53, so several packets are sometimes read per wake (the 145 packets of 960 frames fit that) |
| Packet sizes | 480 frames x 23,709 and 960 frames x 145 |
| SILENT-flagged packets | 0 |
| Discontinuity-flagged packets | 1 (second 0, the first packet) |
| Timestamp-error packets | 0 |
| Device-position continuity | contiguous 23,853, non-contiguous 0; first devPos 480 |
| Packet age (read time minus stamp) | min 0.05, p50 0.92, p90 5.46, p99 45.79, max 169.84 ms |
| QPC stamp minus frame-count time | slope 68.5 ppm (0.0685 ms per s); detrended jitter std 0.148 ms, max deviation 9.427 ms |
| Packets whose raw offset vs the first packet exceeds a 2 ms slack | 21,141 of 23,854; raw offset first 0.000 ms, last 16.610 ms over 240.0 s |
| Draft timeline simulation (anchor Start, slack 2 ms, idle margin 50 ms) | 11 pad events (2476 frames = 51.6 ms, largest 12.71 ms), 3 drop events (1047 frames = 21.8 ms), 0 idle-pad events; emitted 11,520,949 frames vs 11,520,512 wall-clock frames (delta 9.1 ms) |
| QPC | frequency 10,000,000 Hz; raw counter 70,355,259,178 at start (2.0 h); a naive `raw * 10_000_000 / freq` in u64 overflows above raw 1.845E+12, i.e. about 49.3 h left at that moment |

Conclusion drawn in the architecture: on this Realtek endpoint, event-driven loopback keeps delivering about 100 packets per second of unflagged zeros while nothing plays, so "silence" must be decided by level, not by absence of packets or by the SILENT flag. Device position, not QPC stamps, is the continuity authority. Not verified for HDMI, USB or Bluetooth endpoints.

## FmtProbe results (from the review agent's report, output file not saved)

- 16 kHz mono s16 with `AUTOCONVERTPCM | SRC_DEFAULT_QUALITY`: initializes with channel mask 0x0, 0x1 and 0x4 and with plain WAVEFORMATEX; buffer 16,000 frames (hns 10,000,000 here), packets of about 160 frames.
- Same without AUTOCONVERTPCM: fails with `0x88890008`.
- 48 kHz stereo s16 without AUTOCONVERTPCM initializes.
- Number note: `../loopback-probes/out/probe_conv1.txt` reports a 356-frame buffer for the same 16 kHz mono format. No conflict: that run requested buffer 0 (engine default), this one requested 1 s.

## Caveats
- Managed .NET; indicative for Rust.
- One endpoint, one driver, one 240 s window. App names in the session list are as seen on that day.
- The draft timeline simulation is the reviewer's model of an algorithm that was later changed (see `docs/ARCHITECTURE.md` 3.3, where the capture thread no longer edits PCM to chase QPC stamps).

# Evidence index

First-party artifacts from the Phase 1 research on the laptop (ASUS Vivobook Go E1504FA, Athlon Silver, 2 cores, 7.3 GB RAM, Windows 11 build 26200), measured on 2026-11-01. They back the numbers in [`../phase1-diagnostics-laptop.md`](../phase1-diagnostics-laptop.md) and in `docs/ARCHITECTURE.md` (sections 0, 1 E4/E5/E8, 3.3, 4, 7).

None of this is application code. The probes are throwaway C# / Python used to learn how Windows audio and faster-whisper behave on this hardware. Nothing here may be integrated before the Phase 2 gate passes (see `CLAUDE.md`).

| Folder | What it is | Why it exists | Re-run on another PC? |
|---|---|---|---|
| [`loopback-probes/`](loopback-probes/README.md) | `Probe.cs` (single-file C# COM probe, many modes), the patch scripts that grew it, and 10 raw outputs | Loopback proof, buffer size under CPU stress, packet age, 16 kHz mono OS conversion, process loopback | Yes, needs only `csc.exe` from .NET Framework 4.x (ships with Windows) |
| [`idle-trace-240s/`](idle-trace-240s/README.md) | `LoopProbe.cs` (240 s loopback trace with all render sessions idle), `FmtProbe.cs` (AUTOCONVERTPCM format matrix), runner scripts, `idle240.txt` | Answers "what does loopback deliver when nothing plays" (architecture E8) and QPC vs device-position drift | Yes, Windows PowerShell 5.1 only (`Add-Type`, no exe is produced) |
| [`stt-benchmark/`](stt-benchmark/README.md) | `bench.py`, `halluc.py`, `imp.py` plus the results table | faster-whisper latency / RAM / hallucination numbers on a 2-core CPU | Yes, needs Python 3.12 + faster-whisper; the test clips are NOT in the repo (`*.wav` is gitignored) |
| [`stt-worker-spike/`](stt-worker-spike/README.md) | Phase 3 STT worker prototype, test client, PyInstaller spec | Proved the stdin/stdout framing protocol and that an unsigned PyInstaller exe runs under Smart App Control | Yes, but it is a spike, not app code |
| [`sac-test/`](sac-test/README.md) | One-line `hello.cs` | First Smart App Control smoke test | Yes, trivial |

## Rules every artifact was checked against

1. The project never opens a capture endpoint (microphone, "Stereo Mix", any eCapture device). Every source file was searched for `eCapture`, `EDataFlow`, `GetDefaultAudioEndpoint(1,...)`, `EnumAudioEndpoints(1,...)`, `DEVINTERFACE_AUDIO_CAPTURE`, `Direction::Capture`, `microphone`, `Stereo Mix`, `getUserMedia` and Python audio-input libraries. The per-file result is in each folder README. Summary: no violation found.
2. Sanitized for a public repo: the Windows user name, the scratch path, and the per-machine audio endpoint GUIDs were replaced by placeholders (`<scratch>`, `%USERPROFILE%`, `<endpoint-guid-N>`). No e-mail, IP, host name, product ID, serial number or owner name was present in the files.
3. Measurements are copied verbatim. Raw outputs use the pt-BR decimal comma (`0,9865` means 0.9865). The managed-.NET probes (GC pauses possible) are indicative for Rust, not exact.

## Re-running on h0wzy-pc (Ryzen 7 5800X, 32 GB, RTX 3050 8 GB)

Do the probes first, in this order, and keep the new outputs next to these ones (e.g. `evidence-h0wzy-pc/`), never over them:

1. `powershell -ExecutionPolicy Bypass -File tools\diagnostics\audio-probe.ps1` (read-only endpoint list).
2. Compile and run `loopback-probes/Probe.cs` modes `devices`, then the default scenario run, `stress2`, `conv`.
3. `idle-trace-240s/run.ps1 -Seconds 240` with every audio app silent (check the session list at the top of the output: all `state=0`).
4. `stt-benchmark/bench.py` for the models you care about, once on CPU and, for CUDA, after installing the CUDA build of CTranslate2.
5. Check Smart App Control state and the CodeIntegrity log (see [`sac-test/README.md`](sac-test/README.md)).

Section 13 of the report lists what must be re-measured and why.

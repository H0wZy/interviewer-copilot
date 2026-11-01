# stt-benchmark

faster-whisper CPU benchmark scripts and the results they produced on the laptop (2026-11-01, files dated 11:26-11:40). The audio clips are NOT in the repo (`*.wav` is gitignored by policy).

| File | What |
|---|---|
| `bench.py` | Loads a model (int8, CPU), warms up once, then times `transcribe()` on three clips at beam 1 and beam 5 (median and min of 3), and records load time and RSS. Prints JSON. |
| `halluc.py` | Feeds silence, white noise at -45 and -30 dB, a 0.6 s cut-off speech fragment and speech with -20 dB noise, once with default settings and once with strict thresholds. Then compares no bias, `hotwords` and `initial_prompt` for technical vocabulary. |
| `imp.py` | Times `import faster_whisper`, model load (offline) and the first inference (cold) for one model. |

## Command

```
python -m venv venv && venv\Scripts\pip install faster-whisper==1.2.1 psutil
python bench.py   <model> <cpu_threads> <dir containing short.wav mid.wav long.wav>    # e.g. base.en 2 .
python halluc.py  <model> <dir>                                                         # e.g. base.en .
python imp.py     <model> <model_cache_dir>                                             # needs the model already cached
```

Models are downloaded into `<dir>\models` by `bench.py` (needs network on first run). Pins reported by the research run: faster-whisper 1.2.1, ctranslate2 4.8.4, av 19.0.2, onnxruntime 1.31.1, tokenizers 0.23.3, huggingface_hub 2.4.0, numpy 2.5.4, Python 3.12.10 (psutil version not recorded). `compute_type=int8`, `language=en`, `vad_filter=False`, `temperature=0.0`, `condition_on_previous_text=False`, `without_timestamps=True`.

## How the clips were produced

Synthetic speech from the Microsoft "Zira" text-to-speech voice, 16 kHz, 16-bit, mono WAV. Durations 3.8 s (short), 9.6 s (mid), 17.9 s (long), consistent with the original file sizes (123,086 / 305,646 / 572,046 bytes). The generation script and the text were not preserved. Real interviews will differ (accents, codec artifacts, overlap); no WER was measured.

## Capture-endpoint check

Result: no violation, and no audio device access at all. The scripts read WAV files and call faster-whisper; no `sounddevice`, `pyaudio`, `soundcard`, WASAPI or microphone reference exists. They were not re-run for this documentation.

## Results (idle machine unless noted; Athlon Silver 10, 2 cores / 2 threads, `cpu_threads=2`)

Median latency of one `transcribe()` call including mel extraction, median of 3 after one warm-up, seconds:

| Model | Beam 1: 3.8 s / 9.6 s / 17.9 s | Beam 5: 3.8 s / 9.6 s / 17.9 s | RSS after load / peak | Warm load | First inference |
|---|---|---|---|---|---|
| tiny.en | 0.90 / 1.13 / 1.25 | 0.94 / 1.26 / 2.22 | 148 / 262 MB | 1.8 s | 0.95 s |
| base.en | 1.86 / 2.05 / 2.42 | 2.21 / 2.78 / 3.06 | 185 / 363 MB | 1.3 s | 1.8 s |
| small.en | 5.40 / 7.42 / 9.48 | 10.3 / 7.8 / 12.5 (noisy, other processes running) | 362 / 759 MB | 3.6 s | 6.1 s |

- `import faster_whisper`: about 0.6-0.9 s. Latency is nearly flat in clip length because the encoder always sees a 30 s padded window (`pad_or_trim` to 3000 frames); only decoding grows.
- With 1 core kept busy by a spin loop, base.en at beam 1 took 2.7 / 3.3 / 3.6 s (vs 1.9 / 2.1 / 2.4 s idle). `cpu_threads=1` was noisy (base.en 2.7-7.5 s, tiny.en 1.2-2.1 s). Run-to-run noise on this box is about +/-30 %.
- Install footprint: venv with faster-whisper 1.2.1 is 289 MB (including about 13 MB pip); PyInstaller 6.22.3 `--onedir` build is 238 MB.
- Hallucination test (`halluc.py`): silence and noise produced no text in the research report's runs; short speech fragments (0.6 s) produced plausible guesses, so a Rust-side minimum duration and the `no_speech` / `avg_logprob` / `compression_ratio` thresholds (0.6 / -1.0 / 2.4) matter. The actual per-case outputs were not saved.
- Vocabulary bias: on clean TTS audio both `hotwords` and `initial_prompt` produced "ASP.NET Core ... PostgreSQL"; "C sharp" came out as "C-sharp" (never "C#"), so post-process it. tiny.en garbled "ASP.NET" and "PostgreSQL" on noisy speech.
- Whisper models other than `.en` and CUDA were not tested.

## Number notes

- The architecture TL;DR says "~2 s per utterance" for base.en: consistent with 1.86-2.42 s at beam 1.
- `bench.py` stores `beam1_short(3.8s)` style keys, so the clip lengths in the table are the ones printed by the script, not nominal values.
- The raw JSON printed by `bench.py` was not saved; the table comes from the research agent's report.

## Caveats
- Synthetic clean speech, English only, no WER.
- Other processes were running during parts of the small.en beam 5 run.
- The numbers are for this 2-core CPU. The Ryzen 7 5800X / RTX 3050 machine has never been measured; re-measure on CPU and on CUDA before choosing a default model.

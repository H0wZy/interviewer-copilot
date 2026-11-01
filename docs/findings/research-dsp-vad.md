# Research: DSP chain, metering, ring buffer and VAD

> Produced on 2026-11-01 by the `loopback-architecture` research workflow (Sonnet 5.5 teammates) while the project was still called *interview-whisper*, on the 2-core laptop. Historical input: where it differs from [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) (written after the adversarial review), the architecture wins. Absolute paths were replaced with `<scratch>`, `<repo>` or `%USERPROFILE%`.

**Question investigated:** Real-time DSP after WASAPI loopback capture (Rust, 2-core Zen 2): downmix, 48k->16k resampling, f32->i16, level metering, SPSC handoff, VAD/segmentation, CPU budget

## Recommendation

Pipeline: capture thread pushes raw interleaved f32 packets (device channel count, device rate) into an rtrb 0.4.1 SPSC ring (push_entire_slice, all-or-nothing so frames stay aligned; on Full count a dropped-frames atomic, never block). A single DSP thread drains it and does, in order: (a) level meter on the raw multi-channel frames (RMS of all channels, max |sample|, dBFS, floor -90, 40 ms sample-count hop = 25 Hz, sent via a tauri::ipc::Channel); (b) downmix to mono (stereo = 0.5*(L+R); multichannel = normalized fold-down, LFE dropped); (c) resample to 16 kHz with rubato 5.0.1 Fft&lt;f32>, 1 channel, FixedSync::Both, chunk hint 768 (at 48 kHz this gives exactly 768 in / 256 out frames, so one resampler output chunk equals one earshot frame; the same code path handles 44.1/32/96 kHz and skips the resampler when the mix rate is already 16 kHz); (d) f32 -> i16 with round + saturating cast, no dither; (e) VAD/segmentation. Use rubato Fft rather than a hand-rolled FIR only for generality across mix formats (Bluetooth HFP/A2DP and USB devices can expose 16k/24k/44.1k/96k); the CPU difference is irrelevant (my measured full chain with a 120-tap 3:1 Kaiser FIR was ~0.4% of one core in scalar C#). Keep the 120-tap Kaiser polyphase FIR (beta 7.857, fc 8 kHz at 48 kHz: passband ripple +/-0.001 dB to 7 kHz, 9-24 kHz stopband &lt;= -75.6 dB, 1.2 ms group delay) as a documented fallback. Do NOT use dasp (its Sinc is an interpolator with no anti-alias low-pass, so it aliases when decimating) and avoid libsamplerate bindings (C build, last releases 2021/2022). CRITICAL design point: WASAPI loopback delivers NO packets while nothing is playing (NAudio docs), so a DSP loop driven only by packet arrival would freeze the meter at its last value and the VAD would never see the silence that ends an utterance; the DSP thread must compare frames received vs. elapsed monotonic time and inject zero-filled frames after a ~200 ms grace period (and zero-fill any packet flagged AUDCLNT_BUFFERFLAGS_SILENT, whose data must be ignored). For VAD use earshot 1.2.2 (pure Rust, no C compiler, no DLL, ~95 KiB binary, ~8 KiB state, native 16 kHz/256-sample frames, returns a 0..1 score) behind a cheap energy gate, with my own hysteresis state machine because the published 1.2.2 has no segmenter: speech start after 3 consecutive frames with smoothed score >= 0.6 and frame RMS >= -50 dBFS (stay-in-speech threshold 0.5), pre-roll 320 ms, segment end after 600 ms below threshold, turn end (question boundary) after 1400 ms, min voiced 300 ms, soft cut at a >=250 ms pause after 10 s, hard cut at 15 s. Reject webrtc-vad (C build via cc, unmaintained since 2019, GMM-based, worse on tonal sounds) and Silero via voice_activity_detector/ort (pinned to old ort rc.10, onnxruntime runtime ~8 MB extra plus ORT working set on a machine with 0.3-0.7 GB free RAM). Total DSP+VAD budget is well under 1% of one core, far below Whisper and the browser call.

## Versions checked

| Name | Version | Checked at |
|---|---|---|
| rubato | 5.0.1 (published 2026-10-01; MSRV rustc 1.87; default feature fft_resampler pulls realfft ^3.5 / num-complex; master Cargo.toml also 5.0.1) | https://crates.io/api/v1/crates/rubato/5.0.1 |
| audioadapter-buffers | 5.2.0 (rubato 5.0.1 requires ^5.1) | https://crates.io/api/v1/crates/audioadapter-buffers |
| audioadapter | 5.0.0 | https://crates.io/api/v1/crates/audioadapter |
| realfft | 3.5.1 | https://crates.io/api/v1/crates/realfft |
| rtrb | 0.4.1 (2026-10-25; MIT OR Apache-2.0; no dependencies; MSRV 1.38; default feature std) | https://crates.io/api/v1/crates/rtrb/0.4.1 |
| ringbuf | 0.6.0 (published 2026-10-30, two days before this research; deps crossbeam-utils) | https://crates.io/api/v1/crates/ringbuf/0.6.0 |
| crossbeam-channel | 0.5.17 (2026-09-05; MSRV 1.60; MPMC) | https://crates.io/api/v1/crates/crossbeam-channel/0.5.17 |
| earshot | 1.2.2 (2026-08-19; MSRV 1.87; MIT OR Apache-2.0; no runtime deps, optional libm) | https://crates.io/api/v1/crates/earshot/1.2.2 |
| webrtc-vad | 0.4.0 (2019-10-01; build-dependency cc ^1.0.45; GitHub repo last pushed 2020-07-16) | https://crates.io/api/v1/crates/webrtc-vad/0.4.0 |
| voice_activity_detector | 0.2.1 (2025-08-04; ort =2.0.0-rc.10 pinned exactly; Silero VAD v5 model bundled) | https://crates.io/api/v1/crates/voice_activity_detector/0.2.1 |
| ort | 2.0.0-rc.15 (2026-10-19, still a release candidate) | https://crates.io/api/v1/crates/ort/versions |
| dasp | 0.11.0 (2020-05-29) | https://crates.io/api/v1/crates/dasp |
| samplerate (libsamplerate binding) | 0.2.4 (2021-01-14) over libsamplerate-sys 0.1.12 (2022-05-10) | https://crates.io/api/v1/crates/samplerate |
| tauri | 2.12.5 (2026-10-21) | https://crates.io/api/v1/crates/tauri |
| faster-whisper (PyPI) | 1.2.1 | https://pypi.org/pypi/faster-whisper/json |
| Silero VAD (upstream) | v6.2.3 (2026-09-23); the Rust crate bundles the older v5 model | https://api.github.com/repos/snakers4/silero-vad/releases/latest |

## Verified facts

- rubato 5.x current API: traits Resampler&lt;T> (process_into_buffer(&dyn Adapter&lt;T>, &mut dyn AdapterMut&lt;T>, Option&lt;&Indexing>) -> ResampleResult&lt;(usize,usize)> returning (frames_read, frames_written); input_frames_next/max, output_frames_next/max, output_delay, reset), Adjustable, Resizable. Types: Fft&lt;T> (synchronous, FFT, FixedSync::{Input,Output,Both}), Async&lt;T> (Async::new_sinc(ratio, max_rel_ratio, &SincInterpolationParameters, chunk, channels, FixedAsync) and Async::new_poly(ratio, max_rel_ratio, PolynomialDegree, chunk, channels, FixedAsync)), Slip. Buffers come from the audioadapter/audioadapter-buffers crates (e.g. audioadapter_buffers::direct::InterleavedSlice::new(&[T], channels, frames) and ::new_mut(&mut [T], channels, frames)), no longer Vec&lt;Vec&lt;f32>>. Fft is f32 or f64 (Sample implemented for both).
  - source: https://docs.rs/rubato/5.0.1/rubato/ ; https://raw.githubusercontent.com/HEnquist/rubato/master/src/lib.rs ; src/synchro.rs ; src/asynchro.rs ; audioadapter-buffers src/direct.rs
- Fft::new(sample_rate_input: usize, sample_rate_output: usize, chunk_size: usize, nbr_channels: usize, fixed: FixedSync) -> Result&lt;Fft&lt;T>, ResamplerConstructionError>; Fft::new_custom adds sub_chunks and a WindowFunction (default BlackmanHarris2). With FixedSync::Both the FFT block is ceil(chunk_size / (rate_in/gcd)) * (rate_in/gcd) input frames, and output_delay() = fft_size_out/2 output frames. Computed from the source: 48000->16000 with chunk hint 768 gives fft 768 in / 256 out, delay 128 frames (8 ms), anti-alias cutoff ~7.58 kHz; hint 480 gives 480/160, delay 5 ms, cutoff ~7.33 kHz; 44100->16000 with hint 480 gives 882/320, delay 10 ms, cutoff ~7.66 kHz; 32000->16000 gives 480/240.
  - source: https://raw.githubusercontent.com/HEnquist/rubato/master/src/synchro.rs (new, new_custom, calc_chunk_sizes, output_delay) and src/windows.rs (calculate_cutoff); numbers computed by me
- rubato is real-time safe when using process_into_buffer (pre-allocated output, no allocation or blocking); the 'log' feature must stay off in real-time use. README says resampling should NOT be done inside the audio callback; copy into a shared buffer and resample in a separate loop, using an easy chunk size near the API's average packet size. Rubato recommends Fft for fixed ratios and Async::new_poly for the cheapest conversion.
  - source: https://raw.githubusercontent.com/HEnquist/rubato/master/README.md
- rubato changelog: v4.0 added audioadapter 4 (Adapter without lifetime), Indexing::new() fluent setters, Fft::new without sub_chunks, SincInterpolationParameters::f_cutoff became Option&lt;f32>; v5.0.0 moved to audioadapter 5.0 / audioadapter-buffers 5.1 and raised MSRV to rustc 1.87. Tutorials using FftFixedIn/SincFixedIn or Vec&lt;Vec&lt;f32>> are stale (earshot's dev-dependency still uses rubato ^0.16).
  - source: https://docs.rs/rubato/5.0.1/rubato/ (Migrating and Changelog sections)
- dasp's Sinc interpolator is a windowed-sinc (Hann-like window) interpolator evaluated at source-rate spacing with no cutoff scaling, i.e. no anti-aliasing low-pass for downsampling; it is unsuitable for 3:1 decimation. The 'samplerate' crate wraps C libsamplerate via libsamplerate-sys (needs a C build) and has not been updated since 2021.
  - source: https://raw.githubusercontent.com/RustAudio/dasp/master/dasp_interpolate/src/sinc/mod.rs ; https://crates.io/api/v1/crates/samplerate/0.2.4/dependencies
- rtrb: wait-free SPSC ring buffer, fixed capacity allocated once at construction (RingBuffer::new(capacity) -> (Producer&lt;T>, Consumer&lt;T>)); Producer and Consumer are Send but not Sync. For T: Copy: Producer::push_partial_slice(&[T]) -> (&[T], &[T]), Producer::push_entire_slice(&[T]) -> Result&lt;(), ChunkError> (all or nothing), Consumer::pop_partial_slice(&mut [T]) -> (&mut [T], &mut [T]), Consumer::pop_entire_slice; also write_chunk/write_chunk_uninit/read_chunk, Consumer::slots(), Producer::slots(), is_abandoned(). No blocking wait: the reader must poll or be woken externally.
  - source: https://docs.rs/rtrb/0.4.1/rtrb/ ; .../struct.Producer.html ; .../struct.Consumer.html
- ringbuf 0.6.0: 'Lock-free SPSC FIFO ring buffer'; HeapRb::&lt;T>::new(cap).split() -> (producer, consumer) with `use ringbuf::traits::*`; batch ops push_slice/pop_slice; try_push/try_pop. Released 2026-10-30, depends on crossbeam-utils. crossbeam-channel 0.5.17 is MPMC; bounded() preallocates but a per-packet Vec&lt;f32> message would allocate on the capture thread, and it offers blocking recv_timeout (useful for wake-up) at the cost of heavier machinery.
  - source: https://docs.rs/ringbuf/0.6.0/ringbuf/ ; https://crates.io/api/v1/crates/ringbuf/0.6.0 ; https://docs.rs/crossbeam-channel/0.5.17/crossbeam_channel/fn.bounded.html
- earshot 1.2.2 (the crates.io release) exposes Detector (Default, default_boxed, reset) with predict_i16(&[i16]) -> f32 and predict_f32(&[f32]) -> f32; frames must be exactly 256 samples (16 ms) at 16 kHz, f32 in [-1,1]; the score is in [0,1], >0.5 generally voice; returns -1.0 for a wrong length in release builds. State ~8 KiB, binary footprint ~95 KiB (40 KiB network), pure Rust, no_std capable, no onnxruntime. The released crate has NO Segmenter/segments()/Score type: those (SegmenterOptions defaults: smoothing 0.4, attack 0.6, decay 0.5, preroll 5 frames, min_duration 6, start_delay 2, end_delay 4 frames) exist only on GitHub main and the README example using detector.predict(...).is_voice() does not compile against 1.2.2. Vendor claim: RTF 0.0003 and more accurate than Silero v6 / TEN VAD.
  - source: https://crates.io/api/v1/crates/earshot/1.2.2/download (I downloaded and read src/lib.rs) ; https://docs.rs/earshot/1.2.2/earshot/struct.Detector.html ; https://raw.githubusercontent.com/pykeio/earshot/main/README.md and src/segments.rs
- webrtc-vad 0.4.0 compiles libfvad C sources through the cc crate in build.rs (README: 'A C compiler has to be installed'); last release 2019-10-01, repo last push 2020-07-16, 5 open issues. On this project a C compiler (MSVC Build Tools 'Desktop development with C++') is required by Tauri on Windows anyway, so it would build, but it is unmaintained.
  - source: https://raw.githubusercontent.com/kaegi/webrtc-vad/master/build.rs ; README.md ; https://api.github.com/repos/kaegi/webrtc-vad ; https://v2.tauri.app/start/prerequisites/
- voice_activity_detector 0.2.1 wraps the Silero VAD v5 ONNX model through ort pinned to exactly =2.0.0-rc.10 (while ort is now at 2.0.0-rc.15); Silero v5 needs fixed 512-sample windows at 16 kHz (256 at 8 kHz); the ort docs recommend static linking, with load-dynamic needing onnxruntime.dll shipped next to the exe. Earshot's README states ONNX Runtime adds ~8 MB to the binary plus much more memory. Silero upstream claims &lt;1 ms per 30+ ms chunk on one CPU thread (not measured on this CPU).
  - source: https://raw.githubusercontent.com/nkeenan38/voice_activity_detector/master/Cargo.toml and README.md ; https://ort.pyke.io/setup/linking ; https://raw.githubusercontent.com/snakers4/silero-vad/master/README.md
- WASAPI loopback only delivers data while something is rendering: NAudio documents that with no audio playing the DataAvailable event won't fire and suggests playing silence or inserting silence yourself when gaps are detected; SoundCard issue #166 reports GetNextPacketSize always 0 during silence on some sound cards. Microsoft: GetBuffer returns AUDCLNT_S_BUFFER_EMPTY with 0 frames when no packet is available; AUDCLNT_BUFFERFLAGS_SILENT means treat all data in the packet as silence and ignore the actual values; AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY indicates a glitch; the client must read a whole packet or none; GetBuffer/ReleaseBuffer must be paired on one thread; event-driven loopback works from Windows 10 1703.
  - source: https://raw.githubusercontent.com/naudio/NAudio/master/Docs/WasapiLoopbackCapture.md ; https://github.com/bastibe/SoundCard/issues/166 ; https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer ; https://learn.microsoft.com/windows/win32/api/audioclient/ne-audioclient-_audclnt_bufferflags ; https://learn.microsoft.com/windows/win32/coreaudio/loopback-recording
- Rust float->int `as` casts round toward zero, saturate at the integer min/max, and map NaN to 0, so `(x*32768.0).round() as i16` is a safe clamp+NaN-guard in one expression.
  - source: https://doc.rust-lang.org/reference/expressions/operator-expr.html
- faster-whisper 1.2.1 transcribe() accepts a float32 np.ndarray; its own decode_audio resamples to s16 mono 16 kHz and then converts, so 16 kHz mono 16-bit PCM is exactly what its pipeline uses (no dither anywhere). It ships its own Silero-based vad_filter (default True in 1.2.1) with VadOptions defaults threshold 0.5, min_silence_duration_ms 2000, speech_pad_ms 400, min_silence_at_max_speech 98 ms; pass vad_filter=False (or tune) for segments already cut by our VAD.
  - source: https://raw.githubusercontent.com/SYSTRAN/faster-whisper/master/faster_whisper/audio.py ; transcribe.py ; vad.py
- Tauri 2 events are 'not designed for low latency or high throughput', payloads are JSON strings; tauri::ipc::Channel&lt;T> is the documented mechanism for fast ordered streaming (Rust: command takes `on_event: Channel&lt;T>` and calls .send(..); TS: `new Channel&lt;T>()` with .onmessage, passed to invoke). Channels evaluate JavaScript under the hood, so keep payloads small (a 25 Hz level struct is fine).
  - source: https://v2.tauri.app/develop/calling-frontend/
- Measured on this machine (Athlon Silver 3050U-class, scalar C# .NET Framework 4 JIT, synthetic stereo 48 kHz float with 9-11 ms random packet sizes, 60 s of audio per run, 3 runs): the full chain (stereo->mono, 48 kHz peak+sum-of-squares meter with 40 ms hops, 120-tap polyphase FIR 3:1, round/clamp to i16, per-20ms energy accumulation) cost 224/243/236 ms CPU = 0.37-0.41% of ONE core (~0.2% of the 2-core machine). 96 taps: ~0.29%; 72 taps: ~0.18% (30 s runs). Filter quality measured by direct DTFT evaluation: 120 taps/beta 7.857/fc 8 kHz -> passband 0-7 kHz ripple within +/-0.0013 dB, stopband 9-24 kHz max -75.6 dB, -6.0 dB at 8 kHz, -0.71 dB at 7.5 kHz; 96 taps/beta 6.5 -> stopband -56 dB; 72 taps/beta 5 -> -41.8 dB.
  - source: Own benchmark: %USERPROFILE%\AppData\Local\Temp\iw_dsp\bench\bench.cs (compiled with C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe) run on the target machine

## API sketch (research-time, superseded by ARCHITECTURE.md where they differ)

```text
// Cargo.toml (Tauri crate or a dsp workspace crate)
// rtrb = "0.4.1"
// rubato = "5.0.1"                  // default feature fft_resampler -> Fft
// audioadapter-buffers = "5.1"      // same major as rubato's audioadapter 5
// earshot = "=1.2.2"                // pin: GitHub main API differs from the published crate
// [profile.dev.package."*"] opt-level = 3   // otherwise unoptimized rustfft/rubato is slow in debug on 2 cores (not measured)

use rtrb::RingBuffer;
use rubato::{Fft, FixedSync, Resampler};            // Resampler trait must be in scope
use audioadapter_buffers::direct::InterleavedSlice;
use std::sync::atomic::{AtomicU64, Ordering};

// ---------- handoff: capture thread (producer) -> DSP thread (consumer) ----------
// Items are interleaved f32 samples. Capacity = 2 s of audio in the device format. Created once per stream start.
let (mut prod, mut cons) = RingBuffer::<f32>::new(rate as usize * channels * 2);
static DROPPED_FRAMES: AtomicU64 = AtomicU64::new(0);

// capture thread, once per WASAPI packet (no alloc, no lock; whole frames only so channel alignment is kept):
fn on_packet(prod: &mut rtrb::Producer<f32>, samples: &[f32] /*n_frames*channels*/, silent_flag: bool, channels: usize) {
    let r = if silent_flag {
        // AUDCLNT_BUFFERFLAGS_SILENT: data must be ignored -> push zeros (write_chunk yields Default-initialised slots)
        prod.write_chunk(samples.len()).map(|c| c.commit_all())
    } else {
        prod.push_entire_slice(samples)               // all-or-nothing, wait-free
    };
    if r.is_err() { DROPPED_FRAMES.fetch_add((samples.len() / channels) as u64, Ordering::Relaxed); } // overrun: drop packet, never block
    // optionally dsp_thread.unpark() so the DSP loop wakes immediately (std; syscall but no allocation)
}

// ---------- DSP thread ----------
struct Dsp {
    ch: usize, rate: usize,
    scratch: Vec<f32>,            // ch * 1024, pre-allocated, pop target
    stage: Vec<f32>, stage_len: usize,   // mono @ device rate, capacity >= 2*in_chunk + max pop
    rs: Option<Fft<f32>>,         // None when rate == 16000
    out: Vec<f32>,                // capacity = rs.output_frames_max()
    total_in_frames: u64, t0: std::time::Instant,   // for gap detection
}
impl Dsp {
    fn new(rate: usize, ch: usize) -> Self {
        // 48000 -> 16000, hint 768: in=768 (16 ms), out=256 (16 ms = one earshot frame), delay 128 frames (8 ms).
        // 44100 -> 16000: in=882/out=320 (20 ms). Always read input_frames_next()/output_frames_next(); never hard-code.
        let rs = (rate != 16_000).then(|| Fft::<f32>::new(rate, 16_000, 768, 1, FixedSync::Both).expect("resampler"));
        let in_n = rs.as_ref().map_or(256, |r| r.input_frames_next());
        let out_cap = rs.as_ref().map_or(256, |r| r.output_frames_max());
        Dsp { ch, rate, scratch: vec![0.0; ch * 1024], stage: vec![0.0; in_n * 4], stage_len: 0,
              rs, out: vec![0.0; out_cap], total_in_frames: 0, t0: std::time::Instant::now() }
    }

    // called every ~10-20 ms (park_timeout) or when woken
    fn tick(&mut self, cons: &mut rtrb::Consumer<f32>) {
        loop {
            let avail = cons.slots() / self.ch * self.ch;            // whole frames only
            if avail == 0 { break; }
            let n = avail.min(self.scratch.len());
            let (got, _) = cons.pop_partial_slice(&mut self.scratch[..n]);   // returns (filled, unused)
            let frames = got.len() / self.ch;
            // 1) meter on raw multichannel `got` (sumsq over all samples, max |x|)  -> see meter() below
            // 2) downmix straight into self.stage[self.stage_len..]  (stereo: 0.5*(l+r))
            self.stage_len += frames;
            self.total_in_frames += frames as u64;
        }
        // 3) loopback gives NO packets while nothing plays: inject zeros so meter decays and VAD can end utterances
        let expected = (self.t0.elapsed().as_secs_f64() * self.rate as f64) as u64;
        let gap = expected.saturating_sub(self.total_in_frames);
        if gap > (self.rate as u64) / 5 {                            // 200 ms grace
            let fill = (gap - self.rate as u64 / 20) as usize;       // keep 50 ms margin
            // feed `fill` zero frames through the same path (meter, downmix/stage, resample, VAD) in <=1024-frame slices; mark active=false
            self.total_in_frames += fill as u64;
        }
        // 4) resample in exact chunks
        if let Some(rs) = self.rs.as_mut() {
            loop {
                let in_n = rs.input_frames_next();
                if self.stage_len < in_n { break; }
                let inp = InterleavedSlice::new(&self.stage[..in_n], 1, in_n).unwrap();
                let cap = self.out.len();
                let mut outp = InterleavedSlice::new_mut(&mut self.out[..], 1, cap).unwrap();
                let (_read, written) = rs.process_into_buffer(&inp, &mut outp, None).unwrap(); // no allocation
                drop((inp, outp));
                self.stage.copy_within(in_n..self.stage_len, 0); self.stage_len -= in_n;
                // self.out[..written] is mono f32 @16 kHz -> to_i16 -> 256-sample VAD framer / segment ring
                let _ = written;
            }
        }
    }
}

#[inline] fn to_i16(x: f32) -> i16 { (x * 32768.0).round() as i16 }   // `as` saturates, NaN -> 0; no dither

// ---------- meter (sample-count based, independent of packet size) ----------
// hop = rate/25 frames (1920 @48k = 40 ms). Per hop: ms = sumsq/(frames*ch); peak = max|x|
// rms_db  = 10*log10(max(ms, 1e-9))          // floor -90 dBFS (1e-9 power == 3.162e-5 amplitude)
// peak_db = 20*log10(max(peak, 3.1623e-5))
// ballistics: rms: instant attack, release tau 250 ms (a = 1-exp(-0.040/0.25) per hop); peak: instant attack, fall 12 dB/s, hold 800 ms
// payload (serde camelCase, 1 decimal): { seq, tMs, rmsDb, peakDb, holdDb, clip, active }  -> tauri::ipc::Channel<LevelEvent>::send(..) from the DSP thread, 25 Hz, ~110 B

// ---------- VAD (earshot 1.2.2) ----------
let mut det = earshot::Detector::default_boxed();   // Box<Detector>, ~8 KiB state
// per 256-sample i16 frame @16 kHz:  let p = det.predict_i16(&frame);   // f32 in [0,1]
// state machine (own code): Idle -> PendingStart(>=3 frames: smoothed p>=0.6 && frame_rms>=-50 dBFS) -> Speech
//   Speech -> emit SpeechEnd after 600 ms (38 frames) with smoothed p<0.5; emit TurnEnd after 1400 ms; discard if voiced < 300 ms
//   pre-roll ring 320 ms (20 frames, i16); soft cut at >=250 ms pause once segment >10 s; hard cut at 15 s
//   events carry t_ms from the 16 kHz sample counter: SpeechStart{t_ms}, SpeechEnd{start_ms,end_ms,reason: Silence|MaxLen|Stop, pcm: Vec<i16>}, TurnEnd{t_ms}
```

## Alternatives considered

| Option | Verdict |
|---|---|
| Hand-rolled 120-tap Kaiser polyphase FIR decimator for the integer 3:1 case | Valid and measured: 1.92 MMAC/s, whole chain ~0.4% of one core in scalar C#, 1.2 ms group delay, zero dependencies, stopband -75.6 dB. Not chosen as primary because it only covers 48k (44.1k needs a 160/441 rational polyphase, 96k/32k/24k each need new filters) and mix formats vary with Bluetooth/USB devices; keep as fallback or as the 48k fast path if rubato/rustfft compile time on the 2-core box becomes a problem. |
| rubato Async::new_sinc / Async::new_poly | Rejected for this use: asynchronous resamplers exist for drifting clocks and variable ratios, which do not apply (we only stream to STT). new_sinc costs the most CPU; new_poly has no anti-alias filter, so 8-24 kHz content aliases into the speech band when decimating 3:1. |
| rubato Slip | Rejected: not a sample-rate converter (only absorbs ppm clock drift). |
| dasp / dasp_signal Converter with Sinc interpolator | Rejected: the Sinc interpolator has no anti-alias low-pass for downsampling (verified in source); last release 2020. |
| libsamplerate bindings (samplerate 0.2.4 / libsamplerate-sys 0.1.12) | Rejected: C build dependency, unmaintained since 2021/2022, no benefit over rubato Fft. |
| Ask Windows to convert (AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM + SRC_DEFAULT_QUALITY with a 16 kHz mono 16-bit format on the loopback stream) | Not recommended: unverified that it is reliable on this driver, quality of the Windows SRC is undocumented, we lose the proven mix-format path, and the conversion CPU just moves to audiodg. NAudio 2.1.0 docs do say capture at a chosen rate is possible. Revisit only if the DSP thread proves too costly (it will not). |
| ringbuf 0.6.0 as the SPSC queue | Viable (HeapRb split, push_slice/pop_slice) but published 2026-10-30, a fresh API generation with a dependency on crossbeam-utils; rtrb 0.4.1 is smaller, dependency-free, explicitly wait-free, and has exactly the slice APIs needed. |
| crossbeam-channel bounded() between threads | Rejected for the audio path: per-packet owned buffers would allocate on the capture thread (or require a pool), and the channel is heavier than needed. Fine for low-rate control/events (VAD events, meter frames) if desired. |
| webrtc-vad 0.4.0 (libfvad via cc) | Rejected: unmaintained since 2019, GMM-based, weak against tonal non-speech such as notification chimes, C build step (MSVC is present anyway for Tauri, so not a hard blocker). Supports only 10/20/30 ms frames at 8/16/32/48 kHz with 4 aggressiveness modes. |
| Silero VAD via voice_activity_detector 0.2.1 + ort | Rejected for now: crate pins ort =2.0.0-rc.10 (current rc.15), model is Silero v5 (upstream is v6.2.3), onnxruntime adds ~8 MB and extra RAM on a machine with 0.3-0.7 GB free, CPU cost on 2 Zen 2 cores unmeasured (~1 ms/32 ms chunk claimed on desktop CPUs). Keep as the Phase 3 escalation path if earshot gives false positives on real interviewer audio. |
| Adaptive energy VAD with hangover only | Use as the cheap pre-gate and fallback (16 ms RMS frames; noise floor with fast-down/slow-up tracking at ~0.5 dB/s rise; start at floor+12 dB and >= -50 dBFS, stop at floor+8 dB, 600 ms hangover). Alone it cannot reject tonal notification sounds or music, which is the stated false-positive source, so it is not the primary classifier. |
| Dithering the f32->i16 conversion | Rejected: STT gains nothing (quantization noise ~ -98 dBFS RMS for a full-scale sine; speech at -30 dBFS still has ~68 dB SNR), faster-whisper's own decode path quantizes to s16 without dither, and dither would turn digital silence into low-level noise that confuses silence detection and invites Whisper hallucinations. |

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Loopback produces no packets during silence, so a packet-driven DSP loop freezes: the meter never decays and the VAD never sees the trailing silence, so utterances never close and Whisper never receives them. | high | Drive the DSP thread by a ~10-20 ms timer (park_timeout), compare total frames received to elapsed monotonic time x rate, inject zero frames after a ~200 ms grace period, carry an 'active' flag in the meter payload, and zero-fill AUDCLNT_BUFFERFLAGS_SILENT packets. Add a test that plays then stops audio and asserts SpeechEnd fires and the meter falls to -90. |
| Mix format can change under us (headphones plugged in, Bluetooth headset connected, default device change, AUDCLNT_E_DEVICE_INVALIDATED): different rate (16k/24k/44.1k/96k), channel count (mono HFP, 5.1/7.1), or device. A stale resampler/ring would produce wrong-speed audio. | high | Treat (rate, channels, subformat) as part of the stream identity: on any change tear down capture, ring, Fft resampler and VAD state (Detector::reset), and rebuild. Bypass the resampler when rate == 16000. Surface an explicit error if the endpoint is gone; never fall back to a capture/microphone device (project rule 2). |
| Ring overrun when the DSP thread is starved by the browser video call on a 2-core, 0.3-0.7 GB-free-RAM machine. | medium | Ring holds 2 s so the DSP thread can stall hundreds of ms; count dropped frames in an AtomicU64 and show a warning in the UI; raise capture thread priority via MMCSS (AvSetMmThreadCharacteristics 'Audio'); keep DSP work tiny (measured ~0.4% of one core); never block the producer. |
| earshot false positives/negatives on real interviewer audio (TTS or compressed call audio, chimes, hold music) - accuracy figures are vendor-reported only, and the released 1.2.2 has no segmenter (README/main differ from the crate). | medium | Pin earshot =1.2.2 and use predict_i16/predict_f32 only; write our own hysteresis state machine; AND with an energy gate; require >= 300 ms voiced and a voiced-frame ratio in the segment; build a small recorded test set (speech, chime, music, silence with noise) before Phase 3 sign-off; keep Silero via ort as the escalation path. |
| Level meter and VAD thresholds are absolute dBFS, but the system volume/app gain changes loopback level only partly (loopback is taken before the endpoint volume in most drivers, which is not verified here) and call audio can be AGC'd, so thresholds may need adaptation. | medium | Use the adaptive noise-floor energy gate relative to floor+margin with a low absolute minimum (-50 dBFS), expose sensitivity as a setting, and log levels for tuning. Metering is on the unprocessed signal so the UI reflects what Windows plays. |
| Multichannel downmix choices (5.1/7.1 endpoints, one-sided stereo) can attenuate or cancel speech. | low | Stereo: 0.5*(L+R) (mono-in-stereo keeps level, no +6 dB clipping). >2 channels: use the WAVEFORMATEXTENSIBLE channel mask, fold down with centre at +3 dB, surrounds at -3 dB, drop LFE, normalise by the sum of coefficients. Interview calls are almost always stereo or mono so keep this simple and tested. |
| Building rubato (rustfft/realfft) and running debug builds on a 2-core CPU with little free RAM can be slow, and debug-mode DSP can be 10-50x slower than release, hiding or inventing real-time problems. | low | Set [profile.dev.package."*"] opt-level = 3 (or at least for rubato/realfft/rustfft/earshot), rely on release builds for the Phase 2 proof, and keep the hand-rolled FIR as a zero-dependency fallback. |
| Meter/UI traffic: Tauri events are explicitly not designed for high throughput. | low | Use tauri::ipc::Channel&lt;LevelEvent> at a fixed 25 Hz (~110 B JSON per message, ~2.7 kB/s) emitted from the DSP thread, never from the capture thread; the sample-count hop makes the rate independent of packet size. |

## Not verified at research time

- CPU cost of rubato Fft&lt;f32> (rustfft 960-point real FFT + 320-point inverse per 16 ms block) on this Zen 2 CPU: only estimated analytically (~30k flops per block, expected well under 1% of one core); Rust/cargo are not installed so nothing from rubato, rtrb or earshot was compiled or timed. My only measurement is a scalar C# model of the chain (0.37-0.41% of one core).
- CPU cost and real-world accuracy of earshot on this machine, on TTS/call audio, on chimes and on music: only the vendor's RTF 0.0003 / '40x faster and more accurate than Silero v6' claims exist; frame cost for a 1024-point FFT + 40-band mel + small network was not measured here.
- CPU, RAM and DLL/binary size of Silero via voice_activity_detector + ort rc.10/rc.15 on this CPU, and whether ort's download-binaries default links statically on Windows MSVC (the ort linking page quoted did not state Windows defaults); the '~8 MB' figure is earshot's README claim.
- Whether this machine's loopback delivers zero packets or SILENT-flagged packets when nothing is playing (the supplied probe only tested while audio played). NAudio/SoundCard sources say it varies by driver, so the gap-fill design handles both; confirm with a Phase 2 test that plays then stops audio.
- Whether loopback level is captured before or after the endpoint master volume/mute on the Realtek driver (affects absolute VAD thresholds); not tested.
- Windows timer granularity of std::thread::sleep / park_timeout in current Rust (whether it uses high-resolution timers) and whether thread::unpark is free of locks on Windows; assumed acceptable because the 2 s ring tolerates 15 ms scheduling jitter.
- The MMCSS call AvSetMmThreadCharacteristics and the exact channel-mask fold-down coefficients are standard Windows/ITU practice but I did not re-fetch their documentation in this session.
- Rubato 'FixedSync::Both' chunk arithmetic (768 in / 256 out at 48 kHz, 882/320 at 44.1 kHz) and the cutoff values (7.58 / 7.66 kHz) were derived by reading source on GitHub master (version 5.0.1, identical to the published version string) and computing calculate_cutoff by hand, not by running the crate. The listed rubato items are consistent with the docs.rs 5.0.1 text but crate tests were not run.
- The claim that faster-whisper's built-in Silero vad_filter is on by default comes from the master transcribe.py (signature shows vad_filter: bool = True) and PyPI latest 1.2.1; I did not confirm that the 1.2.1 wheel matches master exactly.
- Speech-timing parameters (3-frame start, 600 ms segment end, 1400 ms turn end, 320 ms pre-roll, 300 ms min voiced, 15 s hard cut) are engineering recommendations derived from earshot/Silero/faster-whisper reference defaults, not measured on interviewer audio; they must be tuned on recordings in Phase 3.
- Behaviour of the 44.1 kHz and mono 16 kHz (Bluetooth HFP) paths was only reasoned from rubato's block-size rules; this machine currently exposes only the 48 kHz stereo float mix format.

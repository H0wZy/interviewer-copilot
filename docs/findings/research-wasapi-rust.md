# Research: WASAPI loopback from Rust (crate choice, API, semantics, no-mic guarantee)

> Produced on 2026-11-01 by the `loopback-architecture` research workflow (Sonnet 5.5 teammates) while the project was still called *interview-whisper*, on the 2-core laptop. Historical input: where it differs from [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) (written after the adversarial review), the architecture wins. Absolute paths were replaced with `<scratch>`, `<repo>` or `%USERPROFILE%`.

**Question investigated:** Crate selection and capture-engine design for WASAPI loopback in Rust inside a Tauri 2 app (Windows 11 26200, 2-core Athlon Silver, Realtek speakers)

## Recommendation

Use the `wasapi` crate at an exact pin (=0.26.0) as the only owner of COM/WASAPI, plus `windows` 0.62.2 for four tiny extras (QPC, MMCSS, HRESULT constants). That `windows` is already pulled in by the whole Tauri 2.12.5 stack (tauri, tauri-runtime-wry, wry, tao, webview2-com) and by cpal, so it builds once. The wasapi crate is the only candidate that exposes enumeration with IDs and friendly names, a render-direction check via IMMEndpoint::GetDataFlow, event-driven loopback, per-packet flags and QPC timestamps, an IMMNotificationClient implementation, and process loopback, with ~0 `unsafe` for us to own. cpal is rejected because it hides the loop (INFINITE wait), ignores the SILENT flag, and has no public device notifications or process loopback. Direct `windows` bindings stay as the vendor/fallback path, since upstream is about to churn (0.27.0 breaking, then a move off the `windows` umbrella crate to windows-core 0.100 with MSRV 1.95).

Design: ONE dedicated std thread, MTA, owns DeviceEnumerator + notification registration + the loopback stream. The UI talks to it only over channels, because every wasapi COM wrapper is !Send/!Sync. Capture is shared-mode loopback on an `eRender` ACTIVE endpoint, chosen by ID or by following the eConsole default (browsers use eConsole). It is event-driven with a 20 ms wait timeout, so the event is a hint and never the clock. Use a 1 s buffer, since on this CPU the 22 ms minimum lost 38 to 174 ms of audio per 7.4 s under 2x CPU oversubscription and 1 s lost nothing (MMCSS did not fix that). Measured on this machine, `AUTOCONVERTPCM|SRC_DEFAULT_QUALITY` delivers 16 kHz mono s16 straight from the OS (use channel mask 0, not the crate default of 0x1). Treat SILENT packets as zeros, and keep a QPC-anchored timeline that pads gaps and idle time with silence so VAD and the meter keep a continuous clock.

Recover from AUDCLNT_E_DEVICE_INVALIDATED by tearing down and reopening with backoff, following default-device and format changes (Bluetooth profile flips). Make mic capture structurally impossible: a `RenderEndpoint` newtype constructible only via `resolve()`, which rejects any non-Render or non-Active device, plus a CI test that exactly one `Direction::Capture` token exists in src/. Keep process loopback (verified working here) as an optional "capture browser only" mode, with include_tree=true.

## Versions checked

| Name | Version | Checked at |
|---|---|---|
| wasapi (HEnquist/wasapi-rs) - RECOMMENDED, pin =0.26.0 | 0.26.0 (published 2026-10-11; MIT; MSRV 1.85, edition 2024; deps: windows ^0.62.2, windows-core ^0.62.2, log, num-integer, thiserror; only feature: device-tests) | https://crates.io/api/v1/crates/wasapi/0.26.0/dependencies |
| wasapi upcoming (NOT released as of 2026-11-01) | 0.27.0 pending (breaking: initialize_client gets session_guid, enums by value, Handle Send+Sync); then PR #74 drops the windows umbrella for windows-core 0.100, MSRV 1.95 | https://github.com/HEnquist/wasapi-rs/issues/81 |
| windows (umbrella) | 0.62.2 (2025-10-06) is still the latest on crates.io; MSRV 1.82; features used by us: Win32_Foundation, Win32_Media_Audio, Win32_System_Threading, Win32_System_Performance | https://crates.io/api/v1/crates/windows |
| windows-core / windows-result | windows-core 0.62.2 (used by wasapi, Tauri) with windows-result 0.4.1; focused crates (windows-core, -link, -strings, -bindgen, ...) are at 0.100.0 since 2026-09-03 but `windows`/`windows-sys` 0.100 were NOT published | https://github.com/microsoft/windows-rs/issues/4867 |
| cpal (rejected for the engine) | 0.18.2 (2026-08-16; MSRV 1.85; windows ^0.62; badge actively-developed) | https://crates.io/api/v1/crates/cpal |
| tauri | 2.12.5 (2026-10-21; MSRV 1.90 / edition 2024; windows ^0.62). tauri-runtime-wry 2.12.3, wry 0.57.1, tao 0.37.2, webview2-com 0.39.1 all windows ^0.62 | https://crates.io/api/v1/crates/tauri/2.12.5/dependencies |
| Rust toolchain floor for the app | >= 1.90 (driven by tauri 2.12.5; wasapi 0.26.0 needs only 1.85) | https://crates.io/api/v1/crates/tauri/2.12.5 |
| rubato (only if OS-side AUTOCONVERTPCM is rejected and a Rust resampler is wanted; API NOT reviewed) | 1.0.0 (MSRV 1.74) | https://crates.io/api/v1/crates/rubato/1.0.0 |

## Verified facts

- wasapi 0.26.0 is actively maintained: repo pushed 2026-10-31, 8 releases between 2025-06-20 (0.19) and 2026-10-11 (0.26), 476k downloads, 95 stars, 5 open issues, MIT. All WASAPI COM wrappers are safe Rust; the crate README lists loopback, event-driven and polled modes, device/default-device notifications, and process (application) loopback.
  - source: https://crates.io/api/v1/crates/wasapi ; https://api.github.com/repos/HEnquist/wasapi-rs ; https://github.com/HEnquist/wasapi-rs/releases
- Upstream churn is announced: 0.27.0 is breaking (initialize_client takes session_guid: Option&lt;GUID>; Direction/Role/ShareMode/StreamMode/SampleType passed by value; AudioRenderClient::write_to_device drops nbr_frames; Handle becomes Send+Sync) and the maintainer calls it 'one last release on windows 0.62'. Issue #74 then replaces `windows` with windows-core 0.100 + windows-bindgen output (MSRV 1.95, no windows types in the public API).
  - source: https://github.com/HEnquist/wasapi-rs/issues/81 ; /issues/73 ; /issues/74
- windows-rs 'release 74' (2026-09-03) moved the focused crates to 0.100.0 and rewrote the metadata pipeline, but the umbrella crates `windows` and `windows-sys` were not published; `windows` on crates.io is still 0.62.2.
  - source: https://github.com/microsoft/windows-rs/issues/4867 ; https://crates.io/api/v1/crates/windows
- The entire Tauri 2.12.5 stack (tauri, tauri-runtime-wry 2.12.3, wry 0.57.1, tao 0.37.2, webview2-com 0.39.1) and cpal 0.18.2 and wasapi 0.26.0 all require windows ^0.62 / windows-core ^0.62, so one `windows` 0.62.2 compile serves the whole tree. Tauri 2.12.5 has rust-version 1.90.
  - source: https://crates.io/api/v1/crates/tauri/2.12.5/dependencies ; .../wry/0.57.1/dependencies ; .../wasapi/0.26.0/dependencies ; .../cpal/0.18.2
- wasapi 0.26.0 enumeration/ID API (verified in src/api.rs and on docs.rs): DeviceEnumerator::new()->Result&lt;DeviceEnumerator,WasapiError>; get_device_collection(&self,&Direction) uses EnumAudioEndpoints(dir, DEVICE_STATE_ACTIVE) (ACTIVE only); get_default_device_for_role(&self,&Direction,&Role); get_device(&self,&str) builds the Device via IMMEndpoint::GetDataFlow; DeviceCollection::get_nbr_devices()/get_device_at_index(u32); Device::get_id()/get_friendlyname()/get_state()->DeviceState/get_direction()->Direction/get_iaudioclient()/get_audiometerinformation().
  - source: https://docs.rs/wasapi/0.26.0/wasapi/struct.DeviceEnumerator.html ; crate source src/api.rs L361-722
- AudioClient::initialize_client(&mut self,&WaveFormat,&Direction,&StreamMode) derives flags from (device direction, stream direction, share mode): (Render,Capture,Shared)=>AUDCLNT_STREAMFLAGS_LOOPBACK; (Render,Capture,Exclusive)=>Err(LoopbackWithExclusiveMode); (Capture,Render,_)=>Err(RenderToCaptureDevice); (Capture,Capture,_)=>flags 0, i.e. ORDINARY MICROPHONE CAPTURE. EventsShared{autoconvert,buffer_duration_hns} passes period=0, bufferDuration=buffer_duration_hns, adds EVENTCALLBACK, and autoconvert adds AUTOCONVERTPCM|SRC_DEFAULT_QUALITY.
  - source: wasapi-0.26.0/src/api.rs L1229-1311 (crate tarball https://crates.io/api/v1/crates/wasapi/0.26.0/download)
- All wasapi COM wrapper types (AudioClient, AudioCaptureClient, Device, DeviceEnumerator, DeviceCollection, DeviceEventRegistration, Handle, AudioMeterInformation) are !Send and !Sync in 0.26.0; WaveFormat and DeviceEventCallbacks are Send+Sync. So every WASAPI object must be created and used on one thread.
  - source: https://docs.rs/wasapi/0.26.0/wasapi/struct.AudioClient.html (Auto Trait Implementations) and the other struct pages
- Capture-read API in 0.26.0: AudioCaptureClient::get_next_packet_size()->Result&lt;Option&lt;u32>> (None only in exclusive mode); read_from_device(&self,&mut [u8])->Result&lt;(u32,BufferInfo)>; BufferInfo{flags:BufferFlags{data_discontinuity,silent,timestamp_error}, index:u64 (device position), timestamp:u64 (QPC, 100 ns)}. Handle::wait_for_event(timeout_ms)->Result&lt;(),WasapiError> returns Err(EventTimeout) for ANY non-signaled result (timeout or wait failure). AudioClient::{set_get_eventhandle,get_audiocaptureclient,get_buffer_size,start_stream,stop_stream,get_mixformat,get_direction,get_sharemode,get_timing_mode} exist.
  - source: https://docs.rs/wasapi/0.26.0/wasapi/struct.AudioCaptureClient.html ; struct.Handle.html ; src/api.rs L2105-2325
- wasapi 0.26.0 pitfalls: (a) read_from_device does NOT zero packets flagged SILENT (upstream issue #88, opened 2026-10-31, will zero them); (b) if the caller buffer is smaller than the packet, read_from_device RELEASES (drops) the packet and then returns DataLengthTooShort; (c) read_from_device_to_deque unwraps ReleaseBuffer (can panic); (d) iterating a DeviceCollection with `for d in &coll` unwraps get_nbr_devices (can panic). Use index-based enumeration and a read buffer sized to the whole endpoint buffer.
  - source: wasapi-0.26.0/src/api.rs L2204-2278 and L515-527 ; https://github.com/HEnquist/wasapi-rs/issues/88
- wasapi implements IMMNotificationClient for us: DeviceEventCallbacks::{set_device_state_callback(Fn(String,DeviceState)), set_device_added_callback(Fn(String)), set_device_removed_callback(Fn(String)), set_default_device_callback(Fn(Direction,Role,Option&lt;String>)), set_property_value_callback(Fn(String,PROPERTYKEY))}; closures must be Fn+Send+Sync+'static; DeviceEnumerator::register_notification_callback(DeviceEventCallbacks)->Result&lt;DeviceEventRegistration>; unregisters on drop; callbacks run on a thread owned by the Windows audio system, must return quickly, must not call back into the DeviceEnumerator.
  - source: wasapi-0.26.0/src/events.rs L316-504 and src/api.rs L420-465 ; https://docs.rs/wasapi/0.26.0/wasapi/struct.DeviceEnumerator.html
- Process loopback in wasapi: AudioClient::new_application_loopback_client(process_id:u32, include_tree:bool) activates VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK via ActivateAudioInterfaceAsync, blocks on a Condvar, and requires initialize_client(.., &Direction::Capture, Shared). get_mixformat/get_device_period/get_current_padding etc. return errors on this client. GOTCHA: the doc comment says include_tree=false captures 'only the target process', but the code maps false to PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE (everything EXCEPT that process tree). Local test confirmed EXCLUDE = system mix minus the app.
  - source: wasapi-0.26.0/src/api.rs L757-887 ; https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode
- The wasapi example named `loopback.rs` is NOT WASAPI loopback: it captures from the default CAPTURE (microphone) device and plays it back; `record.rs` also opens get_default_device(&Direction::Capture). Copying either would violate Critical Rule 1.
  - source: wasapi-0.26.0/examples/loopback.rs L86-102 and examples/record.rs L24-27
- cpal 0.18.2 on WASAPI: building an INPUT stream on an OUTPUT device sets AUDCLNT_STREAMFLAGS_LOOPBACK (EVENTCALLBACK is always set); input streams are initialized WITHOUT AUTOCONVERTPCM; the thread blocks in WaitForMultipleObjectsEx(..., INFINITE) ('TODO: allow setting a timeout'); the SILENT flag is never handled (no occurrence of 'silent' in src/host/wasapi); DATA_DISCONTINUITY surfaces only as ErrorKind::Xrun; IMMNotificationClient is used only internally for default-device streams; no ActivateAudioInterfaceAsync process-loopback path. Its API also exposes input devices and default_input_device().
  - source: cpal-0.18.2/src/host/wasapi/{mod.rs L22-27, device.rs L852-856, stream.rs L581-585 and L797-827} (https://crates.io/api/v1/crates/cpal/0.18.2/download)
- windows 0.62.2 provides every raw item needed for the extras: AvSetMmThreadCharacteristicsW&lt;P0>(taskname:P0, taskindex:*mut u32)->Result&lt;HANDLE> and AvRevertMmThreadCharacteristics(HANDLE)->Result&lt;()> (both in Win32::System::Threading, feature Win32_System_Threading); QueryPerformanceCounter(*mut i64)->Result&lt;()> and QueryPerformanceFrequency(*mut i64)->Result&lt;()> (Win32::System::Performance); AUDCLNT_E_DEVICE_INVALIDATED=0x88890004, _DEVICE_IN_USE=0x8889000A, _RESOURCES_INVALIDATED=0x88890026, _SERVICE_NOT_RUNNING=0x88890010, _WRONG_ENDPOINT_TYPE=0x88890003; VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK=w!("VAD\\Process_Loopback"); AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK=1; PROCESS_LOOPBACK_MODE_INCLUDE=0/EXCLUDE=1. AUDCLNT_STREAMOPTIONS_POST_VOLUME_LOOPBACK is NOT defined in 0.62.2.
  - source: windows-0.62.2/src/Windows/Win32/{System/Threading/mod.rs, System/Performance/mod.rs, Media/Audio/mod.rs} (https://crates.io/api/v1/crates/windows/0.62.2/download)
- windows_core::Error::code(&self)->HRESULT (const fn), HRESULT is a Copy newtype over i32 with ok(self)->Result&lt;()>, is_ok(), is_err(); wasapi::initialize_mta()->HRESULT, initialize_sta()->HRESULT, deinitialize().
  - source: windows-result-0.4.1/src/error.rs L123 and src/hresult.rs L7-34 (windows-core 0.62.2 re-exports windows_result::*) ; wasapi-0.26.0/src/api.rs L72-85
- The project spec's Phase 1 PowerShell uses the property GUID {a45c254e-df1c-4ed4-a1e2-6d63d6a303f2},2, which is wrong (every device would print 'Unknown'). The real keys are PKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0},14 and PKEY_Device_DeviceDesc = same fmtid,2. Local COM test with the wrong GUID returned VT_EMPTY; with the right GUID names resolved.
  - source: windows-0.62.2/src/Windows/Win32/Devices/FunctionDiscovery/mod.rs L1986 and L2002 ; local probe scratchpad/probe/out (devices mode)
- Microsoft Loopback Recording: loopback is shared-mode only; in Windows before 10 1703 an event-driven loopback capture client receives NO events (workaround: drive from an event-driven render stream), and in Windows 10 1703+ event-driven loopback clients are supported; WASAPI uses the hardware loopback pin if present, otherwise copies the engine output; a trusted driver does not let loopback capture DRM-protected content; loopback contains the mix of all sessions.
  - source: https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording
- IAudioClient::Initialize: AUDCLNT_STREAMFLAGS_LOOPBACK is valid only on a render endpoint in shared mode (AUDCLNT_E_WRONG_ENDPOINT_TYPE / E_INVALIDARG otherwise); for event-driven shared streams the docs say both hnsPeriodicity and hnsBufferDuration must be 0 (but a non-zero duration with periodicity 0 was accepted on this machine up to 1 s, and OBS uses 5 s); SetEventHandle must be called before Start; after a failed Initialize you must obtain a new IAudioClient; 'loopback data in the capture buffer is in the device format'.
  - source: https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize
- IAudioCaptureClient::GetBuffer: read the whole packet or none; AUDCLNT_S_BUFFER_EMPTY (success, 0 frames) when nothing is available; packets vary in size and the engine may deliver several per wake-up, so drain with GetNextPacketSize until 0; pu64QPCPosition = 10,000,000*t/f (100 ns units); delaying ReleaseBuffer more than one period risks losing samples; AUDCLNT_E_DEVICE_INVALIDATED / RESOURCES_INVALIDATED / SERVICE_NOT_RUNNING are possible results.
  - source: https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer ; https://learn.microsoft.com/en-us/windows/win32/coreaudio/capturing-a-stream
- _AUDCLNT_BUFFERFLAGS: SILENT = 'treat all of the data in the packet as silence and ignore the actual data values'; DATA_DISCONTINUITY = 'data not correlated with the previous packet's device position (stream state transition or timing glitch)'; TIMESTAMP_ERROR = 'time at which the device position was recorded is uncertain'. DATA_DISCONTINUITY is undefined on the first GetBuffer after Start (Chromium ignores it when device_position==0).
  - source: https://learn.microsoft.com/en-us/windows/win32/api/audioclient/ne-audioclient-_audclnt_bufferflags ; https://chromium.googlesource.com/chromium/src/+/main/media/audio/win/audio_low_latency_input_win.cc
- AUDCLNT_E_DEVICE_INVALIDATED recovery per Microsoft: release IAudioClient and all interfaces; default-device apps re-resolve GetDefaultAudioEndpoint and re-activate; apps on a specific device re-activate the same device and, if that fails, ask the user to choose another. IAudioSessionEvents::OnSessionDisconnected gives the precise reason (device removal, format change, exclusive-mode override, ...).
  - source: https://learn.microsoft.com/en-us/windows/win32/coreaudio/recovering-from-an-invalid-device-error
- IMMNotificationClient rules: callbacks must be non-blocking (never wait on a synchronization object), must never call RegisterEndpointNotificationCallback/Unregister..., must never release the final reference on an MMDevice object; endpoint ID strings are opaque. Apps using MMDevice/WASAPI directly must implement stream routing themselves (listen for default-device change, device removal/addition, format change).
  - source: https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient ; https://learn.microsoft.com/en-us/windows/win32/coreaudio/stream-routing
- Roles: eConsole = games/system sounds/voice commands, eMultimedia = music/movies, eCommunications = voice calls. Chromium maps the 'default' sink to eConsole and only the special 'communications' device id to eCommunications, so a browser call without setSinkId plays on the eConsole default render endpoint.
  - source: https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/ne-mmdeviceapi-erole ; https://chromium.googlesource.com/chromium/src/+/main/media/audio/win/core_audio_util_win.cc (CreateDeviceByID L455-476)
- The loopback stream taps BEFORE endpoint volume and mute by default (so changing master volume does not change the captured level); Windows 11 24H2 added an opt-in post-volume tap (AUDCLNT_STREAMOPTIONS_POST_VOLUME_LOOPBACK via SetClientProperties) and requires drivers to support pre-volume. Per-app session volume (Volume Mixer) is applied before the mix and DOES affect the capture.
  - source: https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/windows-11-apis-for-audio-processing-objects ; https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-audioloopback ; https://learn.microsoft.com/en-us/windows/win32/api/audioclient/ne-audioclient-audclnt_streamoptions
- Process loopback: minimum 'Windows 10 Build 20348' per PROCESS_LOOPBACK_MODE and AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS docs (the ActivateAudioInterfaceAsync page says 20438 - docs disagree; Windows 11 is above both). INCLUDE_TARGET_PROCESS_TREE = streams of the process and its children; EXCLUDE_ = everything else. Pre-Windows-10 needs STA + an IAgileObject handler. Microsoft's own sample initializes it with SHARED, LOOPBACK|EVENTCALLBACK|AUTOCONVERTPCM, buffer 0, period 0 and a caller-chosen PCM format (GetMixFormat unsupported).
  - source: https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-activateaudiointerfaceasync ; https://raw.githubusercontent.com/microsoft/windows-classic-samples/main/Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp
- MMCSS: tasks Audio, Capture, Distribution, Games, Playback, Pro Audio, Window Manager; categories High=23-26 (Pro Audio), Medium=16-22 (foreground), Low=8-15; SystemResponsiveness reserves CPU for low-priority work. On this machine: SystemResponsiveness=20; Audio=Medium/prio 6/BackgroundOnly=True; Capture=Medium/5/True; Pro Audio=High/1/False.
  - source: https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service ; local registry HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile
- Exclusive mode: an exclusive-mode stream seizes the endpoint (other apps' audio is silenced); by default Windows lets an exclusive-mode request preempt shared-mode streams, so an exclusive app can invalidate our shared loopback stream; a shared stream cannot be created while another app holds the device exclusively (AUDCLNT_E_DEVICE_IN_USE). Loopback itself is impossible in exclusive mode.
  - source: https://learn.microsoft.com/en-us/windows/win32/coreaudio/exclusive-mode-streams ; https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize
- Bluetooth on Windows 11: A2DP and HFP endpoints are UNIFIED into a single render endpoint (plus one input endpoint if HFP is supported); Windows switches to HFP (mono, 8 kHz CVSD or 16 kHz mSBC wideband) when any app opens the headset microphone or creates a Communications-category output stream, otherwise A2DP is used; audio from other apps is resampled to the active profile. On Windows 10 there were separate 'Stereo' and 'Hands-Free' endpoints and anything sent to Stereo was discarded while HFP was active. LE Audio can keep stereo playback with the mic on (32 kHz TMAP / 16-24 kHz HAP in calls) on supported PCs.
  - source: https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio#selection-of-a2dp-and-hfp-endpoints ; https://learn.microsoft.com/en-us/windows/win32/coreaudio/communications-audio-format-capabilities
- Tauri 2 docs: the event system 'is not designed for low latency or high throughput' and sends JSON strings; Channels (tauri::ipc::Channel&lt;T>, passed as a #[tauri::command] parameter, send via .send(..)) are 'designed to be fast and deliver ordered data'. Use a Channel for the ~20 Hz level meter and Emitter::emit only for rare events (device list changes).
  - source: https://v2.tauri.app/develop/calling-frontend/
- Smart App Control (enforced on this PC) blocks unsigned code unless cloud reputation predicts it safe, applies to ALL executables (not just downloads), reputation can change over time, and Microsoft tells developers to sign every exe/dll.
  - source: https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/appcontrol ; https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview ; https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- LOCAL: SAC blocked 1 of 10 freshly csc-compiled unsigned probe executables (probe9.exe) while the other nine ran; CodeIntegrity events 3033/3077/3118 'Smart App Control Block Details' were logged at 11:37:27. A cargo build creates many unsigned binaries (build scripts, proc-macro DLLs, test exes), so this nondeterminism is a project-level risk.
  - source: Get-WinEvent Microsoft-Windows-CodeIntegrity/Operational on this machine, 2026-11-01 11:37
- LOCAL (probe, C# COM interop, render endpoints only; the code hard-aborts if IMMEndpoint.GetDataFlow != eRender): 11 eRender endpoints in all states: exactly one ACTIVE (0x1) 'Speakers (Realtek(R) Audio)' id {0.0.0.00000000}.{&lt;speakers-endpoint-guid>} that is default for eConsole+eMultimedia+eCommunications; one UNPLUGGED (0x8) 'Headphones (Realtek(R) Audio)'; nine NOTPRESENT (0x4) (USB-Audio, HDMI/DP, LG TV, ...). Endpoint master volume 0.62, not muted. DeviceFormat = 48000 Hz/2ch/16-bit; mix format = 48000/2ch/32-bit float, mask 0x3; default period 10 ms, minimum 3 ms.
  - source: &lt;scratch>\probe\out\probe_run1.txt (+ devices mode run)
- LOCAL: event-driven endpoint loopback (LOOPBACK|EVENTCALLBACK) works on this Windows 11 build: ~100 events/s, 0 event timeouts, coverage 99.8-100.2% across 3 full runs, and QPC/device-position continuous (no jumps) while a media app rendered. Initialize accepted hnsBufferDuration 0/100000/200000 (all clamped to GetBufferSize=1056 frames = 22 ms), 500000 -> 2400 frames, 2,000,000 -> 9600, 10,000,000 -> 48000 frames (1 s). First packet arrives ~15-200 ms after Start and carries DATA_DISCONTINUITY.
  - source: scratchpad/probe/out/probe_run1.txt and the repeat run in this session
- LOCAL stress (4 busy threads on 2 logical CPUs, first 600 ms excluded, 7.4 s per config, managed .NET probe): buffer 22 ms/no MMCSS lost ~38 ms (5 discontinuities, loop gap max 61 ms); 22 ms + MMCSS Audio lost ~136 ms (6, max 79 ms, p99 10.6 vs 14.0 ms); 22 ms + MMCSS Pro Audio lost ~174 ms (6, max 111 ms); 22 ms + MMCSS Capture lost ~46 ms (4); buffer 200 ms lost ~100 ms (1 discontinuity, a 308 ms stall); buffer 1 s lost 0 in all three runs (no MMCSS x2, Pro Audio x1; stalls up to 148 ms absorbed). The buffer size, not MMCSS, is the decisive factor.
  - source: scratchpad/probe/out/probe_stress2.txt and probe_stress3.txt
- LOCAL: endpoint loopback with AUTOCONVERTPCM|SRC_DEFAULT_QUALITY and a 16 kHz/mono/16-bit PCM format initializes (GetBufferSize 356 frames), delivers 401 packets in 4 s at 159.9 frames/packet (100.1% coverage), non-silent audio (RMS 0.33-0.35 vs 0.35 for the float stereo control). WITHOUT AUTOCONVERTPCM the same format fails with AUDCLNT_E_UNSUPPORTED_FORMAT (0x88890008). The channel mask matters: plain WAVEFORMATEX and WAVEFORMATEXTENSIBLE mask 0 gave RMS 0.352/peak 0.989; mask 0x1 or 0x4 gave RMS 0.234/peak 0.884 (about -3.5 dB, a different stereo->mono matrix). wasapi::WaveFormat::new(.., channels=1, None) defaults to mask 0x1.
  - source: scratchpad/probe/out/probe_conv1.txt and probe_ext1.txt ; wasapi-0.26.0/src/waveformat.rs L186-198
- LOCAL process loopback (VAD\Process_Loopback via ActivateAudioInterfaceAsync, float32 48 kHz stereo, LOOPBACK|EVENTCALLBACK|AUTOCONVERTPCM, buffer 0 -> 480 frames): works on this machine. INCLUDE tree of the rendering app: music (peak 0.78, 98.6% coverage). INCLUDE of a silent process, and EXCLUDE of the rendering app: a CONTINUOUS stream of 480-frame packets (100% coverage, all samples exactly 0, flags=0 i.e. NOT flagged SILENT). Device position is always 0 in this mode; QPC stays contiguous.
  - source: scratchpad/probe/out/probe_proc1.txt
- LOCAL: loopback packet QPC timestamps are essentially 'now' at read time: age of packet START at GetBuffer p50 0.1 ms, p99 2.8 ms, max 12.9 ms (packet END is ~10 ms in the future for 10 ms packets). Device-position jumps equal QPC jumps on DISCONTINUITY (e.g. 8544 frames = 178 ms). This supports a QPC-anchored timeline with a 50 ms idle margin.
  - source: scratchpad/probe/out/probe_age.txt ; probe_run1.txt gap analysis
- Third-party source evidence for the 'nothing is rendering' case: Chromium's endpoint-loopback input deliberately does NOT use EVENTCALLBACK on the capture stream ('does not support event-driven mode since it requires active output audio to trigger the event') and instead starts an extra event-driven RENDER stream on the same endpoint to wake its capture loop and always zero-fills SILENT packets; OBS Studio has a 'Silent loopback fix' that renders a silent sample because the loopback stream otherwise 'stops and messes up timestamps' during silence, polls loopback every 10 ms, uses a 5 s buffer, and substitutes zeros for SILENT packets.
  - source: https://chromium.googlesource.com/chromium/src/+/main/media/audio/win/audio_low_latency_input_win.cc (L2018-2160, L1520-1526) ; https://raw.githubusercontent.com/obsproject/obs-studio/master/plugins/win-wasapi/win-wasapi.cpp (L638, L751-800, L1044-1054, L1128-1133)
- LOCAL indirect evidence for the pre-volume tap on this driver: with endpoint master volume at 0.62 the loopback data peaked at 0.986 (a post-volume tap could not exceed ~0.62 of full scale). Not a controlled test.
  - source: scratchpad/probe/out/probe_run1.txt

## API sketch (research-time, superseded by ARCHITECTURE.md where they differ)

```text
// =====================================================================================
// WASAPI loopback engine sketch. Targets wasapi 0.26.0 (enums passed BY REFERENCE; 0.27 changes that) + windows 0.62.2.
// NOT COMPILED (no Rust/MSVC on this box). Every external signature was read from wasapi-0.26.0/src/*.rs, windows-0.62.2 and
// windows-result-0.4.1 sources and cross-checked on docs.rs/wasapi/0.26.0. [measured] = observed with the C# probe on this PC.
// =====================================================================================
// Cargo.toml
//   [target.'cfg(windows)'.dependencies]
//   wasapi  = "=0.26.0"   # exact pin: 0.27 is breaking; later versions are expected to drop the `windows` umbrella crate
//   windows = { version = "0.62.2", features = ["Win32_Foundation","Win32_Media_Audio","Win32_System_Threading","Win32_System_Performance"] }
//   # only what OUR code touches (QPC, MMCSS, HRESULT consts); Cargo unifies these with wasapi's and Tauri's features into ONE windows build.
// Layout: src-tauri/src/audio/{render_only.rs, timeline.rs, engine.rs, mod.rs}. render_only.rs is the ONLY file that may contain `Direction::Capture`.

// ---------------------------------- render_only.rs ----------------------------------
use wasapi::{AudioCaptureClient, AudioClient, Device, DeviceEnumerator, DeviceState, Direction, Handle, Role,
             SampleType, StreamMode, WasapiError, WaveFormat};
use windows::Win32::Media::Audio::{AUDCLNT_E_DEVICE_INVALIDATED, AUDCLNT_E_DEVICE_IN_USE,
             AUDCLNT_E_RESOURCES_INVALIDATED, AUDCLNT_E_SERVICE_NOT_RUNNING};

pub enum Target { FollowDefault, Id(String) }               // ids only ever come from list_render_devices()
pub struct DeviceInfo { pub id: String, pub name: String, pub is_default_console: bool,
                        pub is_default_multimedia: bool, pub is_default_communications: bool }

#[derive(Debug)]
pub enum EngineError {
    NotARenderEndpoint,                 // Rule 2: refuse. NEVER fall back to a capture endpoint
    NoDefaultRenderDevice,
    EndpointNotActive(DeviceState),
    DeviceLost(WasapiError),            // INVALIDATED / RESOURCES_INVALIDATED / SERVICE_NOT_RUNNING => tear down, reopen with backoff
    ExclusiveBusy(WasapiError),         // AUDCLNT_E_DEVICE_IN_USE => another app owns the device exclusively
    UnsupportedFormat,
    Wasapi(WasapiError),
}
fn classify(e: WasapiError) -> EngineError {
    let code = match &e { WasapiError::Windows(w) => Some(w.code()), _ => None };      // windows_core::Error::code() -> HRESULT
    match code {
        Some(c) if c == AUDCLNT_E_DEVICE_INVALIDATED || c == AUDCLNT_E_RESOURCES_INVALIDATED
                || c == AUDCLNT_E_SERVICE_NOT_RUNNING => EngineError::DeviceLost(e),
        Some(c) if c == AUDCLNT_E_DEVICE_IN_USE => EngineError::ExclusiveBusy(e),
        _ => EngineError::Wasapi(e),
    }
}

/// Refresh button / hot-plug list. ACTIVE render endpoints only (get_device_collection = EnumAudioEndpoints(eRender, ACTIVE)).
pub fn list_render_devices(en: &DeviceEnumerator) -> Result<Vec<DeviceInfo>, WasapiError> {
    let coll = en.get_device_collection(&Direction::Render)?;
    let dflt = |r: Role| en.get_default_device_for_role(&Direction::Render, &r).ok().and_then(|d| d.get_id().ok());
    let (c, m, k) = (dflt(Role::Console), dflt(Role::Multimedia), dflt(Role::Communications));
    let mut v = Vec::new();
    for i in 0..coll.get_nbr_devices()? {                        // index loop: `for d in &coll` unwraps inside wasapi 0.26.0
        let d = coll.get_device_at_index(i)?;
        let id = d.get_id()?;
        let name = d.get_friendlyname().unwrap_or_else(|_| "(unnamed output)".into());
        v.push(DeviceInfo { is_default_console: c.as_deref() == Some(id.as_str()),
                            is_default_multimedia: m.as_deref() == Some(id.as_str()),
                            is_default_communications: k.as_deref() == Some(id.as_str()), id, name });
    }
    Ok(v)
}

/// STRUCTURAL GUARD: the only way to obtain something a Loopback can be opened on. No pub constructor besides resolve().
pub struct RenderEndpoint { device: Device, pub id: String, pub name: String }
impl RenderEndpoint {
    pub fn resolve(en: &DeviceEnumerator, t: &Target) -> Result<Self, EngineError> {
        let device = match t {
            Target::FollowDefault => en.get_default_device_for_role(&Direction::Render, &Role::Console)   // eRender+eConsole only
                                       .map_err(|_| EngineError::NoDefaultRenderDevice)?,
            Target::Id(id) => en.get_device(id).map_err(classify)?,   // wasapi derives direction from IMMEndpoint::GetDataFlow
        };
        if device.get_direction() != Direction::Render { return Err(EngineError::NotARenderEndpoint); }
        let st = device.get_state().map_err(classify)?;
        if st != DeviceState::Active { return Err(EngineError::EndpointNotActive(st)); }
        let id = device.get_id().map_err(classify)?;
        let name = device.get_friendlyname().unwrap_or_else(|_| id.clone());
        Ok(Self { device, id, name })
    }
}

/// The ONLY place that names Direction::Capture: it is the STREAM direction. (Render device, Capture stream, Shared) => AUDCLNT_STREAMFLAGS_LOOPBACK.
fn initialize_loopback(c: &mut AudioClient, f: &WaveFormat, m: &StreamMode) -> Result<(), WasapiError> {
    debug_assert_eq!(c.get_direction(), Direction::Render);      // also true for new_application_loopback_client()
    c.initialize_client(f, &Direction::Capture, m)
}

#[derive(Clone, Copy)] pub enum OutFormat { Native, Speech16kMono }
pub struct Loopback { client: AudioClient, cap: AudioCaptureClient, pub fmt: WaveFormat, buf: Vec<u8>, evt: Handle /* last: closed after client release */ }

impl Loopback {
    pub fn open(ep: &RenderEndpoint, out: OutFormat) -> Result<Self, EngineError> {
        let mut last = None;
        // 1 s buffer first [measured: zero loss under 2x CPU oversubscription; 22 ms lost audio]; 0 = the value the docs prescribe for event-driven shared streams.
        for buffer_hns in [10_000_000i64, 0] {
            let mut client = ep.device.get_iaudioclient().map_err(classify)?;       // fresh IAudioClient per attempt (docs: after a failed Initialize get a new one)
            let (fmt, autoconvert) = match out {
                OutFormat::Native => (client.get_mixformat().map_err(classify)?, false),   // GetMixFormat: always accepted in shared mode
                // mask Some(0): same OS downmix as a plain WAVEFORMATEX [measured]; None => mask 0x1 => ~-3.5 dB different matrix
                OutFormat::Speech16kMono => (WaveFormat::new(16, 16, &SampleType::Int, 16_000, 1, Some(0)), true), // adds AUTOCONVERTPCM|SRC_DEFAULT_QUALITY
            };
            let mode = StreamMode::EventsShared { autoconvert, buffer_duration_hns: buffer_hns };
            match initialize_loopback(&mut client, &fmt, &mode) {
                Ok(()) => {
                    let evt = client.set_get_eventhandle().map_err(classify)?;             // must precede Start
                    let cap = client.get_audiocaptureclient().map_err(classify)?;
                    let frames = client.get_buffer_size().map_err(classify)? as usize;
                    let buf = vec![0u8; frames * fmt.get_blockalign() as usize];           // >= any single packet: read_from_device never drops one
                    client.start_stream().map_err(classify)?;
                    return Ok(Self { client, cap, fmt, buf, evt });
                }
                Err(e) => last = Some(classify(e)),
            }
        }
        Err(last.unwrap())
    }
    /// Optional "browser only" mode. include_tree=true => INCLUDE_TARGET_PROCESS_TREE. CAUTION: wasapi 0.26.0 maps `false` to EXCLUDE (everything except pid).
    /// No IMMDevice and no endpoint id are involved, so it cannot reach a microphone. Tail identical to open() (set_get_eventhandle, get_audiocaptureclient, start_stream).
    pub fn open_process(pid: u32) -> Result<Self, EngineError> {
        let mut client = AudioClient::new_application_loopback_client(pid, true).map_err(classify)?;
        let fmt = WaveFormat::new(32, 32, &SampleType::Float, 48_000, 2, None);        // GetMixFormat is unsupported on this client
        initialize_loopback(&mut client, &fmt, &StreamMode::EventsShared { autoconvert: true, buffer_duration_hns: 0 }).map_err(classify)?;
        todo!("same tail as open()")
    }
}
impl Drop for Loopback { fn drop(&mut self) { let _ = self.client.stop_stream(); } }

// ---------------------------------- timeline.rs (pure, unit-testable) ----------------------------------
// Packet QPC position is ~"now" at read time (p99 2.8 ms, max 12.9 ms old) [measured], so anchor at Start and pad by QPC.
const IDLE_MARGIN_100NS: u64 = 500_000;                           // 50 ms
pub struct Timeline { rate: u64, anchor_q: u64, emitted: u64 }
impl Timeline {
    pub fn start(rate: u32, now_q: u64) -> Self { Self { rate: rate as u64, anchor_q: now_q, emitted: 0 } }
    fn frames(&self, dq: u64) -> u64 { (dq as u128 * self.rate as u128 / 10_000_000) as u64 }
    fn slack(&self) -> u64 { self.rate / 500 }                      // 2 ms
    /// Before emitting a real packet: (silence frames to insert first, leading frames to drop). Covers DATA_DISCONTINUITY gaps and the end of an idle period.
    pub fn on_packet(&mut self, pkt_q: u64, n: u32) -> (u64, u32) {
        let pos = self.frames(pkt_q.saturating_sub(self.anchor_q)); let s = self.slack();
        let (pad, drop) = if pos > self.emitted + s { (pos - self.emitted, 0) }
                          else if pos + s < self.emitted { (0, (self.emitted - pos).min(n as u64) as u32) } else { (0, 0) };
        self.emitted += pad + (n - drop) as u64; (pad, drop)
    }
    /// Every loop pass after draining: silence owed to the wall clock (endpoint loopback may deliver NO packets while nothing renders).
    pub fn on_idle(&mut self, now_q: u64) -> u64 {
        let target = self.frames(now_q.saturating_sub(self.anchor_q + IDLE_MARGIN_100NS));
        if target > self.emitted + self.slack() { let p = target - self.emitted; self.emitted = target; p } else { 0 }
    }
}
fn qpc_100ns() -> u64 {                                              // same formula Microsoft documents for pu64QPCPosition
    use windows::Win32::System::Performance::{QueryPerformanceCounter, QueryPerformanceFrequency};
    static F: std::sync::OnceLock<i64> = std::sync::OnceLock::new();
    let f = *F.get_or_init(|| { let mut f = 0i64; unsafe { let _ = QueryPerformanceFrequency(&mut f); } f.max(1) });
    let mut t = 0i64; unsafe { let _ = QueryPerformanceCounter(&mut t); }
    ((t as u128 * 10_000_000) / f as u128) as u64
}

// ---------------------------------- engine.rs ----------------------------------
fn to_mono_f32(bytes: &[u8], f: &WaveFormat, out: &mut Vec<f32>) -> Result<(), EngineError> {
    let ch = f.get_nchannels() as usize;
    match (f.get_subformat().map_err(EngineError::Wasapi)?, f.get_bitspersample()) {
        (SampleType::Float, 32) => for fr in bytes.chunks_exact(4 * ch) { out.push(fr.chunks_exact(4).map(|b| f32::from_le_bytes([b[0],b[1],b[2],b[3]])).sum::<f32>() / ch as f32) },
        (SampleType::Int, 16)   => for fr in bytes.chunks_exact(2 * ch) { out.push(fr.chunks_exact(2).map(|b| i16::from_le_bytes([b[0],b[1]]) as f32 / 32768.0).sum::<f32>() / ch as f32) },
        _ => return Err(EngineError::UnsupportedFormat),
    }
    Ok(())
}
pub enum LoopEnd { Stopped, Reopen }
pub fn capture_loop(ep: &RenderEndpoint, lb: &mut Loopback, ctl: &std::sync::mpsc::Receiver<Ctl>, emit: &mut dyn FnMut(Event)) -> Result<LoopEnd, EngineError> {
    use std::time::{Duration, Instant};
    let mut tl = Timeline::start(lb.fmt.get_samplespersec(), qpc_100ns());
    let (mut mono, mut first) = (Vec::<f32>::with_capacity(4096), true);
    loop {
        // 1) control plane, non-blocking (Stop / Refresh / Dev(..) events pushed by the notification closures)
        while let Ok(c) = ctl.try_recv() { match c { Ctl::Stop | Ctl::Shutdown => return Ok(LoopEnd::Stopped),
                                                      Ctl::Dev(ev) if ev.concerns(&ep.id) => return Ok(LoopEnd::Reopen), _ => {} } }
        // 2) the event is a HINT, never the clock: 20 ms timeout keeps control + silence padding alive when nothing renders
        let t0 = Instant::now();
        let signaled = lb.evt.wait_for_event(20).is_ok();       // Err(EventTimeout) also covers WAIT_FAILED
        if !signaled && t0.elapsed() < Duration::from_millis(5) { std::thread::sleep(Duration::from_millis(10)); }   // never spin on a failing wait
        // 3) drain EVERY packet (auto-reset event: several packets may be coalesced)
        loop {
            match lb.cap.get_next_packet_size().map_err(classify)? { Some(n) if n > 0 => {}, _ => break }
            let (frames, info) = lb.cap.read_from_device(&mut lb.buf).map_err(classify)?;
            if frames == 0 { break; }
            let bytes = frames as usize * lb.fmt.get_blockalign() as usize;
            if info.flags.silent { lb.buf[..bytes].fill(0); }   // MS: ignore the data of SILENT packets (wasapi 0.26.0 does not zero them)
            if info.flags.data_discontinuity && !first { /* count a glitch; the QPC jump below pads the hole */ }
            first = false;                                      // DATA_DISCONTINUITY is undefined on the first packet after Start
            let q = if info.flags.timestamp_error { qpc_100ns() } else { info.timestamp };   // QPC, 100 ns units
            let (pad, drop) = tl.on_packet(q, frames);
            if pad > 0 { emit(Event::Silence { frames: pad }); }
            mono.clear(); to_mono_f32(&lb.buf[..bytes], &lb.fmt, &mut mono)?;
            emit(Event::Audio(mono[drop as usize..].to_vec())); // consumer does 50 ms RMS/peak dBFS (20*log10(max(x,1e-5))) -> Event::Level, resample/VAD on ANOTHER thread
        }
        // 4) idle clock
        let owed = tl.on_idle(qpc_100ns()); if owed > 0 { emit(Event::Silence { frames: owed }); }
    }
}

// Supervisor (shape): resolve -> open -> capture_loop; on Ok(Reopen) retry at once; on Err(DeviceLost | NoDefaultRenderDevice | EndpointNotActive | ExclusiveBusy)
// emit Event::Reconnecting and retry with backoff 200,500,1000,2000,2000 ms, then Event::Failed(err). ANY other error => Event::Failed(err): explicit error, never a mic.

// Engine thread: dedicated std::thread (fresh thread => MTA; Tauri's main thread is STA and untouched). All COM objects live inside the inner scope.
pub fn spawn_engine(rx: std::sync::mpsc::Receiver<Ctl>, tx_self: std::sync::mpsc::Sender<Ctl>) -> std::thread::JoinHandle<()> {
    std::thread::Builder::new().name("wasapi-engine".into()).spawn(move || {
        if wasapi::initialize_mta().ok().is_err() { return; }           // HRESULT::ok()
        {
            let _mmcss = Mmcss::join();                                 // cheap insurance only; benefit is small [measured]
            let en = DeviceEnumerator::new().expect("MMDeviceEnumerator");
            let mut cb = wasapi::DeviceEventCallbacks::new();           // callbacks: non-blocking channel sends ONLY (MS rules), no locks, no COM calls
            { let t = tx_self.clone(); cb.set_default_device_callback(move |d: Direction, r: Role, id: Option<String>| { if d == Direction::Render { let _ = t.send(Ctl::Dev(DevEvent::DefaultChanged(r, id))); } }); }
            { let t = tx_self.clone(); cb.set_device_state_callback(move |id: String, s: DeviceState| { let _ = t.send(Ctl::Dev(DevEvent::State(id, s))); }); }
            { let t = tx_self.clone(); cb.set_device_added_callback(move |id: String| { let _ = t.send(Ctl::Dev(DevEvent::Added(id))); }); }
            { let t = tx_self;        cb.set_device_removed_callback(move |id: String| { let _ = t.send(Ctl::Dev(DevEvent::Removed(id))); }); }
            let _reg = en.register_notification_callback(cb);           // keep alive; Drop unregisters
            // loop on rx: Ctl::Start(target) => supervise(..); Ctl::Refresh => list_render_devices(&en) -> Event::Devices; Ctl::Shutdown => break
        }
        wasapi::deinitialize();                                         // CoUninitialize only after every COM object is dropped
    }).expect("spawn wasapi-engine")
}
struct Mmcss(windows::Win32::Foundation::HANDLE);
impl Mmcss { fn join() -> Option<Self> { let mut idx = 0u32;
    unsafe { windows::Win32::System::Threading::AvSetMmThreadCharacteristicsW(windows::core::w!("Audio"), &mut idx) }.ok().map(Mmcss) } }
impl Drop for Mmcss { fn drop(&mut self) { unsafe { let _ = windows::Win32::System::Threading::AvRevertMmThreadCharacteristics(self.0); } } }

// Tauri glue: #[tauri::command] start_capture(state, target, on_level: tauri::ipc::Channel<LevelMsg>) sends Ctl::Start; the emit closure forwards Event::Level via on_level.send(..) (~20 Hz, ordered);
// rare Event::Devices / State go through AppHandle::emit (Events are documented as unsuited to high throughput).

// ---------------------------------- CI guard: mic capture structurally unreachable ----------------------------------
// #[test] fn only_one_capture_direction_token() {
//   let hits: Vec<_> = walk("src").flat_map(|f| lines(&f).filter(|l| !l.trim_start().starts_with("//") &&
//        (l.contains("Direction::Capture") || l.contains("eCapture") || l.contains("DEVINTERFACE_AUDIO_CAPTURE"))).map(move |_| f.clone())).collect();
//   assert_eq!(hits.len(), 1); assert!(hits[0].ends_with("render_only.rs")); }
// Also: wrap the engine thread body in catch_unwind (wasapi 0.26.0 still has unwrap()s) and report a panic as Event::Failed.

// Phase-2 gate (STOP-AND-VERIFY): (1) list_render_devices() shows 'Speakers (Realtek(R) Audio)' with all three default flags; (2) start(FollowDefault, Speech16kMono):
// RMS rises >30 dB when media plays and falls to about -100 dBFS when paused, with Level events still arriving at ~20 Hz (padded silence);
// (3) cross-check with ep.device.get_audiometerinformation()?.get_peak_value() (independent OS meter, same IMMDevice);
// (4) plug/unplug a headset => automatic refresh + reopen; (5) the CI test above passes.
```

## Alternatives considered

| Option | Verdict |
|---|---|
| cpal 0.18.2 (input stream on an output device = implicit loopback) | Rejected for the engine. It works (LOOPBACK\|EVENTCALLBACK, actively developed, shares windows ^0.62) but the loop is private: INFINITE wait (no silence padding or command servicing), SILENT flag ignored, no first-class QPC/flag access, input streams lack AUTOCONVERTPCM, DATA_DISCONTINUITY is only an Xrun error, notifications are internal, no process loopback, and the API exposes default_input_device()/input_devices() (more mic-misuse surface). Fine only as a throwaway cross-check. |
| Direct `windows` 0.62.2 bindings (write our own enumeration, PROPVARIANT/property-store handling, #[implement(IMMNotificationClient)], activation handler, capture loop) | Viable fallback and the vendor path if wasapi stalls: every needed type exists in 0.62.2. Cost: roughly 400-600 lines of unsafe COM glue we would own (CoTaskMemFree, PROPVARIANT clear, apartment rules), re-implementing what wasapi already wraps and tests. Note that `windows` itself is frozen at 0.62.2 while the ecosystem moves to 0.100 focused crates, so a migration is coming either way. |
| wasapi 0.26.0 + windows 0.62.2 for four small extras (RECOMMENDED) | Chosen. Safe wrappers for everything required, active upstream, same windows version as the Tauri stack (one build, no duplicate crate on a 7.3 GB RAM machine). Isolate all wasapi use in one adapter module and pin exactly so the churn (0.27 breaking; later windows-core 0.100 + MSRV 1.95) becomes a one-file migration. |
| Wait for / adopt wasapi 0.27+ (or the windows-core 0.100 rewrite) | Not now. 0.27.0 is unreleased and breaking; the 0.100 line would add a second windows-core to a Tauri tree that is fully on 0.62 and needs Rust 1.95. Revisit after Tauri moves. |
| Process loopback (new_application_loopback_client) as the PRIMARY capture | Not the default, but a worthwhile optional mode: verified working on this machine and it delivers a continuous zero-filled stream when the target is silent (no idle problem) and isolates the browser from other audio (the user has Spotify and Steam running). Costs: needs the browser's root PID with include_tree=true (Chrome/Edge render audio in child processes), GetMixFormat unsupported, device position is always 0, and the spec's wording is 'a render endpoint'. |
| Open a helper render stream on the user's endpoint to keep loopback alive (the Chromium / OBS workaround) | Not by default: it adds a stream on the user's device (keeps Bluetooth/HDMI links busy, shows up as an app session, interacts with profile switching). Prefer QPC-based silence synthesis. Keep as a documented fallback if Phase 2 shows glitchy timestamps after idle-to-active transitions on the Realtek driver. |
| Hardware loopback capture devices such as 'Stereo Mix' / 'What U Hear' | Forbidden: they are CAPTURE endpoints, so they would violate Critical Rule 1 and the structural eRender-only guard rejects them. |
| Polling-only loop (Sleep 10 ms + GetNextPacketSize) instead of event + timeout | Works (measured 99-101% coverage) and is what OBS does for loopback, but depends on timer resolution and always wakes 100x/s. The event+20 ms timeout loop is equally robust (the event is only a hint) and wakes earlier under load. |
| Fixed small buffer (0 / 22 ms) as the documentation prescribes for event-driven shared streams | Rejected for this hardware: lost 38-174 ms of audio per 7.4 s under CPU contention. Use 1 s with automatic fallback to 0 if Initialize rejects it. |

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Mic capture by mistake: wasapi's initialize_client with (Capture device, Capture stream) silently opens an ordinary microphone, and two bundled examples (`loopback.rs`, `record.rs`) do exactly that. Also the WebView frontend could call getUserMedia. | high | RenderEndpoint newtype (only resolve(), which rejects non-Render and non-Active devices and takes ids only from the eRender listing), the single-token CI test for `Direction::Capture`/`eCapture`, never calling get_default_device(&Direction::Capture) or enumerating Capture, errors surfaced as Event::Failed with no fallback. Hardening to evaluate: deny WebView2 microphone/camera permission requests (API not verified here). |
| CPU starvation on 2 cores (browser + webcam + our app) makes the engine buffer overflow and silently drop audio (measured: 22 ms buffer lost 38-174 ms per 7.4 s under 2x oversubscription, MMCSS did not prevent it). | high | 1 s hnsBufferDuration (zero loss measured), keep the capture thread to read+copy+meter only (do resampling/VAD/Whisper on other threads behind a bounded channel with a drop-oldest policy), count DATA_DISCONTINUITY and pad the QPC gap, MMCSS 'Audio' as cheap extra. Note the probe was managed .NET (GC pauses may inflate stalls vs Rust). |
| Windows Smart App Control (enforced) blocked 1 of 10 freshly compiled unsigned executables here; cargo produces many unsigned binaries (build scripts, proc-macro DLLs, test exes, the app itself), so builds or the shipped exe may be blocked nondeterministically. | high | Raise with the user/orchestrator early: Microsoft says sign every exe/dll (Trusted/Artifact Signing or a CA-issued cert) or test with the SAC audit policy; consider whether to keep SAC on. Do not change the setting without the user's consent. Retry builds that are blocked and log CodeIntegrity events 3033/3077. |
| wasapi API/dependency churn: 0.27.0 is breaking and imminent; a later release drops the `windows` umbrella for windows-core 0.100 (MSRV 1.95) and may conflict with the Tauri tree; wasapi 0.26.0 also has unwrap()s that can panic (DeviceCollection iterator, read_from_device_to_deque). | medium | Pin =0.26.0, confine all wasapi calls to render_only.rs/engine.rs, avoid the panicking APIs, wrap the engine thread in catch_unwind, keep the option to vendor wasapi 0.26.0 (MIT, ~5k lines) or fall back to direct windows bindings. |
| Behaviour of ENDPOINT loopback while NOTHING is rendering is not documented by Microsoft and could not be observed here (a media app rendered continuously). Chromium/OBS code suggests the stream stops delivering packets/events (and may glitch timestamps on resume), which would starve VAD/segmentation of the trailing silence that closes an utterance. | medium | The loop does not depend on either behaviour: 20 ms timeouts plus the QPC Timeline pad silence when no packets arrive and re-sync on resume. Add engine diagnostics (packets/s, pad frames, glitches) and run the manual idle test (pause all audio) as part of the Phase 2 gate. |
| Bluetooth headset: on Windows 11 the endpoint is unified and the profile flips to HFP (mono, 8 or 16 kHz) when the browser opens the headset mic, changing the mix format; the loopback stream is expected to be invalidated at that moment and the interviewer audio becomes narrowband/wideband speech, which can hurt transcription accuracy. | medium | Treat AUDCLNT_E_DEVICE_INVALIDATED and device property/default changes as normal events (reopen with backoff, re-read the mix format), request the fixed 16 kHz mono s16 target via AUTOCONVERTPCM so downstream is format-agnostic, show the active format in the UI, and recommend a wired headset/speakers when accuracy matters. |
| Audio the engine cannot see: exclusive-mode render streams bypass the shared engine (and exclusive apps can invalidate our shared stream); DRM-protected content is withheld from loopback by trusted drivers; per-app muting in Volume Mixer silences the loopback (master volume does not). | low | Browsers use shared mode and WebRTC is not DRM, so low impact; surface AUDCLNT_E_DEVICE_IN_USE with a clear message, and show the endpoint's OS meter next to ours so users can tell 'nothing is playing' from 'engine is broken'. |
| Following the wrong endpoint: users plug in a headset mid-session; the browser follows the default (eConsole) device but a pinned loopback stays on the old, now silent endpoint. | medium | Default to Target::FollowDefault, subscribe to default-device/state notifications, debounce 150-300 ms and reopen; when pinned, show a banner if the default render device differs from the pinned one. |
| Channel-mask pitfall in the OS downmix (mask 0x1/0x4 gave about -3.5 dB vs mask 0) and unknown quality of the OS resampler (AUTOCONVERTPCM) versus a dedicated resampler. | low | Use mask Some(0) and normalise gain downstream; keep OutFormat::Native as a switchable path and compare Whisper accuracy in Phase 3 before committing to OS-side conversion. |

## Not verified at research time

- Endpoint-loopback packet delivery while NOTHING is rendering on this Realtek / Windows 11 26200 machine: Spotify rendered continuously, so the idle waiter (up to 20 min, meter==0 for 2 s) never triggered. Only Chromium/OBS source comments (third-party) describe the stream stopping; Microsoft docs are silent. To close it: ask the user to pause all audio and run `probe.exe idle 60` (source: &lt;scratch>\probe\src\Probe.cs, binaries in ...\probe\out).
- The Rust sketch was NOT compiled or run (no Rust toolchain or MSVC on the machine). Signatures were verified by reading wasapi-0.26.0, windows-0.62.2 and windows-result-0.4.1 sources and docs.rs pages, not by rustc; borrow-check/edition details may need small fixes.
- Whether a Bluetooth profile switch (A2DP&lt;->HFP) actually invalidates an already running loopback stream: inferred from Microsoft's docs (unified endpoint, automatic resampling to the active profile, stream-routing guidance on format change). No Bluetooth device was paired, so nothing was tested.
- Quality of the Windows resampler/downmixer behind AUTOCONVERTPCM|SRC_DEFAULT_QUALITY (aliasing, impact on Whisper word-error rate): only level/continuity were measured, not spectrum or WER.
- MMCSS and buffer results come from a managed .NET probe under synthetic busy-loop load; Rust has no GC, and real browser+webcam load was not reproduced. Absolute numbers (38-174 ms lost, 1 s sufficient) are indicative, not guaranteed.
- Pre-volume loopback tap on this specific Realtek driver: only indirect evidence (loopback peak 0.986 with master volume 0.62); no controlled volume sweep (the user's volume was deliberately not changed).
- Microsoft documents conflicting minimum builds for process loopback (20348 on the enum/struct pages, 20438 on ActivateAudioInterfaceAsync); it works on build 26200.
- Exclusive-mode render streams being invisible to shared-mode loopback is inferred from the documented mechanism (engine copies its own output), not tested; Edge/Firefox default-device role mapping was only checked in Chromium's source.
- Whether Smart App Control will block cargo-built artifacts (build scripts, proc-macro DLLs, the final exe): observed only with csc-built .NET probes (1 of 10 blocked); no Rust build was attempted.
- Final API of wasapi 0.27.0 and of the post-0.62 rewrite (issue #74) - unreleased; 'Handle becomes Send+Sync' and the #88 SILENT zeroing are stated by the maintainer but not yet in a published version.
- Whether WebView2/wry in Tauri 2.12.5 exposes a hook to deny microphone permission requests (suggested hardening); not verified.
- rubato 1.0.0 was only checked for existence/MSRV, not API.

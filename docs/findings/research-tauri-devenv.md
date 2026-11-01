# Research: Tauri 2 shell, frontend stack and dev environment on Windows

> Produced on 2026-11-01 by the `loopback-architecture` research workflow (Sonnet 5.5 teammates) while the project was still called *interview-whisper*, on the 2-core laptop. Historical input: where it differs from [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) (written after the adversarial review), the architecture wins. Absolute paths were replaced with `<scratch>`, `<repo>` or `%USERPROFILE%`.

**Question investigated:** Tauri 2 shell, Rust-to-UI streaming contract, and dev-environment setup (Windows 11 Home, 2 cores / 7.3 GB, Smart App Control ON) - researched 2026-11-01

## Recommendation

Scaffold with create-tauri-app (npm, template react-ts, non-interactive via -y/--manager/--template/--identifier), then add Tailwind v4 (@tailwindcss/vite), the "@" alias and shadcn init. Keep the template's TypeScript ~6.0.3 rather than the newest 7.0.2. Stream everything high-frequency (level meter, transcript, answer tokens) over tauri::ipc::Channel&lt;T> passed as a command argument. Tauri's docs say events are "not designed for low latency or high throughput" and are always JSON strings. Channels are ordered and are the mechanism Tauri itself uses for downloads and child-process output. Use events only for rare broadcasts. For the meter, send a decimated frame of about 20-30 Hz, not one per 10 ms WASAPI packet (this rate is my judgment, not from the docs). Keep a Mutex&lt;CaptureSlot> in managed state holding a stop flag (Arc&lt;AtomicBool>) and the JoinHandle of a std::thread that owns the WASAPI objects and a clone of the Channel. Stop it from stop_capture, from WindowEvent::CloseRequested, and from RunEvent::ExitRequested/Exit. Errors go through a thiserror enum with a hand-written Serialize in the docs' tag="kind", content="message" form, mirrored by a TS discriminated union. Custom app commands need no permission entries (allowed by default), so capabilities only need core:default now, plus shell/global-shortcut/store entries later. For the API key, use keyring-core 1.0.1 plus windows-native-keyring-store 1.1.0 (Windows Credential Manager) and make the Rust side call Anthropic so the key never reaches JS. Use tauri-plugin-store only for non-secret preferences. Skip Stronghold (it has no deprecation notice, but it needs a password/hash design). Toolchain on this box: winget Rustlang.Rustup (stable-x86_64-pc-windows-msvc, currently 1.99.1), winget Microsoft.VisualStudio.BuildTools with the C++ x64 tools and Windows 11 SDK 26100 only, and winget OpenJS.NodeJS.LTS (26.x). The biggest risk is Smart App Control. rust-lang/rust#160163 (July 2026) shows cargo.exe from an official toolchain blocked with os error 4551 while an older version worked, and rustup-init.exe is still unsigned (rustup#1568). Microsoft says individual apps cannot be exempted, and the supported options are signing or turning SAC off in Windows Security (re-enabling is allowed on recent Windows updates). My recommendation is to turn SAC off on this dev/runtime machine before installing the toolchain. Failing that, expect to reinstall or pin toolchains when blocks appear, and keep the default MSVC link.exe (Microsoft-signed) instead of rust-lld. Build tuning: [profile.dev] debug = "line-tables-only", incremental on, build jobs = 2 (1 if the machine swaps), and plan for a very slow first build (my estimate is 15-40 min, not measured).

## Versions checked

| Name | Version | Checked at |
|---|---|---|
| tauri (crate, stable) | 2.12.5 (rust_version 1.90; deps windows ^0.62, webview2-com ^0.39) | https://crates.io/api/v1/crates/tauri |
| tauri-build | 2.7.4 | https://crates.io/api/v1/crates/tauri-build |
| @tauri-apps/api | 2.12.4 | https://registry.npmjs.org/@tauri-apps/api/latest |
| @tauri-apps/cli | 2.12.4 | https://registry.npmjs.org/@tauri-apps/cli/latest |
| create-tauri-app (npm) | 4.7.5 | https://registry.npmjs.org/create-tauri-app/latest |
| tauri-plugin-shell / @tauri-apps/plugin-shell | 2.6.0 / 2.6.0 | https://crates.io/api/v1/crates/tauri-plugin-shell |
| tauri-plugin-global-shortcut / JS | 2.4.1 / 2.4.1 (crates.io also shows 3.0.0-alpha.3 prereleases; stay on ^2) | https://crates.io/api/v1/crates/tauri-plugin-global-shortcut |
| tauri-plugin-store / JS | 2.5.0 / 2.5.0 (3.0.0-alpha.3 prerelease exists) | https://crates.io/api/v1/crates/tauri-plugin-store |
| tauri-plugin-stronghold | 2.4.0 (3.0.0-alpha.3 prerelease exists; requires Rust 1.90) | https://crates.io/api/v1/crates/tauri-plugin-stronghold |
| tauri-plugin-opener (in template) | 2.7.2 | https://crates.io/api/v1/crates/tauri-plugin-opener |
| keyring (wrapper crate) | 4.2.0 (rust_version 1.88; docs say apps should use keyring-core + a store crate) | https://docs.rs/keyring/latest/keyring/ |
| keyring-core | 1.0.1 | https://crates.io/api/v1/crates/keyring-core |
| windows-native-keyring-store | 1.1.0 | https://docs.rs/windows-native-keyring-store/latest/windows_native_keyring_store/ |
| thiserror | 2.0.21 | https://crates.io/api/v1/crates/thiserror |
| serde | 1.0.229 | https://crates.io/api/v1/crates/serde |
| tailwindcss / @tailwindcss/vite | 4.3.3 / 4.3.3 (peer vite ^5.2\|\|^6\|\|^7\|\|^8) | https://registry.npmjs.org/@tailwindcss/vite/latest |
| shadcn (CLI) | 4.23.0 (node >=20.18.1) | https://registry.npmjs.org/shadcn/latest |
| lucide-react | 1.66.0 (peer react up to ^19) | https://registry.npmjs.org/lucide-react/latest |
| vite | 8.3.5 (node ^20.19.0 \|\| >=22.12.0) | https://registry.npmjs.org/vite/latest |
| @vitejs/plugin-react | 6.1.2 (requires vite ^8) | https://registry.npmjs.org/@vitejs/plugin-react/latest |
| react | 19.3.1 (template pins ^19.1.0) | https://registry.npmjs.org/react/latest |
| typescript | npm latest 7.0.2, but the create-tauri-app template pins ~6.0.3 | https://registry.npmjs.org/typescript/latest |
| @types/node | 26.6.6 (pin to the installed Node major) | https://registry.npmjs.org/@types/node/latest |
| Rust stable | 1.99.1 (2026-10-13) | https://static.rust-lang.org/dist/channel-rust-stable.toml |
| rustup (winget Rustlang.Rustup) | 1.29.1 (latest manifest) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/r/Rustlang/Rustup |
| Node.js LTS | v26.13.0 'Lithium' (2026-10-28) is newest LTS; v24.22.2 'Krypton', v22.23.4 'Jod' also LTS; v27 alpha is not LTS | https://nodejs.org/dist/index.json |
| Visual Studio Build Tools (winget Microsoft.VisualStudio.BuildTools) | 18.x (VS 2026); listing shows 18.11.2; Microsoft.VisualStudio.2022.BuildTools latest seen 17.14.44 | https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/m/Microsoft/VisualStudio/BuildTools |

## Verified facts

- create-tauri-app supports non-interactive flags: -m/--manager, -t/--template, --identifier, -y/--yes (skip prompts), -f/--force, --tauri-version [1|2] (default 2), positional PROJECTNAME ('.' = current dir). Template presets include react-ts. With npm 7+ an extra '--' is needed. PowerShell variant: $env:CTA_ARGS="--template react-ts"; irm https://create.tauri.app/ps | iex. The docs page itself documents no flags; the README and args.rs do.
  - source: https://raw.githubusercontent.com/tauri-apps/create-tauri-app/dev/README.md and https://raw.githubusercontent.com/tauri-apps/create-tauri-app/dev/src/args.rs
- The react-ts template: package.json has react ^19.1.0, @tauri-apps/api ^2, @tauri-apps/plugin-opener ^2, devDeps vite ^8.0.16, @vitejs/plugin-react ^6.0.2, typescript ~6.0.3, @tauri-apps/cli ^2, scripts dev/build/preview/tauri. It has a single tsconfig.json (no tsconfig.app.json) plus tsconfig.node.json. vite.config.ts uses port 1420 strictPort, clearScreen false, TAURI_DEV_HOST, and watch ignore of src-tauri. src-tauri/Cargo.toml: edition 2024, [lib] name &lt;x>_lib with crate-type staticlib/cdylib/rlib, tauri 2, tauri-plugin-opener 2, serde, serde_json, and a release profile with codegen-units=1, lto, opt-level 3, panic abort, strip. lib.rs has `pub fn run()` with Builder::default().plugin(tauri_plugin_opener::init()).invoke_handler(generate_handler![greet]). Default capability: identifier 'default', windows ['main'], permissions core:default and opener:default.
  - source: https://github.com/tauri-apps/create-tauri-app/tree/dev/templates (template-react-ts and _base_/src-tauri, raw files fetched)
- Tailwind v4 with Vite: `npm install tailwindcss @tailwindcss/vite`; add `tailwindcss()` to the vite.config.ts plugins; the CSS file contains `@import "tailwindcss";`.
  - source: https://tailwindcss.com/docs/installation/using-vite
- shadcn/ui for Vite: add the Tailwind plugin; replace src/index.css with @import "tailwindcss"; add baseUrl '.' and paths {'@/*': ['./src/*']} to tsconfig (docs show tsconfig.json and tsconfig.app.json); `npm i -D @types/node`; vite alias '@' -> path.resolve(__dirname, './src'); then `npx shadcn@latest init` and `npx shadcn@latest add button` (docs show the pnpm dlx form). init flags: -t/--template (next|vite|start|react-router|laravel|astro), -b/--base (base|radix|aria), -p/--preset, -y/--yes, -d/--defaults (= --template=next --preset=nova), -f/--force, -c/--cwd, --css-variables. add flags: -y, -o, -a, -c, -p. Icon libs include lucide.
  - source: https://ui.shadcn.com/docs/installation/vite and https://ui.shadcn.com/docs/cli
- Tauri docs: the event system is for small amounts of data, multi-producer/consumer; it is 'not designed for low latency or high throughput'; payloads are always JSON strings; events have no strong typing or capability control and 'directly evaluate JavaScript code'. 'Channels are designed to be fast and deliver ordered data' and are used internally for download progress, child process output and WebSocket messages. The calling-rust page calls channels the recommended way to stream data.
  - source: https://v2.tauri.app/develop/calling-frontend/ and https://v2.tauri.app/develop/calling-rust/
- Channel API (tauri 2.12.5): `pub struct Channel&lt;TSend = InvokeResponseBody>`; `fn send(&self, data: TSend) -> Result&lt;()>` where TSend: IpcResponse (blanket-implemented for any Serialize); `fn id(&self) -> u32`; Clone (clones share one JS callback and are cheap); usable as a command argument (CommandArg); Send/Sync; can be stored in app state or moved to threads. Each message carries an index and the JS side re-orders, so onmessage sees send order. Channel&lt;Vec&lt;u8>> serialises as a JSON array of numbers; Channel&lt;InvokeResponseBody> sends raw bytes (JS gets an ArrayBuffer). When the last clone is dropped the JS callback is unregistered.
  - source: https://docs.rs/tauri/latest/tauri/ipc/struct.Channel.html
- JS side: `import { invoke, Channel } from '@tauri-apps/api/core'; const onEvent = new Channel&lt;T>(); onEvent.onmessage = (m) => ...; await invoke('cmd', { url, onEvent })`. The Rust command takes `on_event: Channel&lt;DownloadEvent>` (the JS key is camelCase onEvent). The enum pattern is #[derive(Clone, Serialize)] #[serde(rename_all="camelCase", rename_all_fields="camelCase", tag="event", content="data")], and the Rust side calls on_event.send(...).
  - source: https://v2.tauri.app/develop/calling-frontend/
- Error pattern in docs: `#[derive(Debug, thiserror::Error)] enum Error { #[error(transparent)] Io(#[from] std::io::Error), ... }` with a manual `impl serde::Serialize`. The structured form serialises through `#[derive(Serialize)] #[serde(tag="kind", content="message")] #[serde(rename_all="camelCase")] enum ErrorKind { Io(String), Utf8(String) }`, so the frontend receives `{ kind: 'io'|'utf8', message: string }`.
  - source: https://raw.githubusercontent.com/tauri-apps/tauri-docs/v2/src/content/docs/develop/calling-rust.mdx
- State: register with Builder::manage or app.manage(Mutex::new(AppState::default())) in setup; access with `State&lt;'_, Mutex&lt;AppState>>` in commands. No Arc is needed. std::sync::Mutex is fine unless the guard is held across an await. In threads, move an AppHandle (cheap to clone) and call `app_handle.state::&lt;Mutex&lt;T>>()` (Manager trait). Non-async commands run on the main thread unless #[tauri::command(async)] or async fn is used; async commands with borrowed args like State must return Result.
  - source: https://v2.tauri.app/develop/state-management/ and https://v2.tauri.app/develop/calling-rust/
- Lifecycle API: `App::run&lt;F: FnMut(&AppHandle&lt;R>, RunEvent) + 'static>(self, callback: F)` never returns (process::exit); `run_return(...) -> i32` returns the exit code; `App::cleanup_before_exit(&self)`. RunEvent variants: Exit, ExitRequested { code: Option&lt;i32>, api: ExitRequestApi } (code None for user-initiated exits), WindowEvent { label, event }, Ready, and others; the enum is #[non_exhaustive]. `ExitRequestApi::prevent_exit(&self)` exists. WindowEvent::CloseRequested { api: CloseRequestApi } and WindowEvent::Destroyed exist (non_exhaustive).
  - source: https://docs.rs/tauri/latest/tauri/struct.App.html, https://docs.rs/tauri/latest/tauri/enum.RunEvent.html, https://docs.rs/tauri/latest/tauri/struct.ExitRequestApi.html, https://docs.rs/tauri/latest/tauri/enum.WindowEvent.html
- Capabilities live in src-tauri/capabilities/*.json (or .toml). Fields are $schema, identifier, description, windows (labels, '*' allowed), permissions, and optional platforms/remote. All files in the directory are enabled by default unless app.security.capabilities in tauri.conf.json lists specific ones. Commands registered via invoke_handler are usable by all windows by default; to restrict them, use tauri_build::AppManifest::new().commands(&[...]) in build.rs.
  - source: https://v2.tauri.app/security/capabilities/
- tauri-plugin-shell sidecar: bundle.externalBin entries are relative to src-tauri (e.g. 'binaries/my-sidecar'); a file named `&lt;name>-&lt;target-triple>[.exe]` must exist (on Windows e.g. my-sidecar-x86_64-pc-windows-msvc.exe, inferred from the docs' .exe example); `rustc --print host-tuple` gives the triple. Rust: `app.shell().sidecar("my-sidecar")` (filename only, not the externalBin path) then `.spawn()` returns (rx, child); events are CommandEvent::Stdout etc. JS: Command.sidecar('binaries/my-sidecar'). Permission: {"identifier":"shell:allow-execute","allow":[{"name":"binaries/my-sidecar","sidecar":true}]} (execute for command.execute(), shell:allow-spawn for spawn). Plugin source: all spawned children are killed on RunEvent::Exit. Default shell permission is only allow-open. The shell plugin's own `open` is deprecated in favour of tauri-plugin-opener.
  - source: https://v2.tauri.app/develop/sidecar/ and https://raw.githubusercontent.com/tauri-apps/plugins-workspace/v2/plugins/shell/src/lib.rs
- Global shortcut: `cargo add tauri-plugin-global-shortcut --target 'cfg(any(target_os = "macos", windows, target_os = "linux"))'` plus JS @tauri-apps/plugin-global-shortcut. Rust: app.handle().plugin(tauri_plugin_global_shortcut::Builder::new().with_handler(|_app, shortcut, event| ...).build())? inside setup under #[cfg(desktop)], then app.global_shortcut().register(shortcut) with GlobalShortcutExt. Permissions are not enabled by default and need global-shortcut:allow-register, allow-unregister, allow-is-registered.
  - source: https://v2.tauri.app/plugin/global-shortcut/
- tauri-plugin-store: `app.store("store.json")` via StoreExt, values must be serde_json::Value, permission 'store:default'. The docs say nothing about secrets, and the file is a plain JSON key-value store.
  - source: https://v2.tauri.app/plugin/store/
- Stronghold plugin: no deprecation notice on the v2 docs page or README. It needs Rust >= 1.90, a hash function returning exactly 32 bytes (Builder::with_argon2(&salt_path) or a custom one), and a recommended Cargo override `[profile.dev.package.scrypt] opt-level = 3` for an upstream bug. Dangerous commands are blocked until enabled in capabilities.
  - source: https://v2.tauri.app/plugin/stronghold/
- The keyring 4.x crate now says applications wanting control over credential stores should depend on keyring-core and specific store crates. keyring-core 1.0.1: `Entry::new(service:&str, user:&str)->Result&lt;Entry>`, `set_password(&self,&str)`, `get_password(&self)->Result&lt;String>`, `delete_credential(&self)`, errors NoEntry/Ambiguous/Invalid/NoDefaultStore/BadEncoding, and `pub fn set_default_store(new: Arc&lt;CredentialStore>)`. windows-native-keyring-store 1.1.0 (Windows Credential Manager, generic credentials, default persistence Enterprise) is installed with `keyring_core::set_default_store(windows_native_keyring_store::Store::new().unwrap())`. Warning: a read right after a write on a different thread may return NoEntry.
  - source: https://docs.rs/keyring/latest/keyring/, https://docs.rs/keyring-core/latest/keyring_core/struct.Entry.html, https://docs.rs/windows-native-keyring-store/latest/windows_native_keyring_store/
- Tauri prerequisites on Windows: Microsoft C++ Build Tools with the 'Desktop development with C++' workload; WebView2 is preinstalled on Windows 10 1803+ and Windows 11; Rust via `winget install --id Rustlang.Rustup`, then select/ensure the MSVC host (`rustup default stable-msvc`); Node.js LTS only for a JS frontend (corepack enable optional); VBSCRIPT is needed only for MSI bundling.
  - source: https://v2.tauri.app/start/prerequisites/
- Component IDs: Microsoft.VisualStudio.Workload.VCTools, Microsoft.VisualStudio.Component.VC.Tools.x86.x64 and Microsoft.VisualStudio.Component.Windows11SDK.26100 (marked Recommended; 10.0.26100.6901 in the VS 18 table) are valid in both the 17.x and 18.x Build Tools catalogs. The rustup book documents the minimal pair `--add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.22621 --addProductLang En-us` via winget `--override`, says 'replace Community with BuildTools', states Rust supports VS 2017+ including 2022 and 2026, and says rustup-init can auto-install the prerequisites. The winget BuildTools manifest silent switch is --quiet and UAC is required.
  - source: https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools and https://raw.githubusercontent.com/rust-lang/rustup/master/doc/user-guide/src/installation/windows-msvc.md and the winget-pkgs manifest 18.9.3
- Smart App Control (SAC) evidence. (1) Microsoft: SAC first asks the cloud reputation service; if that is not confident, it allows only validly signed code; unsigned or invalid-signature apps are untrusted; there is no per-app bypass; SAC can be turned off in Windows Security, and recent Windows updates allow turning it back on without a clean install; evaluation mode may auto-disable it for developers. (2) rust-lang/rust#160163 (open, July 2026): on Windows 11 with SAC On, cargo.exe from Rust 1.97.1 failed with 'Application Control policy has blocked this file. (os error 4551)' while rustup.exe and rustc.exe worked and 1.96.0 worked; maintainers say nothing can be done on the Rust side, and commenters report that blocks appear after days, even on proc-macros run by rust-analyzer, and that reinstalling a toolchain via rustup temporarily fixes it. (3) rust-lang/rustup#1568: rustup-init.exe is still not code-signed (issue open).
  - source: https://support.microsoft.com/en-US/Windows/Security/Threat-Malware-Protection/smart-app-control-frequently-asked-questions, https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview, https://github.com/rust-lang/rust/issues/160163, https://github.com/rust-lang/rustup/issues/1568
- Cargo profile facts: dev defaults are opt-level 0, debug true (full), incremental true, codegen-units 256; debug accepts false/0/'none', 'line-tables-only', 'limited'/1, 'full'/2; build scripts and proc macros use [profile.dev.build-override] (opt-level 0, debug false). Rust 1.90 made rust-lld the default only for x86_64-unknown-linux-gnu (with a 7x link-time gain reported there); the blog mentions neither Windows nor MSVC. lld-link on MSVC needs matching LLVM versions, and a linker set in the cargo config can break crates that run nested cargo invocations (rustc linker-plugin-lto page).
  - source: https://doc.rust-lang.org/cargo/reference/profiles.html, https://blog.rust-lang.org/2025/09/01/rust-lld-on-1.90.0-stable, https://doc.rust-lang.org/rustc/linker-plugin-lto.html
- Tauri's Vite guide settings: devUrl, frontendDist '../dist', beforeDevCommand/beforeBuildCommand, and vite clearScreen false, strictPort true, TAURI_DEV_HOST-based host/hmr (ws, port 1421), and watch.ignored '**/src-tauri/**'. The CTA template uses port 1420.
  - source: https://v2.tauri.app/start/frontend/vite/ and the CTA template vite.config.ts.lte

## API sketch (research-time, superseded by ARCHITECTURE.md where they differ)

```text
=== 0. TOOLCHAIN (elevated PowerShell; reboot or new terminal afterwards) ===
# 0a. FIRST decide on Smart App Control: Settings > Privacy & security > Windows Security > App & browser control > Smart App Control > Off (recommended for this dev box; MS says it can be re-enabled on recent Windows updates)
winget install --id Microsoft.VisualStudio.BuildTools --source winget --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --addProductLang En-us"
#   fallback if 2026 (18.x) misbehaves: --id Microsoft.VisualStudio.2022.BuildTools (same --override)
winget install --id Rustlang.Rustup --source winget
rustup default stable-x86_64-pc-windows-msvc     # or: rustup-init -y --default-host x86_64-pc-windows-msvc --default-toolchain stable --profile minimal
winget install --id OpenJS.NodeJS.LTS --source winget   # Node 26.x; Vite 8 needs ^20.19 || >=22.12
# verify: rustc -Vv ; cargo -V ; node -v ; npm -v ; cargo works under SAC (no os error 4551)

=== 1. SCAFFOLD (do this in a scratch dir, then copy; npm 7+ needs the extra --) ===
npm create tauri-app@latest iw-app -- --manager npm --template react-ts --identifier com.interviewwhisper.app --yes
cd iw-app
npm i tailwindcss @tailwindcss/vite lucide-react
npm i -D @types/node
# src/index.css (or App.css) first line: @import "tailwindcss";
# vite.config.ts: import tailwindcss from "@tailwindcss/vite"; plugins: [react(), tailwindcss()],
#   resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } }   (the shadcn doc uses path.resolve(__dirname,"./src"))
# tsconfig.json compilerOptions (template has ONE tsconfig.json): "baseUrl": ".", "paths": { "@/*": ["./src/*"] }
npx shadcn@latest init -t vite -y      # components.json + src/lib/utils.ts; then: npx shadcn@latest add button card badge select switch scroll-area -y
# keep typescript at the template's ~6.0.3 (npm latest 7.0.2 not validated against shadcn/vite 8)

=== 2. src-tauri/Cargo.toml additions ===
# [dependencies] thiserror = "2"  serde = {version="1",features=["derive"]}  serde_json = "1"
# later: tauri-plugin-shell = "2"  tauri-plugin-global-shortcut = "2" (cfg desktop)  tauri-plugin-store = "2"  keyring-core = "1"  windows-native-keyring-store = "1"
# [profile.dev]  debug = "line-tables-only"  incremental = true   # codegen-units stays 256
# [profile.dev.build-override]  opt-level = 0  debug = false
# src-tauri/.cargo/config.toml:  [build] jobs = 2      (use 1 if the box swaps; do NOT set a linker initially)

=== 3. RUST: error.rs (docs pattern) ===
#[derive(Debug, thiserror::Error)]
pub enum CaptureError {
    #[error("output device not found: {0}")]            DeviceNotFound(String),
    #[error("loopback unavailable on output device: {0}")] LoopbackUnavailable(String), // NEVER fall back to a mic
    #[error("capture already running")]                  AlreadyRunning,
    #[error("capture not running")]                      NotRunning,
    #[error("audio engine error: {0}")]                  Engine(String),
}
#[derive(serde::Serialize)]
#[serde(tag = "kind", content = "message", rename_all = "camelCase")]
enum CaptureErrorKind { DeviceNotFound(String), LoopbackUnavailable(String), AlreadyRunning(String), NotRunning(String), Engine(String) }
impl serde::Serialize for CaptureError {
    fn serialize<S: serde::Serializer>(&self, s: S) -> Result<S::Ok, S::Error> {
        let m = self.to_string();
        let k = match self { Self::DeviceNotFound(_) => CaptureErrorKind::DeviceNotFound(m), Self::LoopbackUnavailable(_) => CaptureErrorKind::LoopbackUnavailable(m),
            Self::AlreadyRunning => CaptureErrorKind::AlreadyRunning(m), Self::NotRunning => CaptureErrorKind::NotRunning(m), Self::Engine(_) => CaptureErrorKind::Engine(m) };
        k.serialize(s)
    }
}

=== 4. RUST: Channel event contract + commands + lifecycle (lib.rs) ===
use std::sync::{Mutex, Arc, atomic::{AtomicBool, Ordering}};
use tauri::{ipc::Channel, State, Manager, RunEvent, WindowEvent};

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase", rename_all_fields = "camelCase", tag = "event", content = "data")]
pub enum CaptureEvent {
    Started { device_id: String, device_name: String, sample_rate: u32, channels: u16 },
    Level { rms_db: f32, peak_db: f32, ts_ms: u64 },          // send at ~20-30 Hz, not per 10 ms packet
    Silence { ts_ms: u64 },                                   // optional
    DeviceChanged { device_id: String, device_name: String }, // default endpoint switched (headphones/BT)
    Error { code: String, message: String },                  // thread-originated failure
    Stopped { reason: String },
}

#[derive(serde::Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct OutputDevice { id: String, name: String, is_default: bool, state: String }

#[derive(Default)]
struct CaptureSlot { stop: Option<Arc<AtomicBool>>, join: Option<std::thread::JoinHandle<()>>, device_id: Option<String> }
type SharedCapture = Mutex<CaptureSlot>;

#[tauri::command] fn list_output_devices() -> Result<Vec<OutputDevice>, CaptureError> { /* IMMDeviceEnumerator, eRender, DEVICE_STATE_ACTIVE ONLY; never eCapture */ todo!() }

#[tauri::command]
fn start_capture(device: String /* endpoint id or "default" */, on_event: Channel<CaptureEvent>, state: State<'_, SharedCapture>) -> Result<(), CaptureError> {
    let mut slot = state.lock().unwrap();
    if slot.stop.is_some() { return Err(CaptureError::AlreadyRunning); }
    let stop = Arc::new(AtomicBool::new(false));
    let (stop2, ch) = (stop.clone(), on_event.clone());          // Channel is Clone + Send + Sync
    let join = std::thread::Builder::new().name("wasapi-loopback".into()).spawn(move || {
        // CoInitializeEx on THIS thread; open the render endpoint in shared mode with AUDCLNT_STREAMFLAGS_LOOPBACK; on any failure ch.send(CaptureEvent::Error{..}) and exit (no mic fallback)
        // loop { poll ~10 ms; downmix/resample to 16 kHz mono i16 for STT (hand off via a bounded std::sync::mpsc / crossbeam channel); accumulate RMS/peak; every ~40 ms: let _ = ch.send(CaptureEvent::Level{..}) }
        // while !stop2.load(Ordering::Relaxed)
        let _ = ch.send(CaptureEvent::Stopped { reason: "stopped".into() });
    }).map_err(|e| CaptureError::Engine(e.to_string()))?;
    slot.stop = Some(stop); slot.join = Some(join); slot.device_id = Some(device);
    Ok(())
}
#[tauri::command] fn stop_capture(state: State<'_, SharedCapture>) -> Result<(), CaptureError> { shutdown(&state) }
#[tauri::command] fn get_capture_status(state: State<'_, SharedCapture>) -> CaptureStatus { /* running?, device id */ todo!() }
fn shutdown(state: &SharedCapture) -> Result<(), CaptureError> {
    let (stop, join) = { let mut s = state.lock().unwrap(); (s.stop.take(), s.join.take()) }; // do NOT hold the lock while joining
    let stop = stop.ok_or(CaptureError::NotRunning)?; stop.store(true, Ordering::Relaxed);
    if let Some(j) = join { let _ = j.join(); } Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(SharedCapture::default())                          // Tauri wraps in Arc itself
        .on_window_event(|w, e| if let WindowEvent::CloseRequested { .. } = e { let _ = shutdown(&w.app_handle().state::<SharedCapture>()); })
        .invoke_handler(tauri::generate_handler![list_output_devices, start_capture, stop_capture, get_capture_status])
        .build(tauri::generate_context!()).expect("build")
        .run(|app, e| match e {
            RunEvent::ExitRequested { .. } | RunEvent::Exit => { let _ = shutdown(&app.state::<SharedCapture>()); }
            _ => {}
        });
}

=== 5. TS side (src/lib/ipc.ts) ===
import { invoke, Channel } from "@tauri-apps/api/core";
export type CaptureEvent =
  | { event: "started"; data: { deviceId: string; deviceName: string; sampleRate: number; channels: number } }
  | { event: "level"; data: { rmsDb: number; peakDb: number; tsMs: number } }
  | { event: "silence"; data: { tsMs: number } }
  | { event: "deviceChanged"; data: { deviceId: string; deviceName: string } }
  | { event: "error"; data: { code: string; message: string } }
  | { event: "stopped"; data: { reason: string } };
export type IpcError =
  | { kind: "deviceNotFound" | "loopbackUnavailable" | "alreadyRunning" | "notRunning" | "engine"; message: string };
export const isIpcError = (e: unknown): e is IpcError => typeof e === "object" && e !== null && "kind" in e && "message" in e;
export async function startCapture(device: string, onEvent: (e: CaptureEvent) => void) {
  const ch = new Channel<CaptureEvent>(); ch.onmessage = onEvent;     // ordered delivery guaranteed
  try { await invoke("start_capture", { device, onEvent: ch }); } catch (e) { throw isIpcError(e) ? e : { kind: "engine", message: String(e) }; }
}
export const listOutputDevices = () => invoke<{ id: string; name: string; isDefault: boolean; state: string }[]>("list_output_devices");
export const stopCapture = () => invoke<void>("stop_capture");
// Keep the Rust command-name -> rename mapping: Tauri converts snake_case Rust args (on_event) to camelCase JS keys (onEvent).

=== 6. CAPABILITIES src-tauri/capabilities/default.json (Phase 2: custom commands need no entries) ===
{ "$schema": "../gen/schemas/desktop-schema.json", "identifier": "default", "description": "main window", "windows": ["main"], "permissions": ["core:default"] }
// Later additions: "shell:allow-spawn" + {"identifier":"shell:allow-spawn","allow":[{"name":"binaries/whisper-worker","sidecar":true}]}, "global-shortcut:allow-register","global-shortcut:allow-unregister","global-shortcut:allow-is-registered", "store:default". Remove opener:default if tauri-plugin-opener is removed from lib.rs.
// tauri.conf.json: build.devUrl "http://localhost:1420", beforeDevCommand "npm run dev", beforeBuildCommand "npm run build", frontendDist "../dist"; bundle.externalBin ["binaries/whisper-worker"] (file whisper-worker-x86_64-pc-windows-msvc.exe, from `rustc --print host-tuple`)

=== 7. API KEY (Phase 4) ===
keyring_core::set_default_store(windows_native_keyring_store::Store::new()?);  // once in setup; verify the exact return type at compile time (docs show .unwrap() passed directly)
let e = keyring_core::Entry::new("interview-whisper", "anthropic-api-key")?; e.set_password(&key)?; /* get_password() only inside Rust; commands: set_api_key, has_api_key, clear_api_key; never return the key to JS */
```

## Alternatives considered

| Option | Verdict |
|---|---|
| Emitter::emit / listen events for the level meter and transcript | Rejected for streaming. Tauri docs say events are not designed for low latency or high throughput, are JSON-string only, and evaluate JavaScript directly. Keep events for rare, multi-window broadcasts such as a device-hotplug notice; channels cover everything per-session. |
| Tauri v2 Channel&lt;T> as command argument (chosen) | Selected. It is ordered, typed with a tagged serde enum, cheap to clone into the capture thread, and auto-unregistered when dropped. A separate Channel per stream (capture, transcript, answer tokens) keeps the contracts small. |
| Binary Channel&lt;InvokeResponseBody> for PCM to the UI | Not needed. The UI only needs dB levels (the 16 kHz PCM stays in Rust and goes to the Whisper worker), so a JSON Level frame at 20-30 Hz is trivial. Use a raw-bytes channel only if a waveform view is added later. |
| tauri-plugin-stronghold for the API key | Rejected for now. No deprecation notice was found, but it needs a 32-byte password hash scheme and a user password, a salt file, and a scrypt opt-level override for dev builds. It is heavier for a single-user local app than Credential Manager. |
| tauri-plugin-store for the API key | Rejected for secrets (plain JSON on disk). Fine for non-secret preferences such as selected device id, model, sidebar state. |
| keyring 4.2.0 crate directly | Its own docs now tell applications to use keyring-core plus a store crate. Use keyring-core 1.0.1 and windows-native-keyring-store 1.1.0. The v1 feature mode of the wrapper exists but was not evaluated. |
| Store the API key in env var or .env | Rejected: plaintext on disk and easy to leak. Optionally allow an ANTHROPIC_API_KEY env var for dev only. |
| Microsoft.VisualStudio.2022.BuildTools (17.14.x) instead of Microsoft.VisualStudio.BuildTools (18.x / VS 2026) | Both exist in winget and Rust supports both. 18.x is the current default ID, so it is chosen first. Keep 2022 as the fallback if a build-script or cc-crate problem appears. |
| rust-lld / lld-link to speed up linking | Deferred. Rust made lld default only for Linux x86_64 and I found no MSVC claims or benchmarks. A rustup-shipped unsigned linker is also another SAC block risk, while link.exe is Microsoft-signed. Revisit only if linking dominates incremental builds, and measure. |
| Create-vite then manual tauri init instead of create-tauri-app | Not necessary. create-tauri-app has non-interactive flags and a Tauri-correct vite.config. Manual init only if the template's pinned versions conflict. |
| pnpm instead of npm | Optional. pnpm needs a corepack or extra install step and gives disk and speed benefits, but it adds one more thing to unblock under SAC. npm is the zero-extra-install path. |
| Keep SAC on and work around blocks (reinstall or pin toolchains, sign things) | Fragile. Evidence says blocks are reputation-based and can appear later for any new unsigned binary, including proc-macro and build-script outputs. There is no per-app exemption. Turn SAC off on this machine, or accept recurring interruptions. |

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Smart App Control blocks cargo.exe / rustc / rustup-init.exe / rust-analyzer-launched proc-macros / build-script exes / the Tauri dev exe (os error 4551, 'Application Control policy has blocked this file'), possibly days after a toolchain works. rustup-init.exe is unsigned (rustup#1568). cargo.exe 1.97.1 was blocked while 1.96.0 was not (rust#160163). | high | Turn SAC off in Windows Security before installing Rust (Microsoft documents this and says it can be re-enabled on recent Windows updates). If keeping it on: use winget Rustlang.Rustup, check `cargo -V` right after install, and on a block reinstall the toolchain (`rustup toolchain install stable --force`) or pin a known-good version, and set CARGO_HOME/target outside any MOTW-tagged location. Check Event Viewer > Code Integrity to see which file was refused. Home edition cannot use WDAC supplemental policies (unverified). |
| Very slow and memory-starved first build: 2 cores / 7.3 GB with only ~0.3-0.7 GB free; tauri + windows 0.62 + webview2-com + tokio etc. is a large dependency graph, so OOM or heavy pagefile thrash is possible, and compile contention with the browser/call at runtime. | high | Close the browser and other apps during first builds. Set `[build] jobs = 2` (or 1 on swapping), debug = "line-tables-only", keep incremental on, avoid `cargo clean`, put CARGO_TARGET_DIR on the SSD with ~10+ GB free. Build release only when needed (release uses codegen-units=1 + lto in the template, which is very slow and memory hungry on this machine; consider lto="thin" or false for local release testing). Add Defender exclusions for target/, node_modules/ and .cargo (commonly helps, not measured here). |
| Vite 8 / @vitejs/plugin-react 6 / Tailwind 4 / shadcn CLI use native binaries (rolldown/oxc/lightningcss-style .node/.dll files) from npm, which SAC could refuse to load; no primary source found on this. | medium | If `npm run dev` fails with an Application Control error, turn SAC off. As a last resort pin Vite 7 + @vitejs/plugin-react 5 (esbuild-based), which Tailwind's plugin still supports (peer ^5.2\|\|^6\|\|^7\|\|^8). |
| CPU/RAM contention at runtime: a Tauri/WebView2 UI, WASAPI capture, faster-whisper and a browser call all share 2 cores. An over-chatty IPC (per-10 ms packet) wastes CPU. | medium | Decimate Level frames to 20-30 Hz, batch transcript updates, keep React updates cheap (single store, no re-render of the transcript on every level frame), keep the audio thread free of locks/allocations, and send PCM to the worker on a bounded queue (drop-oldest policy) rather than through the WebView. |
| Capture-thread lifecycle bugs: dropping the Channel or holding the state mutex across join; a blocked WASAPI wait delaying shutdown; zombie sidecar after crash. | medium | Stop flag plus short polling timeouts (~10 ms) so join returns quickly; never hold the Mutex while joining; call shutdown from CloseRequested and ExitRequested/Exit; tauri-plugin-shell already kills spawned children on RunEvent::Exit, but also kill explicitly and have the Python worker exit on stdin EOF. |
| Windows output-device change at runtime (headphones plugged in or BT headset connected changes the default render endpoint): a loopback stream bound to the old endpoint goes silent or errors (AUDCLNT_E_DEVICE_INVALIDATED). | medium | Surface it explicitly through CaptureEvent::Error/DeviceChanged and let the UI re-list devices (refresh button) and restart; never auto-substitute an input device. The audio-engine research topic owns the IMMNotificationClient details. |
| Version drift: Tauri plugin v3 alphas, TypeScript 7.0.2, React 19.3, shadcn 'nova' preset/base options are new and may not match older tutorials. | low | Pin exact versions in package.json/Cargo.lock after scaffolding, stay on plugin ^2 and the template's TypeScript ~6.0.3, and check `components.json` after `shadcn init -t vite` (the vite template flag is documented). |
| Visual Studio Build Tools licensing/disk: the installer is large and subject to Microsoft's license terms (rustup docs say BuildTools requires you to already hold a suitable license); needs admin/UAC. | low | Review the Build Tools license for the intended (personal/interview) use before installing; install only the two components to limit disk use; keep ~30 GB free headroom. |

## Not verified at research time

- Expected first-build time and memory use: no primary source. My estimate is 15-40 min for the first `tauri dev` on this 2-core Zen 2 box with heavy memory pressure, 1-5 min for incremental rebuilds. Not measured.
- Whether SAC blocks esbuild/rollup/rolldown/oxc/lightningcss native binaries, cargo build-script executables and proc-macro DLLs compiled locally, or the Tauri dev binary on this machine. I found only the cargo.exe report (rust#160163) and anecdotes; the user's single .NET exe test passed. A precise 'known blocked' list for esbuild/rollup/Tauri was not found.
- The exact registry value for checking SAC state (VerifiedAndReputablePolicyState under HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy; 0 off, 1 enforce, 2 evaluation) is from memory and was not fetched. The documented check is Windows Security > App & browser control.
- Whether Microsoft.VisualStudio.BuildTools 18.x (VS 2026) or 2022 (17.14.x) is more trouble-free with Tauri 2.12 / cc-crate / WebView2 loader static linking. Rust docs say 2026 is supported; I did not test either. The winget listing order is not strictly semver, so '18.11.2' and '17.14.44' as latest are inferred from directory listings.
- Whether Windows 11 SDK 26100 vs 22621 matters for Tauri 2.12 builds (both IDs are valid; the rustup book uses 22621, Microsoft marks 26100 Recommended). Disk size of the Build Tools install (likely ~5-8 GB) is unverified.
- Whether Node 26.13.0 'Lithium' is fully supported by all npm packages used (Vite 8 engines allow it; shadcn >=20.18.1). Node 24.22.2 is the conservative alternative, but I did not check that winget carries 24.22.2 (OpenJS.NodeJS.LTS listing in winget-pkgs ends at 26.13.0 and 24.22.1).
- The exact return type of windows_native_keyring_store::store::Store::new() versus keyring_core::set_default_store(Arc&lt;CredentialStore>) (the docs example passes Store::new().unwrap() directly); confirm at compile time. The Credential Manager blob limit (~2560 bytes) is general Windows knowledge, not confirmed on the fetched pages; Anthropic API keys are far smaller anyway.
- The claim that `snake_case` Rust command args become camelCase JS keys (on_event -> onEvent) comes from the docs' DownloadEvent example (`onEvent`) and Tauri convention, but the rename-rule page was not fetched.
- Recommended Level-frame rate of 20-30 Hz, and ' &lt;=~ few KB per channel message' sizing: my engineering judgement; the docs give no numeric payload or rate limits.
- rust-lld as `linker = "rust-lld.exe"` under [target.x86_64-pc-windows-msvc]: not tested here, and no official statement or benchmark for the MSVC target was found. Whether the Windows split-debuginfo default is 'packed' was not stated on the Cargo page.
- Whether turning on Windows Developer Mode changes SAC state (Microsoft's FAQ says SAC may stay off in developer-mode/enterprise-managed configurations, but no procedure is given).
- create-tauri-app interplay of --identifier with --yes was read from args.rs but the command was not executed (Node is not installed), so the exact non-interactive run has not been executed end to end; the shadcn init -t vite -y flow also was not executed.
- Whether Tauri's `tauri-build` generates a default capability schema path ../gen/schemas/desktop-schema.json only after the first build (as the docs' $schema suggests); harmless if missing.

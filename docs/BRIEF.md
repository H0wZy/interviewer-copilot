# TASK: AI Interview Copilot (Real-Time Audio Loopback + Faster-Whisper + Claude API)

> **Public copy (2026-11-01):** this repository is public, so the employer, the internship dates and the named assessment platform were redacted (marked `[redacted]`). Everything else is verbatim, including the Phase 1 registry command with its wrong property GUID (fixed in `tools/diagnostics/audio-probe.ps1`) and the retired `claude-3-5-sonnet`. Marcos types the full persona into the app (`docs/SPEC.md` FR-L4); it is never committed.

## 1. Core Objective & Non-Negotiable Requirements
Build a lightweight desktop application with Tauri 2 (Rust) and React/TypeScript. The application serves as an interview transcription and answer-generation helper for technical assessments (e.g., [redacted]).

### CRITICAL RULES:
1. **AUDIO LOOPBACK ONLY**: Capture audio coming OUT of Windows (interviewer voice via speakers/Bluetooth/headset) using Windows WASAPI Loopback Capture. Do NOT capture from the microphone.
2. **NO MOCK/FALLBACK TO MIC**: If loopback fails or output device is unavailable, throw an explicit error. Never switch silently to input/microphone.
3. **STEP-BY-STEP EXECUTION**: Follow the phased implementation plan. Never jump to Whisper or Anthropic integration before proving that WASAPI loopback captures audio chunks properly.

---

## 2. Technical Stack
- **Frontend**: React 18+, TypeScript, Vite, Tailwind CSS, Lucide React, shadcn/ui.
- **Desktop Core**: Tauri 2 (Rust backend).
- **Audio Capture**: Rust native Windows WASAPI loopback (`windows-rs` / `wasapi` or a dedicated low-level loopback thread, converting streams to PCM mono 16kHz).
- **Speech-to-Text**: Local `faster-whisper` (Python sidecar or local microservice communicating with Tauri via IPC/localhost WebSocket/stdin).
- **LLM Engine**: Anthropic Messages API (`claude-3-5-sonnet` or configured model) with token streaming.

---

## 3. Candidate Context & System Persona
When generating answers, the AI must align with the candidate profile:
- **Name**: Marcos | **Location**: Brazil.
- **Background**: Technologist in Systems Analysis & Development (graduation: June 2026).
- **Experience**: Trainee at [redacted] ([redacted]) working with Scrum, APIs, and collaborative development.
- **Stack**: C#, .NET, ASP.NET Core, TypeScript, React, Next.js, Python, SQL Server, PostgreSQL, Docker, Git.
- **Constraint**: Never fabricate seniority or production ownership. Keep answers conversational, technically accurate, concise, and structured:
  - `QUESTION`: The detected question.
  - `INTENT`: Evaluation intent.
  - `SUGGESTED ANSWER`: Concise, natural response in English.
  - `KEY CONCEPTS`: Core technical topics.
  - `LEARNING NOTES`: Quick explanation in Portuguese.

---

## 4. Execution Plan for Claude Code

### Phase 1 — Environment & Device Diagnostics (DO THIS FIRST)
1. Inspect the repository and development environment.
2. Check if running on Windows.
3. Run the following PowerShell command to list registered audio endpoints (Render vs Capture):

```powershell
$base = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio"
$devices = foreach ($type in @("Render", "Capture")) {
    $path = Join-Path $base $type
    Get-ChildItem -Path $path -ErrorAction SilentlyContinue | ForEach-Object {
        $device = $_
        $propertiesPath = Join-Path $device.PSPath "Properties"
        $properties = Get-ItemProperty -Path $propertiesPath -ErrorAction SilentlyContinue
        $friendlyNameProperty = $properties.PSObject.Properties |
            Where-Object { $_.Name -eq "{a45c254e-df1c-4ed4-a1e2-6d63d6a303f2},2" } |
            Select-Object -First 1
        [PSCustomObject]@{
            Type = if ($type -eq "Render") { "Output (Speakers/Headphones)" } else { "Input (Microphone)" }
            Name = if ($friendlyNameProperty) { $friendlyNameProperty.Value } else { "Unknown" }
            EndpointId = $device.PSChildName
        }
    }
}
$devices | Format-Table -AutoSize
```

4. Confirm target default output device (e.g., `Speakers (Realtek(R) Audio)` or connected Bluetooth headphones).

### Phase 2 — Tauri 2 App Shell & WASAPI Loopback Engine (GATEWAY)
1. Scaffold the Tauri 2 + Vite + React + Tailwind app.
2. Implement audio device enumeration in Rust using Core Audio APIs (`IMMDeviceEnumerator`, `eRender`, `eConsole`).
3. Implement WASAPI Loopback capture (`AUDCLNT_STREAMFLAGS_LOOPBACK`):
   - Capture live output audio from the user-selected Render endpoint.
   - Resample and downmix captured audio to 16,000 Hz, 16-bit Mono PCM.
   - Stream real-time peak volume levels (RMS/dB) to the frontend to visually verify audio input is active when desktop sound plays.
4. Add frontend controls: Output Device Selector dropdown, Refresh Devices button, and live Audio Level Meter.
5. **STOP AND VERIFY**: Confirm that playing desktop audio (e.g., YouTube/media player) registers on the level meter before moving to Phase 3.

### Phase 3 — faster-whisper Integration
1. Set up a local Python worker or sidecar embedding `faster-whisper` (model: `base.en` or `small.en`).
2. Implement silence detection (VAD / energy thresholding) and send audio segments (3-5 second sliding windows or silence-delimited chunks) from Rust to the Whisper worker.
3. Transcribe and stream text segments to the frontend with timestamps.
4. Implement question boundary detection (detecting interrogative patterns, tone/punctuation, or brief silence after complete utterances).

### Phase 4 — Anthropic Streaming & UI Interface
1. Set up Anthropic client using secure API key storage.
2. When a question is detected (or manually triggered via Enter/Hotkey):
   - Send question + conversation context + candidate profile to Claude.
   - Stream the response into the UI.
3. UI Layout (Dark Minimalist / Raycast-Linear aesthetic):
   - **Top bar**: Device status, capture indicator, Whisper engine status, model selector.
   - **Left / Collapsible sidebar**: Live transcript feed.
   - **Main view**: Active question card, streaming suggested response, key concepts, and quick PT-BR cheat sheet.
   - **Footer**: Audio device switch, start/stop listening toggle, manual text input for fallback/override.

---

## 5. Immediate Next Step
Acknowledge these instructions, verify the OS environment, execute the audio diagnostics PowerShell command, and present the proposed Rust audio capture architecture. Wait for confirmation before writing the full application code.
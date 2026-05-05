# SoundToText

**Local, real-time speech-to-text dictation for Windows.** Press a hotkey, talk, and your words type into whatever app is focused. Phrase-by-phrase chunking via silence detection. Powered by Whisper running on your CPU. No cloud, no telemetry, no account.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4.svg)](#requirements)
[![Whisper.net](https://img.shields.io/badge/Whisper.net-1.7.4-brightgreen.svg)](https://github.com/sandrohanea/whisper.net)

---

## What it does

Click into any text field, hit `Ctrl+Shift+Alt+Space`, and start talking. Every time you pause for more than half a second, the chunk you just spoke gets transcribed and typed into the focused app — Notepad, Chrome, VS Code, Discord, Slack, anything. Tap the hotkey again to stop.

A floating black-glass HUD sits at the bottom of the screen showing live audio bars while it listens.

## Features

| | |
|---|---|
| **Truly local** | No internet required after the first model download. Audio never leaves your machine. |
| **Phrase chunking** | Energy-based VAD detects pauses and commits phrases as you speak — no waiting for a long monologue to end. |
| **Universal target** | Types into any app that accepts keyboard input. Tested in Notepad, Chrome, VS Code, Discord, Claude Desktop, Word, Slack. |
| **Fast model swap** | Defaults to `small.en` (~466 MB). Swap to `tiny.en`, `base.en`, `medium.en`, or multilingual variants in one line. |
| **Custom HUD** | Animated 5-bar audio meter driven by real-time mic level. Smooth fade-in, wave loop while transcribing. |
| **Tray icon** | Hide the HUD; double-click tray to bring it back. Right-click → Exit. |
| **Toggle hotkey** | One press starts, another stops. Holding the keys won't break it. |

## Showcase

| Idle | Listening |
|---|---|
| ![Idle](showcase1_NotSpeaking.png) | ![Listening](showcase2_Speaking.png) |

The HUD lives bottom-center. Bars are muted in idle and pulse with live mic level while listening.

## Requirements

- Windows 11 (Win10 works; you lose the OS-level rounded shadow)
- A microphone
- ~500 MB free disk for the Whisper model
- For source builds: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Quickstart

### Pre-built binary

1. Grab the latest `SoundToText.exe` from [Releases](../../releases).
2. Run it. First launch downloads `ggml-small.en.bin` (~466 MB) into `%LocalAppData%\SoundToText\models`.
3. Click into any text field and press **`Ctrl+Shift+Alt+Space`**.

### Build from source

```powershell
git clone https://github.com/waytolazy/soundtotext.git
cd soundtotext
dotnet build -c Release
.\bin\Release\net8.0-windows10.0.22621.0\SoundToText.exe
```

### Self-contained single-file publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output lands in `bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\`. Distribute that single `.exe`.

## Usage

1. **Launch.** HUD appears bottom-center. First run downloads the model — wait until it says `Ready · Ctrl+Shift+Alt+Space`.
2. **Click into a target field** (any app).
3. **Press the hotkey** (`Ctrl+Shift+Alt+Space`). HUD turns blue and bars start dancing to your voice.
4. **Talk normally.** Pause briefly after each thought — that pause is the cue to transcribe and type that phrase.
5. **Press the hotkey again to stop.**

The HUD is draggable. The `X` hides it (still running in tray). To exit fully, right-click the tray icon → Exit.

## How it works

1. **Capture.** [NAudio](https://github.com/naudio/NAudio) streams 16 kHz mono PCM from the default mic in 30 ms buffers.
2. **VAD.** A simple RMS-based energy gate detects speech vs. silence. After ≥300 ms of speech followed by ≥650 ms of silence (or 12 s max), the buffered audio is emitted as a phrase.
3. **Transcribe.** [whisper.net](https://github.com/sandrohanea/whisper.net) runs the GGML model on CPU. Each phrase is processed independently (no cross-phrase context) for lower latency and fewer hallucinations.
4. **Inject.** Transcribed text is typed into the target window via `SendInput` with `KEYEVENTF_UNICODE` — generates `WM_CHAR` events, no clipboard touched.

## Configuration

VAD tuning lives in [`Audio/MicCapture.cs`](Audio/MicCapture.cs):

| Constant | Default | Purpose |
|---|---|---|
| `SilenceMs` | 650 | how long a pause must be to commit a phrase |
| `MinSpeechMs` | 300 | shortest speech that counts as a phrase |
| `MaxPhraseMs` | 12000 | force-commit if user never pauses |
| `RmsSpeechThreshold` | 600 | energy gate — raise if room noise triggers false phrases |
| `PreRollMs` | 200 | retained pre-speech audio so phrase onsets aren't clipped |

Model selection lives in [`Stt/WhisperEngine.cs`](Stt/WhisperEngine.cs):

```csharp
private const string ModelFile = "ggml-small.en.bin";
private const GgmlType ModelKind = GgmlType.SmallEn;
```

Available kinds: `Tiny`, `TinyEn`, `Base`, `BaseEn`, `Small`, `SmallEn`, `Medium`, `MediumEn`, `LargeV1`, `LargeV2`, `LargeV3`. The `*En` variants are English-only and noticeably faster.

| Model | Size | CPU latency (~1 s clip, 8-core) | Quality |
|---|---|---|---|
| `tiny.en` | 39 MB | ~80 ms | meh |
| `base.en` | 142 MB | ~200 ms | decent |
| **`small.en`** | **466 MB** | **~600 ms** | **good — default** |
| `medium.en` | 1.5 GB | ~1.8 s | great |

## File locations

| Path | Purpose |
|---|---|
| `%LocalAppData%\SoundToText\models\` | Whisper model files |
| `%LocalAppData%\SoundToText\log.txt` | Per-launch runtime log |

## Troubleshooting

**Hotkey says "already in use"** — another app owns `Ctrl+Shift+Alt+Space`. Edit `BindingCandidates` in [`Hotkey/GlobalHotkey.cs`](Hotkey/GlobalHotkey.cs) and rebuild.

**No text appearing in target app** — check `%LocalAppData%\SoundToText\log.txt`. Look for `SendInput partial: 0/N err=87` (struct mismatch) or any other error.

**Whisper outputs garbage like "Thanks for watching"** — Whisper's silence-hallucination tax. The Sanitizer in [`Stt/WhisperEngine.cs`](Stt/WhisperEngine.cs) strips the common ones; add more to the `Hallucinations` array if you find new patterns.

**Words get cut off at the start of phrases** — increase `PreRollMs`.

**Random short noises trigger phrases** — raise `RmsSpeechThreshold`.

**Long phrases never commit** — lower `MaxPhraseMs` or increase mic sensitivity.

## Roadmap

- [ ] Configurable hotkey via UI
- [ ] Per-language model picker
- [ ] Filler-word stripping ("uh", "um")
- [ ] Optional auto-capitalization + period insertion at phrase end
- [ ] Auto-start on Windows login
- [ ] Mic device picker (currently uses Windows default)

## Acknowledgements

- [whisper.cpp](https://github.com/ggerganov/whisper.cpp) — the inference engine
- [whisper.net](https://github.com/sandrohanea/whisper.net) — .NET bindings
- [NAudio](https://github.com/naudio/NAudio) — audio capture
- [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) — tray support

## License

[MIT](LICENSE)

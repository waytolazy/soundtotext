<p align="center">
  <img src="Assets/logo_banner.png" alt="SoundToText" width="320"/>
</p>

# SoundToText

**Local, real-time speech-to-text dictation for Windows and macOS.** Press a hotkey, talk, and your words type into whatever app is focused. Phrase-by-phrase chunking via silence detection. Powered by Whisper, GPU-accelerated (Metal on Apple Silicon, CUDA on Windows when available). No cloud, no telemetry, no account.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-blue.svg)](#requirements)
[![Whisper.net](https://img.shields.io/badge/Whisper.net-1.7.4-brightgreen.svg)](https://github.com/sandrohanea/whisper.net)

---

## What it does

Click into any text field, hit the hotkey, and start talking. Every time you pause for more than half a second, the chunk you just spoke gets transcribed and typed into the focused app — Notepad, Chrome, VS Code, Discord, Slack, anything. Tap the hotkey again to stop.

A floating black-glass HUD sits at the bottom of the screen showing live audio bars while it listens.

| Platform | Toggle (tap once = start, tap again = stop) | Hold (press & hold to talk, release to commit) |
|---|---|---|
| Windows | `Ctrl+Shift+Alt+Space` | `Ctrl+Alt+Space` |
| macOS | `Ctrl+Option+Shift+Space` | `Ctrl+Option+Space` |

## Features

| | |
|---|---|
| **Truly local** | No internet required after the first model download. Audio never leaves your machine. |
| **Cross-platform** | Single .NET 8 codebase runs natively on Windows and macOS via Avalonia + SharpHook + Silk.NET.OpenAL. |
| **GPU-accelerated** | Metal on Apple Silicon, CUDA on Windows (when present). Falls back to CPU automatically. |
| **High-quality, fast model** | Defaults to `large-v3-turbo` Q5_0 (~600 MB) — near-`large-v3` quality at a fraction of the latency. |
| **Live partial preview** | Italic gray text shows what Whisper is hearing in near real-time, before the phrase commits. Visible feedback while you're still talking. |
| **Toggle + hold modes** | Tap the hotkey for hands-free toggle, or use the second hotkey to push-to-talk like a walkie-talkie. |
| **Auto-cap + auto-punctuate** | First letter of each sentence is capitalized; missing terminal punctuation is filled in. Tracks sentence state across phrases. |
| **Phrase chunking** | Energy-based VAD detects pauses and commits phrases as you speak — no waiting for a long monologue to end. |
| **Universal target** | Types Unicode into any app via OS-native input simulation. |
| **Custom HUD** | Animated 5-bar audio meter driven by real-time mic level. |
| **Tray icon** | Hide the HUD; click tray icon to bring it back. |
| **Toggle hotkey** | One press starts, another stops. |

## Showcase

| Idle | Listening |
|---|---|
| ![Idle](showcase1_NotSpeaking.png) | ![Listening](showcase2_Speaking.png) |

## Requirements

- **OS**: Windows 10/11 or macOS 11+ (Apple Silicon or Intel)
- A microphone
- ~700 MB free disk for the default `large-v3-turbo` Q5_0 model (or up to ~3 GB if you switch to full `large-v3`)
- For source builds: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- macOS only:
  - **Microphone permission** (granted on first launch via the system prompt)
  - **Accessibility permission** (System Settings → Privacy & Security → Accessibility → enable for the terminal/app running SoundToText). Without this, the global hotkey and simulated typing will not work.

## Quickstart

### macOS

```bash
# install .NET 8 SDK if you don't have it
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
export PATH="$HOME/.dotnet:$PATH"

git clone https://github.com/waytolazy/soundtotext.git
cd soundtotext
dotnet run -c Release
```

First launch downloads `ggml-large-v3-turbo-q5_0.bin` (~600 MB) into `~/Library/Application Support/SoundToText/models/` and then warms up the Metal shader cache (a few seconds, one-time).

When prompted by macOS:
1. Allow microphone access.
2. Open **System Settings → Privacy & Security → Accessibility**, enable the terminal (or app bundle) that's running SoundToText, then **restart the app**. Without this, the global hotkey is silently ignored and typing is blocked.

### Windows

```powershell
git clone https://github.com/waytolazy/soundtotext.git
cd soundtotext
dotnet run -c Release
```

First launch downloads the model into `%LocalAppData%\SoundToText\models\`.

### Self-contained publish

```bash
# macOS Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# macOS Intel
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true

# Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Usage

1. **Launch.** HUD appears bottom-center. First run downloads the model — wait until it says `Ready · <hotkey>`.
2. **Click into a target field** (any app).
3. **Press the hotkey.** HUD turns blue and bars start dancing to your voice.
4. **Talk normally.** Pause briefly after each thought — that pause is the cue to transcribe and type that phrase.
5. **Press the hotkey again to stop.**

The HUD is draggable. The `✕` hides it (still running in tray). To exit fully, click the tray icon → Exit.

## How it works

1. **Capture.** [Silk.NET.OpenAL](https://github.com/dotnet/Silk.NET) (with bundled OpenAL Soft natives) streams 16 kHz mono PCM from the default mic.
2. **VAD.** A simple RMS-based energy gate detects speech vs. silence. After ≥280 ms of speech followed by ≥550 ms of silence (or 12 s max), the buffered audio is emitted as a phrase.
3. **Live partial.** While you're still mid-phrase, the controller throttle-fires partial inferences (~700 ms cadence, ~250 ms minimum gap). Each new partial cancels the in-flight one. Result is shown as italic muted preview text — typed-out text only happens on commit.
4. **Transcribe.** [whisper.net](https://github.com/sandrohanea/whisper.net) runs the `large-v3-turbo` Q5_0 GGML model with hardware acceleration (Metal on macOS, CUDA on Windows when available). Samples are passed as `float[]` directly to skip WAV (re)parsing.
5. **Normalize.** [TextNormalizer](Stt/TextNormalizer.cs) capitalizes sentence starts and adds missing terminal punctuation, tracking the previous phrase's last char to know what counts as "next sentence."
6. **Inject.** Final text is sent to the focused app via [SharpHook](https://github.com/TolikPylypchuk/SharpHook)'s `EventSimulator.SimulateTextEntry` — Win32 `SendInput` with `KEYEVENTF_UNICODE` on Windows, `CGEventKeyboardSetUnicodeString` on macOS.

### Toggle vs hold

- **Toggle** mode is for paragraphs and long-form: tap once to start, tap again to stop. While "active," the HUD listens until you tell it to stop.
- **Hold** mode is for short bursts: press and hold the hold-hotkey, talk, release. Releasing any of the modifier keys (or Space) ends the session and commits the final phrase. The hold and toggle bindings can't conflict (the controller refuses to toggle during a hold and vice versa).

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
private const GgmlType ModelKind = GgmlType.LargeV3Turbo;
private const QuantizationType ModelQuant = QuantizationType.Q5_0;
private const string ModelFile = "ggml-large-v3-turbo-q5_0.bin";
```

Available kinds: `Tiny`, `TinyEn`, `Base`, `BaseEn`, `Small`, `SmallEn`, `Medium`, `MediumEn`, `LargeV1`, `LargeV2`, `LargeV3`, **`LargeV3Turbo`** (default — distilled, ~3-4× faster than `LargeV3` at near-equal quality, multilingual).

| Model | Size | Latency (Apple Silicon, Metal) | Quality |
|---|---|---|---|
| `tiny.en` | 39 MB | ~50 ms | meh |
| `base.en` | 142 MB | ~120 ms | decent |
| `small.en` | 466 MB | ~300 ms | good |
| `medium.en` | 1.5 GB | ~700 ms | great |
| **`large-v3-turbo` Q5_0** | **~600 MB** | **~500 ms** | **best — default, multilingual** |
| `large-v3` (full precision) | 2.9 GB | ~2.5 s | marginally better, much slower |

GPU acceleration is enabled by default and falls back to CPU if the platform doesn't have Metal/CUDA. CPU-only latencies are roughly 5-10× slower than the Metal numbers above.

## File locations

| Platform | Models | Log |
|---|---|---|
| Windows | `%LocalAppData%\SoundToText\models\` | `%LocalAppData%\SoundToText\log.txt` |
| macOS | `~/Library/Application Support/SoundToText/models/` | `~/Library/Application Support/SoundToText/log.txt` |

## Troubleshooting

**macOS: hotkey does nothing** — you haven't granted Accessibility permission. System Settings → Privacy & Security → Accessibility → enable the app or terminal running SoundToText, then restart it.

**macOS: HUD appears but nothing happens when I talk** — microphone permission denied. System Settings → Privacy & Security → Microphone.

**No text appearing in target app** — check the log file. Look for `SimulateTextEntry result=…`. On macOS the most common cause is missing Accessibility permission.

**Whisper outputs garbage like "Thanks for watching"** — Whisper's silence-hallucination tax. The Sanitizer in [`Stt/WhisperEngine.cs`](Stt/WhisperEngine.cs) strips the common ones; add more to the `Hallucinations` array if you find new patterns.

**Words get cut off at the start of phrases** — increase `PreRollMs`.

**Random short noises trigger phrases** — raise `RmsSpeechThreshold`.

**Long phrases never commit** — lower `MaxPhraseMs` or increase mic sensitivity.

**`large-v3` is too slow on my machine** — swap to `small.en` or `medium.en` in `WhisperEngine.cs`.

## Roadmap

- [ ] Configurable hotkey via UI
- [ ] Per-language model picker
- [ ] Filler-word stripping ("uh", "um")
- [ ] Optional auto-capitalization + period insertion at phrase end
- [ ] Auto-start on login
- [ ] Mic device picker
- [ ] Native macOS .app bundle with embedded entitlements

## Acknowledgements

- [whisper.cpp](https://github.com/ggerganov/whisper.cpp) — inference engine
- [whisper.net](https://github.com/sandrohanea/whisper.net) — .NET bindings
- [Avalonia](https://avaloniaui.net/) — cross-platform .NET UI
- [Silk.NET](https://github.com/dotnet/Silk.NET) — cross-platform audio capture (OpenAL Soft)
- [SharpHook](https://github.com/TolikPylypchuk/SharpHook) — cross-platform global hooks + input simulation

## License

[MIT](LICENSE)

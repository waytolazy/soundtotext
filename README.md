# SoundToText

Local, real-time speech-to-text dictation for Windows 11. Press a hotkey, talk, and your words are typed into whatever app is focused. No cloud, no telemetry, no account.

Built on [whisper.cpp](https://github.com/ggerganov/whisper.cpp) via [whisper.net](https://github.com/sandrohanea/whisper.net). Runs entirely on CPU.

## Features

- **Toggle hotkey**: `Ctrl+Shift+Alt+Space` starts/stops listening
- **Phrase-by-phrase dictation**: silence detection chops audio into chunks, transcribes each as you pause, types it into the focused app
- **Native Win11 acrylic HUD** with live audio-level bars
- **Local Whisper model** (`small.en`, ~466MB, downloaded on first run to `%LocalAppData%\SoundToText\models`)
- **Works in any app** that accepts keyboard input — Notepad, Chrome, VS Code, Discord, Slack, etc.

## Requirements

- Windows 11 (Win10 works but no Mica/acrylic backdrop)
- .NET 8 Runtime (or use the published self-contained build)
- A microphone

## Install

### Pre-built

Grab `SoundToText.exe` from the [Releases](../../releases) page and run it. First launch downloads the Whisper model (~466MB).

### Build from source

```powershell
dotnet build -c Release
```

Self-contained single-file publish:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output lands in `bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\`.

## Usage

1. Run `SoundToText.exe`
2. Wait for model download/load (HUD shows progress)
3. Click into any text field
4. Press **Ctrl+Shift+Alt+Space** — bars start dancing, HUD says "Listening…"
5. Talk. Pause. Words appear.
6. Keep talking — each phrase types as you pause.
7. Press the hotkey again to stop.

The HUD lives bottom-center, draggable. Close the X to hide; tray icon brings it back.

## Files

- `%LocalAppData%\SoundToText\models\ggml-small.en.bin` — model
- `%LocalAppData%\SoundToText\log.txt` — runtime log (per-launch)

## Tuning

Numbers live in [`Audio/MicCapture.cs`](Audio/MicCapture.cs):

| Constant | Default | Purpose |
|---|---|---|
| `SilenceMs` | 650 | how long a pause must be to commit a phrase |
| `MinSpeechMs` | 300 | shortest speech that counts as a phrase |
| `MaxPhraseMs` | 12000 | force-commit if user never pauses |
| `RmsSpeechThreshold` | 600 | energy gate for "is this speech" |

Different model? Change `ModelKind` and `ModelFile` in [`Stt/WhisperEngine.cs`](Stt/WhisperEngine.cs) — `TinyEn`, `BaseEn`, `SmallEn`, `MediumEn`, `LargeV3` etc.

## License

MIT — see [LICENSE](LICENSE).

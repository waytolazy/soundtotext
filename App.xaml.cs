using System;
using System.Windows;
using SoundToText.Audio;
using SoundToText.Hotkey;
using SoundToText.Input;
using SoundToText.Stt;
using SoundToText.Tray;

namespace SoundToText;

public partial class App : Application
{
    private MainWindow? _hud;
    private GlobalHotkey? _hotkey;
    private MicCapture? _mic;
    private WhisperEngine? _whisper;
    private KeystrokeInjector? _injector;
    private TrayManager? _tray;
    private DictationController? _controller;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            Logger.Log($"OS={Environment.OSVersion} 64bit={Environment.Is64BitProcess} cpu={Environment.ProcessorCount}");
            _hud = new MainWindow();
            _hud.Show();
            _hud.SetStatus(HudStatus.Idle, "Loading model…");

            _injector = new KeystrokeInjector();
            _mic = new MicCapture();
            _whisper = new WhisperEngine();
            _tray = new TrayManager(OnTrayExit);

            await _whisper.InitializeAsync(p => _hud.SetStatus(HudStatus.Idle, p));

            _hotkey = new GlobalHotkey(_hud);
            _controller = new DictationController(_mic, _whisper, _injector, _hud,
                () => _hotkey.LastForegroundAtPress);
            _mic.LevelUpdated += level => _hud.OnLevel(level);

            _hotkey.Tap += () => _ = _controller.ToggleAsync();
            _hotkey.Register();
            Logger.Log($"Hotkey bound: {_hotkey.ActiveBindingLabel}");

            _hud.SetStatus(HudStatus.Idle, $"Ready  ·  {_hotkey.ActiveBindingLabel}");
        }
        catch (Exception ex)
        {
            Logger.Ex("OnStartup", ex);
            MessageBox.Show(ex.ToString(), "SoundToText error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnTrayExit() => Shutdown(0);

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _mic?.Dispose();
        _whisper?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}

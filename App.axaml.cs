using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Startup += (_, _) => _ = StartUpAsync(desktop);
            desktop.Exit += (_, _) => Shutdown();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartUpAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            Logger.Log($"OS={Environment.OSVersion} 64bit={Environment.Is64BitProcess} cpu={Environment.ProcessorCount} platform={(OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : "other")}");

            _hud = new MainWindow();
            desktop.MainWindow = _hud;
            _hud.Show();
            _hud.SetStatus(HudStatus.Idle, "Loading model…");

            _injector = new KeystrokeInjector();
            _mic = new MicCapture();
            _whisper = new WhisperEngine();
            _tray = new TrayManager(() =>
                Dispatcher.UIThread.Post(() => _hud?.ToggleVisibility()), OnTrayExit);

            await _whisper.InitializeAsync(p => _hud.SetStatus(HudStatus.Idle, p));

            _hotkey = new GlobalHotkey();
            _controller = new DictationController(_mic, _whisper, _injector, _hud, _tray);
            _mic.LevelUpdated += level => _hud!.OnLevel(level);

            _hotkey.Tap += () => _ = _controller.ToggleAsync();
            _hotkey.Register();
            Logger.Log($"Hotkey bound: {_hotkey.ActiveBindingLabel}");

            if (OperatingSystem.IsMacOS() && !MacPermissions.IsAccessibilityTrusted())
            {
                var binPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                Logger.Log($"Accessibility NOT granted. Bin: {binPath}");
                _hud.SetStatus(HudStatus.Error,
                    "Grant Accessibility, then restart the app");
                MacPermissions.OpenAccessibilityPane();
                return;
            }

            _hud.SetStatus(HudStatus.Idle, $"Ready  ·  {_hotkey.ActiveBindingLabel}");
        }
        catch (Exception ex)
        {
            Logger.Ex("OnStartup", ex);
            await ShowErrorAsync(ex);
            desktop.Shutdown(1);
        }
    }

    private static async Task ShowErrorAsync(Exception ex)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var win = new Window
                {
                    Title = "SoundToText error",
                    Width = 600,
                    Height = 400,
                    Content = new TextBox
                    {
                        Text = ex.ToString(),
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };
                win.Show();
            });
        }
        catch { }
    }

    private void OnTrayExit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown(0);
    }

    private void Shutdown()
    {
        _hotkey?.Dispose();
        _mic?.Dispose();
        _whisper?.Dispose();
        _tray?.Dispose();
    }
}

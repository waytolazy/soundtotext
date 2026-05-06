using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace SoundToText;

public enum HudStatus
{
    Idle,
    Listening,
    Transcribing,
    Error
}

public partial class MainWindow : Window
{
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private readonly double[] _levelHistory = new double[5];
    private HudStatus _state = HudStatus.Idle;
    private static readonly double[] BarBase = { 6, 9, 12, 9, 6 };
    private const double BarMax = 30;

    private DispatcherTimer? _waveTimer;
    private double _wavePhase;

    private TextBlock? _statusText;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        _bars = new[]
        {
            this.FindControl<Rectangle>("Bar0")!,
            this.FindControl<Rectangle>("Bar1")!,
            this.FindControl<Rectangle>("Bar2")!,
            this.FindControl<Rectangle>("Bar3")!,
            this.FindControl<Rectangle>("Bar4")!,
        };
        _statusText = this.FindControl<TextBlock>("StatusText");
        PositionBottomCenter();
        ApplyState();
    }

    private void PositionBottomCenter()
    {
        var screen = Screens.Primary ?? Screens.All[0];
        var wa = screen.WorkingArea;
        var scale = screen.Scaling;
        var w = (int)(Width * scale);
        var h = (int)(Height * scale);
        Position = new PixelPoint(
            wa.X + (wa.Width - w) / 2,
            wa.Y + wa.Height - h - (int)(24 * scale));
    }

    public void SetStatus(HudStatus status, string? text = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetStatus(status, text));
            return;
        }
        if (text != null && _statusText != null) _statusText.Text = text;
        if (_state == status) return;
        _state = status;
        ApplyState();
    }

    private IBrush GetBrush(string key)
    {
        if (Application.Current!.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var v) && v is IBrush b)
            return b;
        return Brushes.White;
    }

    private void ApplyState()
    {
        if (_bars.Length == 0) return;
        StopWave();

        var accent = GetBrush("AccentBrush");
        var muted = GetBrush("MutedBrush");
        var error = GetBrush("ErrorBrush");

        switch (_state)
        {
            case HudStatus.Idle:
                foreach (var b in _bars) b.Fill = muted;
                ResetBarsToBase();
                break;
            case HudStatus.Listening:
                foreach (var b in _bars) b.Fill = accent;
                break;
            case HudStatus.Transcribing:
                foreach (var b in _bars) b.Fill = accent;
                StartWave();
                break;
            case HudStatus.Error:
                foreach (var b in _bars) b.Fill = error;
                ResetBarsToBase();
                break;
        }
    }

    private void StartWave()
    {
        _wavePhase = 0;
        _waveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) =>
        {
            _wavePhase += 0.18;
            for (int i = 0; i < _bars.Length; i++)
            {
                var t = _wavePhase - i * 0.5;
                var n = 0.5 + 0.5 * Math.Sin(t);
                _bars[i].Height = BarBase[i] + n * (BarMax - BarBase[i]);
            }
        });
        _waveTimer.Start();
    }

    private void StopWave()
    {
        _waveTimer?.Stop();
        _waveTimer = null;
    }

    public void OnLevel(float level)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnLevel(level));
            return;
        }
        if (_state != HudStatus.Listening || _bars.Length == 0) return;

        for (int i = 0; i < _levelHistory.Length - 1; i++)
            _levelHistory[i] = _levelHistory[i + 1];
        _levelHistory[^1] = level;

        for (int i = 0; i < _bars.Length; i++)
        {
            var lvl = _levelHistory[i];
            var target = BarBase[i] + lvl * (BarMax - BarBase[i]) * 1.4;
            if (target > BarMax) target = BarMax;
            _bars[i].Height = _bars[i].Height + (target - _bars[i].Height) * 0.5;
        }
    }

    private void ResetBarsToBase()
    {
        for (int i = 0; i < _bars.Length; i++)
            _bars[i].Height = BarBase[i];
    }

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnHideClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Hide();

    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }
}

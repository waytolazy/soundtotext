using System;
using System.Threading.Tasks;
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

    private TextBlock? _statusA;
    private TextBlock? _statusB;
    private TextBlock? _partialText;
    private bool _statusUsesA = true;

    private Border? _glassRoot;
    private ScaleTransform? _glassScale;
    private TranslateTransform? _glassTranslate;
    private Ellipse? _halo;
    private Ellipse? _ripple;
    private ScaleTransform? _rippleScale;
    private Border? _flashLayer;
    private GradientStop? _bgStop0;
    private GradientStop? _bgStop1;

    private DispatcherTimer? _waveTimer;
    private DispatcherTimer? _breatheTimer;
    private DispatcherTimer? _glowTimer;
    private DispatcherTimer? _driftTimer;
    private double _wavePhase, _breathePhase, _driftPhase;
    private double _currentLevel;

    private CancellationTokenSourceSlim _typewriterCts = new();
    private CancellationTokenSourceSlim _errorAutoFadeCts = new();

    private static readonly Color IdleBarColor    = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
    private static readonly Color ListenBarColor  = Color.FromArgb(0xFF, 0x4F, 0xB1, 0xFF);
    private static readonly Color ErrorBarColor   = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);

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
        _statusA = this.FindControl<TextBlock>("StatusTextA");
        _statusB = this.FindControl<TextBlock>("StatusTextB");
        _partialText = this.FindControl<TextBlock>("PartialText");
        _glassRoot = this.FindControl<Border>("GlassRoot");
        _halo = this.FindControl<Ellipse>("HaloLayer");
        _ripple = this.FindControl<Ellipse>("RippleLayer");
        _flashLayer = this.FindControl<Border>("FlashLayer");

        if (_glassRoot?.RenderTransform is TransformGroup tg)
        {
            _glassScale = tg.Children[0] as ScaleTransform;
            _glassTranslate = tg.Children[1] as TranslateTransform;
        }
        if (_ripple?.RenderTransform is ScaleTransform rs) _rippleScale = rs;
        if (_glassRoot?.Background is LinearGradientBrush lgb && lgb.GradientStops.Count >= 2)
        {
            _bgStop0 = lgb.GradientStops[0];
            _bgStop1 = lgb.GradientStops[1];
        }

        foreach (var b in _bars)
            ApplyBarBrush(b, IdleBarColor);

        PositionBottomCenter();
        ApplyState();
        StartBreathing();
        _ = PlayEntrance();
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

        HidePartial();

        if (text != null) SetStatusText(text);

        if (_state == status) return;
        var prev = _state;
        _state = status;
        ApplyState();
        _ = PlayStatePulse();
        if (status == HudStatus.Error) _ = PlayErrorShakeAndAutoFade();
        if (status == HudStatus.Listening && prev != HudStatus.Listening) _ = PlayCascade();
    }

    public void SetPartial(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetPartial(text));
            return;
        }
        if (_partialText == null || _statusA == null || _statusB == null) return;
        if (string.IsNullOrWhiteSpace(text)) { HidePartial(); return; }

        _statusA.Opacity = 0;
        _statusB.Opacity = 0;
        _partialText.Text = text + "…";
        _partialText.Opacity = 0.75;
    }

    private void HidePartial()
    {
        if (_partialText == null) return;
        _partialText.Opacity = 0;
        _partialText.Text = "";
        if (_statusUsesA && _statusA != null) _statusA.Opacity = 1;
        else if (!_statusUsesA && _statusB != null) _statusB.Opacity = 1;
    }

    public void OnPhraseCommitted()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnPhraseCommitted);
            return;
        }
        _ = PlayPhraseFlash();
        _ = PlayRipple();
    }

    private void SetStatusText(string text)
    {
        _typewriterCts.Cancel();
        _typewriterCts = new CancellationTokenSourceSlim();

        if (_statusA == null || _statusB == null) return;

        var (oldTb, newTb) = _statusUsesA ? (_statusA, _statusB) : (_statusB, _statusA);
        _statusUsesA = !_statusUsesA;

        bool typewriter = _state == HudStatus.Listening || _state == HudStatus.Transcribing;
        newTb.Text = typewriter ? "" : text;
        _ = AnimateOpacity(newTb, 1.0, 150);
        _ = AnimateOpacity(oldTb, 0.0, 150);

        if (typewriter) _ = PlayTypewriter(newTb, text, _typewriterCts.Token);
    }

    private async Task PlayTypewriter(TextBlock target, string text, System.Threading.CancellationToken ct)
    {
        try
        {
            await Task.Delay(80, ct);
            for (int i = 1; i <= text.Length; i++)
            {
                if (ct.IsCancellationRequested) return;
                target.Text = text[..i];
                await Task.Delay(22, ct);
            }
        }
        catch (TaskCanceledException) { }
    }

    private void ApplyState()
    {
        if (_bars.Length == 0) return;

        StopWave();
        StopGlow();
        StopDrift();

        Color targetColor = _state switch
        {
            HudStatus.Listening    => ListenBarColor,
            HudStatus.Transcribing => ListenBarColor,
            HudStatus.Error        => ErrorBarColor,
            _                      => IdleBarColor,
        };

        for (int i = 0; i < _bars.Length; i++)
            _ = AnimateBarColor(_bars[i], targetColor, 200);

        switch (_state)
        {
            case HudStatus.Idle:
                ResetBarsToBase();
                _ = AnimateOpacity(_halo!, 0, 200);
                break;
            case HudStatus.Listening:
                StartGlow();
                StartDrift();
                break;
            case HudStatus.Transcribing:
                StartWave();
                StartDrift();
                break;
            case HudStatus.Error:
                ResetBarsToBase();
                _ = AnimateOpacity(_halo!, 0, 200);
                break;
        }
    }

    private void StartWave()
    {
        _wavePhase = 0;
        _waveTimer?.Stop();
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

    private void StopWave() { _waveTimer?.Stop(); _waveTimer = null; }

    private void StartBreathing()
    {
        _breathePhase = 0;
        _breatheTimer?.Stop();
        _breatheTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            if (_state != HudStatus.Idle || _bars.Length == 0) return;
            _breathePhase += 0.18;
            var n = 0.5 + 0.5 * Math.Sin(_breathePhase);
            var op = 0.85 + n * 0.15;
            foreach (var b in _bars) b.Opacity = op;
        });
        _breatheTimer.Start();
    }

    private void StartGlow()
    {
        if (_halo == null) return;
        _glowTimer?.Stop();
        _glowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) =>
        {
            if (_state != HudStatus.Listening) return;
            var lvl = _currentLevel;
            var op = 0.25 + lvl * 0.75;
            _halo.Opacity = op;
        });
        _halo.Opacity = 0.25;
        _glowTimer.Start();
    }

    private void StopGlow() { _glowTimer?.Stop(); _glowTimer = null; }

    private void StartDrift()
    {
        if (_bgStop0 == null || _bgStop1 == null) return;
        _driftPhase = 0;
        _driftTimer?.Stop();
        _driftTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Background, (_, _) =>
        {
            if (_state != HudStatus.Listening && _state != HudStatus.Transcribing) return;
            _driftPhase += 0.045;
            var hueShift = Math.Sin(_driftPhase) * 8.0;
            _bgStop0.Color = ShiftHue(Color.FromArgb(0xF5, 0x0A, 0x0A, 0x0C), hueShift, 0);
            _bgStop1.Color = ShiftHue(Color.FromArgb(0xF5, 0x02, 0x02, 0x03), -hueShift, 0);
        });
        _driftTimer.Start();
    }

    private void StopDrift()
    {
        _driftTimer?.Stop();
        _driftTimer = null;
        if (_bgStop0 != null) _bgStop0.Color = Color.FromArgb(0xF5, 0x0A, 0x0A, 0x0C);
        if (_bgStop1 != null) _bgStop1.Color = Color.FromArgb(0xF5, 0x02, 0x02, 0x03);
    }

    public void OnLevel(float level)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnLevel(level));
            return;
        }
        _currentLevel = level;
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
            _bars[i].Opacity = 1.0;
        }
    }

    private void ResetBarsToBase()
    {
        for (int i = 0; i < _bars.Length; i++)
            _bars[i].Height = BarBase[i];
    }

    private async Task PlayEntrance()
    {
        if (_glassRoot == null || _glassTranslate == null) return;
        _glassRoot.Opacity = 0;
        _glassTranslate.Y = 24;
        await Task.Delay(40);

        const int frames = 18;
        const int frameMs = 14;
        for (int i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            _glassRoot.Opacity = eased;
            _glassTranslate.Y = 24 * (1 - eased);
            await Task.Delay(frameMs);
        }
        _glassRoot.Opacity = 1;
        _glassTranslate.Y = 0;
    }

    private async Task PlayStatePulse()
    {
        if (_glassScale == null) return;
        const int frames = 12;
        const int frameMs = 16;
        for (int i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            double s = t < 0.5
                ? 1.0 + 0.025 * (t / 0.5)
                : 1.025 - 0.025 * ((t - 0.5) / 0.5);
            _glassScale.ScaleX = s;
            _glassScale.ScaleY = s;
            await Task.Delay(frameMs);
        }
        _glassScale.ScaleX = 1;
        _glassScale.ScaleY = 1;
    }

    private async Task PlayCascade()
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            var bar = _bars[i];
            var fromColor = ((SolidColorBrush)bar.Fill!).Color;
            _ = AnimateBarColor(bar, ListenBarColor, 180, fromColor);
            await Task.Delay(40);
        }
    }

    private async Task PlayPhraseFlash()
    {
        if (_flashLayer == null) return;
        _flashLayer.Opacity = 0.55;
        const int frames = 10;
        for (int i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            _flashLayer.Opacity = 0.55 * (1 - t);
            await Task.Delay(18);
        }
        _flashLayer.Opacity = 0;
    }

    private async Task PlayRipple()
    {
        if (_ripple == null || _rippleScale == null) return;
        _ripple.Opacity = 0.7;
        _rippleScale.ScaleX = 0.4;
        _rippleScale.ScaleY = 0.4;
        const int frames = 22;
        for (int i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            var s = 0.4 + eased * 4.5;
            _rippleScale.ScaleX = s;
            _rippleScale.ScaleY = s;
            _ripple.Opacity = 0.7 * (1 - t);
            await Task.Delay(18);
        }
        _ripple.Opacity = 0;
    }

    private async Task PlayErrorShakeAndAutoFade()
    {
        _errorAutoFadeCts.Cancel();
        _errorAutoFadeCts = new CancellationTokenSourceSlim();
        var ct = _errorAutoFadeCts.Token;

        if (_glassTranslate != null)
        {
            const int oscillations = 3;
            const int half = 6;
            for (int o = 0; o < oscillations; o++)
            {
                for (int i = 0; i <= half; i++)
                {
                    if (ct.IsCancellationRequested) return;
                    var t = i / (double)half;
                    _glassTranslate.X = 6 * Math.Sin(t * Math.PI) * (o % 2 == 0 ? 1 : -1);
                    await Task.Delay(8);
                }
            }
            _glassTranslate.X = 0;
        }

        try { await Task.Delay(2000, ct); }
        catch (TaskCanceledException) { return; }
        if (ct.IsCancellationRequested) return;
        if (_state == HudStatus.Error)
            SetStatus(HudStatus.Idle, "Ready");
    }

    private static async Task AnimateOpacity(Visual target, double to, int durationMs)
    {
        var from = target.Opacity;
        const int frames = 12;
        var perFrame = Math.Max(8, durationMs / frames);
        for (int i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            target.Opacity = from + (to - from) * eased;
            await Task.Delay(perFrame);
        }
        target.Opacity = to;
    }

    private static async Task AnimateBarColor(Rectangle bar, Color toColor, int durationMs, Color? fromOverride = null)
    {
        var fromColor = fromOverride ?? (bar.Fill is SolidColorBrush sb ? sb.Color : Colors.White);
        const int frames = 10;
        var perFrame = Math.Max(8, durationMs / frames);
        for (int i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var c = LerpColor(fromColor, toColor, t);
            ApplyBarBrush(bar, c);
            await Task.Delay(perFrame);
        }
        ApplyBarBrush(bar, toColor);
    }

    private static void ApplyBarBrush(Rectangle bar, Color c)
    {
        if (bar.Fill is SolidColorBrush sb) sb.Color = c;
        else bar.Fill = new SolidColorBrush(c);
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        byte L(byte x, byte y) => (byte)(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    private static Color ShiftHue(Color c, double degrees, double satDelta)
    {
        var (h, s, v) = RgbToHsv(c.R, c.G, c.B);
        h = (h + degrees + 360) % 360;
        s = Math.Clamp(s + satDelta, 0, 1);
        var (r, g, b) = HsvToRgb(h, s, v);
        return Color.FromArgb(c.A, r, g, b);
    }

    private static (double h, double s, double v) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double h = 0, s, v = max;
        double d = max - min;
        s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == rd) h = 60 * (((gd - bd) / d) % 6);
        else if (max == gd) h = 60 * ((bd - rd) / d + 2);
        else h = 60 * ((rd - gd) / d + 4);
        if (h < 0) h += 360;
        return (h, s, v);
    }

    private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
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
        else { Show(); Activate(); _ = PlayEntrance(); }
    }

    private sealed class CancellationTokenSourceSlim
    {
        private readonly System.Threading.CancellationTokenSource _cts = new();
        public System.Threading.CancellationToken Token => _cts.Token;
        public void Cancel() { try { _cts.Cancel(); } catch { } }
    }
}

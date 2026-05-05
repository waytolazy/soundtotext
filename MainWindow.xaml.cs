using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

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
    private Storyboard? _wave;
    private Storyboard? _fadeIn;
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private readonly double[] _levelHistory = new double[5];
    private HudStatus _state = HudStatus.Idle;
    private static readonly double[] BarBase = { 6, 9, 12, 9, 6 };
    private const double BarMax = 30;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoadedReady;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyNoActivate(hwnd);
        PositionBottomCenter();
    }

    private void OnLoadedReady(object sender, RoutedEventArgs e)
    {
        _wave = (Storyboard)FindResource("WaveLoop");
        _fadeIn = (Storyboard)FindResource("FadeIn");
        _bars = new[] { Bar0, Bar1, Bar2, Bar3, Bar4 };
        _fadeIn.Begin(this);
        ApplyState();
    }

    private void PositionBottomCenter()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Bottom - Height - 24;
    }

    public void SetStatus(HudStatus status, string? text = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(status, text));
            return;
        }

        if (text != null) StatusText.Text = text;
        if (_state == status) return;
        _state = status;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_bars.Length == 0) return;
        _wave?.Stop(this);

        var accent = (Brush)FindResource("AccentBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var error = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

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
                _wave?.Begin(this, true);
                break;
            case HudStatus.Error:
                foreach (var b in _bars) b.Fill = error;
                ResetBarsToBase();
                break;
        }
    }

    public void OnLevel(float level)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action<float>(OnLevel), level);
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
            var anim = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(70),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _bars[i].BeginAnimation(HeightProperty, anim);
        }
    }

    private void ResetBarsToBase()
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i].BeginAnimation(HeightProperty, null);
            var anim = new DoubleAnimation
            {
                To = BarBase[i],
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _bars[i].BeginAnimation(HeightProperty, anim);
        }
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static void ApplyNoActivate(IntPtr hwnd)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

}

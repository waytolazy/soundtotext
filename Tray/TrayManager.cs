using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SoundToText.Tray;

public sealed class TrayManager : IDisposable
{
    private const int FrameCount = 12;
    private TrayIcon? _icon;
    private TrayIcons? _icons;
    private WindowIcon? _idleIcon;
    private WindowIcon[]? _animFrames;
    private DispatcherTimer? _animTimer;
    private int _animFrame;

    public TrayManager(Action onShowHud, Action onExit)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://SoundToText/Assets/tray_idle.png"));
            _idleIcon = new WindowIcon(s);
        }
        catch (Exception ex) { Logger.Ex("Tray idle icon", ex); }

        try
        {
            _animFrames = new WindowIcon[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                using var s = AssetLoader.Open(new Uri($"avares://SoundToText/Assets/tray_frame_{i:D2}.png"));
                _animFrames[i] = new WindowIcon(s);
            }
        }
        catch (Exception ex) { Logger.Ex("Tray frames", ex); _animFrames = null; }

        _icon = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = "SoundToText",
        };

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem("Show HUD");
        showItem.Click += (_, _) => onShowHud();
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => onExit();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);
        _icon.Menu = menu;

        _icon.Clicked += (_, _) => onShowHud();

        _icons = new TrayIcons { _icon };
        if (Application.Current != null)
            TrayIcon.SetIcons(Application.Current, _icons);
    }

    public void StartAnimation()
    {
        if (_animFrames == null || _icon == null) return;
        if (_animTimer != null) return;
        _animFrame = 0;
        _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(85), DispatcherPriority.Background, (_, _) =>
        {
            if (_icon == null || _animFrames == null) return;
            _icon.Icon = _animFrames[_animFrame];
            _animFrame = (_animFrame + 1) % _animFrames.Length;
        });
        _animTimer.Start();
    }

    public void StopAnimation()
    {
        _animTimer?.Stop();
        _animTimer = null;
        if (_icon != null && _idleIcon != null) _icon.Icon = _idleIcon;
    }

    public void Dispose()
    {
        try
        {
            StopAnimation();
            if (_icon != null) _icon.IsVisible = false;
        }
        catch { }
        _icon = null;
        _icons = null;
    }
}

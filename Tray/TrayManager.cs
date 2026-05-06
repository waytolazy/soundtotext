using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace SoundToText.Tray;

public sealed class TrayManager : IDisposable
{
    private TrayIcon? _icon;
    private TrayIcons? _icons;

    public TrayManager(Action onShowHud, Action onExit)
    {
        WindowIcon? icon = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://SoundToText/Assets/trayicon.png"));
            icon = new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Logger.Ex("TrayIcon load", ex);
        }

        _icon = new TrayIcon
        {
            Icon = icon,
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

    public void Dispose()
    {
        try
        {
            if (_icon != null) _icon.IsVisible = false;
        }
        catch { }
        _icon = null;
        _icons = null;
    }
}

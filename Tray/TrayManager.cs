using System;
using System.Drawing;
using System.Windows.Forms;

namespace SoundToText.Tray;

public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayManager(Action onExit)
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "SoundToText"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show HUD", null, (_, __) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow w)
                w.ToggleVisibility();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, __) => onExit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, __) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow w)
                w.ToggleVisibility();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

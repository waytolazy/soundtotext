using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoundToText.Hotkey;

public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xB0CA;

    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_SHIFT = 0x4;
    private const uint MOD_WIN = 0x8;

    private readonly Window _owner;
    private IntPtr _hwnd;
    private HwndSource? _src;
    private bool _registered;
    public string ActiveBindingLabel { get; private set; } = "";

    private static readonly (uint mods, uint vk, string label)[] BindingCandidates =
    {
        (MOD_CONTROL | MOD_SHIFT | MOD_ALT, 0x20, "Ctrl+Shift+Alt+Space"),
    };

    private readonly Stopwatch _sinceLastFire = new();
    private const int DebounceMs = 250;

    public event Action? Tap;

    public IntPtr LastForegroundAtPress { get; private set; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public GlobalHotkey(Window owner)
    {
        _owner = owner;
    }

    public void Register()
    {
        var helper = new WindowInteropHelper(_owner);
        _hwnd = helper.EnsureHandle();
        _src = HwndSource.FromHwnd(_hwnd);
        _src!.AddHook(WndProc);

        var tried = new System.Collections.Generic.List<string>();
        foreach (var (mods, vk, label) in BindingCandidates)
        {
            if (RegisterHotKey(_hwnd, HOTKEY_ID, mods, vk))
            {
                _registered = true;
                ActiveBindingLabel = label;
                return;
            }
            tried.Add(label);
        }
        throw new InvalidOperationException(
            "All candidate hotkeys are in use. Tried: " + string.Join(", ", tried));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            OnHotkeyPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnHotkeyPressed()
    {
        if (_sinceLastFire.IsRunning && _sinceLastFire.ElapsedMilliseconds < DebounceMs) return;
        _sinceLastFire.Restart();

        var fg = GetForegroundWindow();
        var ownHwnd = new WindowInteropHelper(_owner).Handle;
        if (fg != IntPtr.Zero && fg != ownHwnd) LastForegroundAtPress = fg;

        Tap?.Invoke();
    }

    public void Dispose()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registered = false;
        }
        _src?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

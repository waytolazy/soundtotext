using System;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Native;

namespace SoundToText.Hotkey;

public sealed class GlobalHotkey : IDisposable
{
    private TaskPoolGlobalHook? _hook;
    private Task? _hookTask;
    private long _lastFireMs;
    private const int DebounceMs = 250;

    public event Action? Tap;
    public string ActiveBindingLabel { get; private set; } = "";

    public void Register()
    {
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.HookEnabled += (_, _) => Logger.Log("uiohook enabled (events flowing)");
        _hook.HookDisabled += (_, _) => Logger.Log("uiohook disabled");

        _hookTask = _hook.RunAsync();
        _hookTask.ContinueWith(t =>
        {
            if (t.IsFaulted) Logger.Ex("uiohook task faulted", t.Exception!.GetBaseException());
            else if (t.IsCompletedSuccessfully) Logger.Log("uiohook task ended");
        }, TaskScheduler.Default);

        ActiveBindingLabel = OperatingSystem.IsMacOS()
            ? "Ctrl+Option+Shift+Space"
            : "Ctrl+Shift+Alt+Space";
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != KeyCode.VcSpace) return;

        var mask = e.RawEvent.Mask;
        bool ctrl  = (mask & ModifierMask.Ctrl)  != 0;
        bool shift = (mask & ModifierMask.Shift) != 0;
        bool alt   = (mask & ModifierMask.Alt)   != 0;

        if (!(ctrl && shift && alt)) return;

        var now = Environment.TickCount64;
        if (now - _lastFireMs < DebounceMs) return;
        _lastFireMs = now;

        try { Tap?.Invoke(); }
        catch (Exception ex) { Logger.Ex("Hotkey.Tap", ex); }
    }

    public void Dispose()
    {
        try
        {
            if (_hook != null)
            {
                _hook.KeyPressed -= OnKeyPressed;
                _hook.Dispose();
            }
        }
        catch { }
        _hook = null;
        _hookTask = null;
    }
}

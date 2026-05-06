using System;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Native;

namespace SoundToText.Hotkey;

public sealed class GlobalHotkey : IDisposable
{
    private TaskPoolGlobalHook? _hook;
    private Task? _hookTask;
    private long _lastToggleFireMs;
    private bool _isHolding;
    private const int DebounceMs = 250;

    public event Action? Tap;
    public event Action? HoldStart;
    public event Action? HoldStop;

    public string ActiveBindingLabel { get; private set; } = "";
    public string HoldBindingLabel { get; private set; } = "";

    public void Register()
    {
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.HookEnabled += (_, _) => Logger.Log("uiohook enabled (events flowing)");
        _hook.HookDisabled += (_, _) => Logger.Log("uiohook disabled");

        _hookTask = _hook.RunAsync();
        _hookTask.ContinueWith(t =>
        {
            if (t.IsFaulted) Logger.Ex("uiohook task faulted", t.Exception!.GetBaseException());
            else if (t.IsCompletedSuccessfully) Logger.Log("uiohook task ended");
        }, TaskScheduler.Default);

        if (OperatingSystem.IsMacOS())
        {
            ActiveBindingLabel = "Ctrl+Option+Shift+Space";
            HoldBindingLabel   = "Ctrl+Option+Space (hold)";
        }
        else
        {
            ActiveBindingLabel = "Ctrl+Shift+Alt+Space";
            HoldBindingLabel   = "Ctrl+Alt+Space (hold)";
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != KeyCode.VcSpace) return;

        var mask = e.RawEvent.Mask;
        bool ctrl  = (mask & ModifierMask.Ctrl)  != 0;
        bool shift = (mask & ModifierMask.Shift) != 0;
        bool alt   = (mask & ModifierMask.Alt)   != 0;

        if (ctrl && shift && alt)
        {
            var now = Environment.TickCount64;
            if (now - _lastToggleFireMs < DebounceMs) return;
            _lastToggleFireMs = now;
            try { Tap?.Invoke(); }
            catch (Exception ex) { Logger.Ex("Hotkey.Tap", ex); }
            return;
        }

        if (ctrl && alt && !shift)
        {
            if (_isHolding) return;
            _isHolding = true;
            try { HoldStart?.Invoke(); }
            catch (Exception ex) { Logger.Ex("Hotkey.HoldStart", ex); }
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_isHolding) return;
        var kc = e.Data.KeyCode;
        if (kc != KeyCode.VcSpace
            && kc != KeyCode.VcLeftControl && kc != KeyCode.VcRightControl
            && kc != KeyCode.VcLeftAlt     && kc != KeyCode.VcRightAlt
            && kc != KeyCode.VcLeftMeta    && kc != KeyCode.VcRightMeta)
            return;

        _isHolding = false;
        try { HoldStop?.Invoke(); }
        catch (Exception ex) { Logger.Ex("Hotkey.HoldStop", ex); }
    }

    public void Dispose()
    {
        try
        {
            if (_hook != null)
            {
                _hook.KeyPressed -= OnKeyPressed;
                _hook.KeyReleased -= OnKeyReleased;
                _hook.Dispose();
            }
        }
        catch { }
        _hook = null;
        _hookTask = null;
    }
}

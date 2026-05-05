using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SoundToText.Input;

public sealed class KeystrokeInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const ushort SC_LCTRL = 0x1D;
    private const ushort SC_LSHIFT = 0x2A;
    private const ushort SC_LALT = 0x38;
    private const ushort SC_RCTRL = 0x1D;
    private const ushort SC_RSHIFT = 0x36;
    private const ushort SC_RALT = 0x38;
    private const ushort SC_LWIN = 0x5B;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public IntPtr CaptureForeground() => GetForegroundWindow();

    public string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(null)";
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            return $"{name} '{(title.Length > 24 ? title[..24] + "…" : title)}'";
        }
        catch { return title; }
    }

    private static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        if (GetForegroundWindow() == hWnd) return;
        var current = GetCurrentThreadId();
        var target = GetWindowThreadProcessId(hWnd, out _);
        if (current == target)
        {
            SetForegroundWindow(hWnd);
            return;
        }
        AttachThreadInput(current, target, true);
        try { SetForegroundWindow(hWnd); }
        finally { AttachThreadInput(current, target, false); }
    }

    public void TypeText(string text, IntPtr targetHwnd = default)
    {
        if (string.IsNullOrEmpty(text)) return;

        ReleaseHeldModifiers();
        Thread.Sleep(15);

        uint accepted = 0;
        uint expected = 0;
        int i = 0;
        while (i < text.Length)
        {
            int nl = text.IndexOf('\n', i);
            string segment = nl < 0 ? text[i..] : text[i..nl];
            if (segment.Length > 0)
            {
                var (acc, exp) = SendUnicode(segment);
                accepted += acc; expected += exp;
            }
            if (nl < 0) break;
            var (acc2, exp2) = SendVkPair(0x0D);
            accepted += acc2; expected += exp2;
            i = nl + 1;
        }
        if (accepted < expected)
            Logger.Log($"SendInput partial: {accepted}/{expected} err={Marshal.GetLastWin32Error()}");
    }

    private static (uint accepted, uint expected) SendUnicode(string s)
    {
        var inputs = new INPUT[s.Length * 2];
        int idx = 0;
        foreach (var ch in s)
        {
            inputs[idx++] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } }
            };
            inputs[idx++] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
            };
        }
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return (sent, (uint)inputs.Length);
    }

    private static (uint accepted, uint expected) SendVkPair(ushort vk)
    {
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } },
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return (sent, (uint)inputs.Length);
    }

    private static void ReleaseHeldModifiers()
    {
        var releases = new System.Collections.Generic.List<INPUT>();
        if ((GetAsyncKeyState(0xA2) & 0x8000) != 0) releases.Add(Scan(SC_LCTRL,  true, false));
        if ((GetAsyncKeyState(0xA3) & 0x8000) != 0) releases.Add(Scan(SC_RCTRL,  true, true));
        if ((GetAsyncKeyState(0xA0) & 0x8000) != 0) releases.Add(Scan(SC_LSHIFT, true, false));
        if ((GetAsyncKeyState(0xA1) & 0x8000) != 0) releases.Add(Scan(SC_RSHIFT, true, false));
        if ((GetAsyncKeyState(0xA4) & 0x8000) != 0) releases.Add(Scan(SC_LALT,   true, false));
        if ((GetAsyncKeyState(0xA5) & 0x8000) != 0) releases.Add(Scan(SC_RALT,   true, true));
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0) releases.Add(Scan(SC_LWIN,   true, true));
        if ((GetAsyncKeyState(0x5C) & 0x8000) != 0) releases.Add(Scan(SC_LWIN,   true, true));
        if (releases.Count == 0) return;
        var arr = releases.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Scan(ushort scan, bool up, bool extended) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = KEYEVENTF_SCANCODE
                          | (up ? KEYEVENTF_KEYUP : 0)
                          | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
            }
        }
    };
}

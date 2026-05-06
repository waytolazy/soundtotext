using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SoundToText;

public static class MacPermissions
{
    private const string AppServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(AppServices)]
    private static extern bool AXIsProcessTrusted();

    public static bool IsAccessibilityTrusted()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try { return AXIsProcessTrusted(); }
        catch { return true; }
    }

    public static void OpenAccessibilityPane()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            Process.Start(new ProcessStartInfo("open",
                "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
            { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Logger.Ex("OpenAccessibilityPane", ex);
        }
    }
}

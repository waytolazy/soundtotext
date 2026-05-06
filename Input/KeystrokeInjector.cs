using System;
using System.Threading;
using SharpHook;
using SharpHook.Native;

namespace SoundToText.Input;

public sealed class KeystrokeInjector
{
    private readonly EventSimulator _sim = new();

    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Thread.Sleep(15);

        try
        {
            var result = _sim.SimulateTextEntry(text);
            if (result != UioHookResult.Success)
                Logger.Log($"SimulateTextEntry result={result}");
        }
        catch (Exception ex)
        {
            Logger.Ex("SimulateTextEntry", ex);
        }
    }
}

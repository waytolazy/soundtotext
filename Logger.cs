using System;
using System.IO;
using System.Threading;

namespace SoundToText;

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _path;

    static Logger()
    {
        _path = Path.Combine(AppPaths.LocalDataDir, "log.txt");
        try { File.WriteAllText(_path, $"=== launch {DateTime.Now:O} ===\n"); }
        catch { }
    }

    public static string FilePath => _path;

    public static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Thread.CurrentThread.ManagedThreadId,2}] {msg}";
        lock (_lock)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); } catch { }
        }
        System.Diagnostics.Debug.WriteLine(line);
    }

    public static void Ex(string ctx, Exception ex) => Log($"{ctx} EX: {ex.GetType().Name}: {ex.Message}");
}

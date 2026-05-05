using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SoundToText.Audio;
using SoundToText.Input;
using SoundToText.Stt;

namespace SoundToText;

public sealed class DictationController
{
    private readonly MicCapture _mic;
    private readonly WhisperEngine _whisper;
    private readonly KeystrokeInjector _injector;
    private readonly MainWindow _hud;
    private readonly Func<IntPtr> _foregroundAtPress;
    private readonly object _gate = new();

    private bool _toggleActive;
    private IntPtr _targetHwnd;
    private int _phrasesPending;

    private readonly BlockingCollection<byte[]> _queue = new(new ConcurrentQueue<byte[]>());
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;

    public DictationController(MicCapture mic, WhisperEngine whisper, KeystrokeInjector injector,
        MainWindow hud, Func<IntPtr> foregroundAtPress)
    {
        _mic = mic;
        _whisper = whisper;
        _injector = injector;
        _hud = hud;
        _foregroundAtPress = foregroundAtPress;
        _mic.PhraseReady += OnPhraseReady;
    }

    public Task ToggleAsync()
    {
        lock (_gate)
        {
            if (_toggleActive) StopSession();
            else StartSession();
        }
        return Task.CompletedTask;
    }

    private void StartSession()
    {
        _toggleActive = true;
        var fg = _foregroundAtPress();
        if (fg == IntPtr.Zero) fg = _injector.CaptureForeground();
        _targetHwnd = fg;
        Logger.Log($"StartSession target=0x{_targetHwnd.ToInt64():X} ({_injector.DescribeWindow(_targetHwnd)})");
        _workerCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoop(_workerCts.Token));
        _mic.Start();
        _hud.SetStatus(HudStatus.Listening, "Listening…");
    }

    private void StopSession()
    {
        _toggleActive = false;
        _mic.Stop();
        _workerCts?.Cancel();
        _workerCts = null;
        _hud.SetStatus(HudStatus.Idle, _phrasesPending > 0 ? "Finishing…" : "Ready");
    }

    private void OnPhraseReady(byte[] wav)
    {
        Interlocked.Increment(ref _phrasesPending);
        _queue.Add(wav);
    }

    private void WorkerLoop(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                byte[]? wav;
                try
                {
                    if (!_queue.TryTake(out wav, 50))
                    {
                        if (ct.IsCancellationRequested && _queue.Count == 0) return;
                        continue;
                    }
                }
                catch { return; }

                if (wav == null) continue;
                _ = TranscribeAndType(wav);
            }
        }
        catch { }
    }

    private async Task TranscribeAndType(byte[] wav)
    {
        try
        {
            _hud.SetStatus(HudStatus.Transcribing, "Transcribing…");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await _whisper.TranscribeAsync(wav);
            Logger.Log($"whisper {sw.ElapsedMilliseconds}ms => '{text}'");
            if (!string.IsNullOrWhiteSpace(text))
            {
                await _hud.Dispatcher.InvokeAsync(() =>
                    _injector.TypeText(text + " ", _targetHwnd));
                _hud.SetStatus(_toggleActive ? HudStatus.Listening : HudStatus.Idle, Trim(text));
            }
            else
            {
                _hud.SetStatus(_toggleActive ? HudStatus.Listening : HudStatus.Idle, "…");
            }
        }
        catch (Exception ex)
        {
            _hud.SetStatus(HudStatus.Error, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _phrasesPending);
        }
    }

    private static string Trim(string s) => s.Length > 48 ? s[..48] + "…" : s;
}

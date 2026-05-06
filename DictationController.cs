using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SoundToText.Audio;
using SoundToText.Input;
using SoundToText.Stt;
using SoundToText.Tray;

namespace SoundToText;

public sealed class DictationController
{
    private readonly MicCapture _mic;
    private readonly WhisperEngine _whisper;
    private readonly KeystrokeInjector _injector;
    private readonly MainWindow _hud;
    private readonly TrayManager? _tray;
    private readonly TextNormalizer _normalizer = new();
    private readonly object _gate = new();

    private bool _toggleActive;
    private bool _holdActive;
    private int _phrasesPending;

    private readonly BlockingCollection<float[]> _queue = new(new ConcurrentQueue<float[]>());
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;

    private CancellationTokenSource? _partialCts;
    private long _lastPartialDispatchMs;
    private const int PartialMinDispatchGapMs = 250;

    public DictationController(MicCapture mic, WhisperEngine whisper, KeystrokeInjector injector,
        MainWindow hud, TrayManager? tray = null)
    {
        _mic = mic;
        _whisper = whisper;
        _injector = injector;
        _hud = hud;
        _tray = tray;
        _mic.PhraseReady += OnPhraseReady;
        _mic.PartialAvailable += OnPartialAvailable;
    }

    public Task ToggleAsync()
    {
        lock (_gate)
        {
            if (_holdActive) return Task.CompletedTask;
            if (_toggleActive) StopSession();
            else StartSession();
        }
        return Task.CompletedTask;
    }

    public void HoldStart()
    {
        lock (_gate)
        {
            if (_toggleActive || _holdActive) return;
            _holdActive = true;
            StartSession();
        }
    }

    public void HoldStop()
    {
        lock (_gate)
        {
            if (!_holdActive) return;
            _holdActive = false;
            StopSession();
        }
    }

    private void StartSession()
    {
        _toggleActive = !_holdActive ? true : _toggleActive;
        if (_holdActive) Logger.Log("HoldStart: session begin");
        else Logger.Log("Toggle: session begin");

        _normalizer.Reset();
        _workerCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoopAsync(_workerCts.Token));
        _mic.Start();
        _hud.SetStatus(HudStatus.Listening, "Listening…");
    }

    private void StopSession()
    {
        if (!_holdActive) _toggleActive = false;
        _mic.Stop();
        _partialCts?.Cancel();
        _partialCts = null;
        _workerCts?.Cancel();
        _workerCts = null;
        _tray?.StopAnimation();
        _hud.SetStatus(HudStatus.Idle, _phrasesPending > 0 ? "Finishing…" : "Ready");
    }

    private void OnPhraseReady(float[] samples)
    {
        Interlocked.Increment(ref _phrasesPending);
        _partialCts?.Cancel();
        _queue.Add(samples);
    }

    private void OnPartialAvailable(float[] samples)
    {
        var now = Environment.TickCount64;
        if (now - _lastPartialDispatchMs < PartialMinDispatchGapMs) return;
        _lastPartialDispatchMs = now;

        _partialCts?.Cancel();
        var cts = new CancellationTokenSource();
        _partialCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                var text = await _whisper.TranscribeAsync(samples, cts.Token);
                if (cts.IsCancellationRequested) return;
                if (!string.IsNullOrWhiteSpace(text))
                    _hud.SetPartial(text);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Ex("Partial", ex); }
        });
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                float[]? samples;
                try
                {
                    if (!_queue.TryTake(out samples, 50))
                    {
                        if (ct.IsCancellationRequested && _queue.Count == 0) return;
                        continue;
                    }
                }
                catch { return; }

                if (samples == null) continue;
                await TranscribeAndType(samples);
            }
        }
        catch (Exception ex) { Logger.Ex("WorkerLoop", ex); }
    }

    private async Task TranscribeAndType(float[] samples)
    {
        try
        {
            _hud.SetStatus(HudStatus.Transcribing, "Transcribing…");
            _tray?.StartAnimation();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var raw = await _whisper.TranscribeAsync(samples);
            var audioMs = (int)(samples.Length / (double)MicCapture.SampleRate * 1000);
            Logger.Log($"whisper {sw.ElapsedMilliseconds}ms (audio {audioMs}ms, x{audioMs / Math.Max(1.0, sw.ElapsedMilliseconds):F2}) => '{raw}'");

            var text = _normalizer.Normalize(raw);

            bool sessionStillLive = _toggleActive || _holdActive;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _injector.TypeText(text + " ");
                _hud.SetStatus(sessionStillLive ? HudStatus.Listening : HudStatus.Idle, Trim(text));
                _hud.OnPhraseCommitted();
            }
            else
            {
                _hud.SetStatus(sessionStillLive ? HudStatus.Listening : HudStatus.Idle, "…");
            }
        }
        catch (Exception ex)
        {
            Logger.Ex("TranscribeAndType", ex);
            _hud.SetStatus(HudStatus.Error, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _phrasesPending);
            if (_phrasesPending == 0 && !_toggleActive && !_holdActive) _tray?.StopAnimation();
        }
    }

    private static string Trim(string s) => s.Length > 48 ? s[..48] + "…" : s;
}

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
    private readonly object _gate = new();

    private bool _toggleActive;
    private int _phrasesPending;

    private readonly BlockingCollection<float[]> _queue = new(new ConcurrentQueue<float[]>());
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;

    public DictationController(MicCapture mic, WhisperEngine whisper, KeystrokeInjector injector,
        MainWindow hud)
    {
        _mic = mic;
        _whisper = whisper;
        _injector = injector;
        _hud = hud;
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
        Logger.Log("StartSession");
        _workerCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoopAsync(_workerCts.Token));
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

    private void OnPhraseReady(float[] samples)
    {
        Interlocked.Increment(ref _phrasesPending);
        _queue.Add(samples);
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await _whisper.TranscribeAsync(samples);
            var audioMs = (int)(samples.Length / (double)MicCapture.SampleRate * 1000);
            Logger.Log($"whisper {sw.ElapsedMilliseconds}ms (audio {audioMs}ms, x{audioMs / Math.Max(1.0, sw.ElapsedMilliseconds):F2}) => '{text}'");
            if (!string.IsNullOrWhiteSpace(text))
            {
                _injector.TypeText(text + " ");
                _hud.SetStatus(_toggleActive ? HudStatus.Listening : HudStatus.Idle, Trim(text));
            }
            else
            {
                _hud.SetStatus(_toggleActive ? HudStatus.Listening : HudStatus.Idle, "…");
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
        }
    }

    private static string Trim(string s) => s.Length > 48 ? s[..48] + "…" : s;
}

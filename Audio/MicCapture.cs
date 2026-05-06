using System;
using System.Collections.Generic;
using System.Threading;
using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.EXT;

namespace SoundToText.Audio;

public sealed unsafe class MicCapture : IDisposable
{
    public const int SampleRate = 16000;
    public const int BytesPerSample = 2;
    private const int SilenceMs = 550;
    private const int MinSpeechMs = 280;
    private const int MaxPhraseMs = 12000;
    private const int PreRollMs = 200;
    private const int RmsSpeechThreshold = 600;

    private const int PollIntervalMs = 8;
    private const int CaptureBufferSamples = SampleRate * 2;

    private ALContext? _alc;
    private Capture? _capture;
    private Device* _device;

    private readonly object _lock = new();
    private Thread? _pollThread;
    private CancellationTokenSource? _pollCts;
    public bool IsCapturing { get; private set; }

    private readonly List<short> _buffer = new(SampleRate * 8);
    private int _silenceMs;
    private int _phraseSpeechMs;
    private int _phraseTotalMs;
    private bool _inSpeech;
    private readonly Queue<short> _preRoll = new(SampleRate);

    public event Action<float[]>? PhraseReady;
    public event Action<float[]>? PartialAvailable;
    public event Action<float>? LevelUpdated;

    private const int PartialIntervalMs = 400;
    private const int PartialMinSpeechMs = 400;
    private long _lastPartialTickMs;

    private const int LevelIntervalMs = 33;
    private long _lastLevelTickMs;

    public void Start()
    {
        lock (_lock)
        {
            if (IsCapturing) return;
            ResetState();

            _alc = ALContext.GetApi();
            if (!_alc.TryGetExtension<Capture>(null, out var capture))
                throw new InvalidOperationException("OpenAL capture extension not available on this platform.");
            _capture = capture;

            _device = _capture.CaptureOpenDevice(
                null,
                SampleRate,
                BufferFormat.Mono16,
                CaptureBufferSamples);

            if (_device == null)
                throw new InvalidOperationException("Failed to open default audio capture device.");

            _capture.CaptureStart(_device);
            IsCapturing = true;

            _pollCts = new CancellationTokenSource();
            _pollThread = new Thread(() => PollLoop(_pollCts.Token))
            {
                IsBackground = true,
                Name = "SoundToText-AudioPoll"
            };
            _pollThread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            if (!IsCapturing) return;
            _pollCts?.Cancel();
            thread = _pollThread;
        }

        try { thread?.Join(500); } catch { }

        lock (_lock)
        {
            try
            {
                if (_capture != null && _device != null)
                {
                    _capture.CaptureStop(_device);
                    _capture.CaptureCloseDevice(_device);
                }
            }
            catch { }
            _device = null;
            _capture = null;
            _alc?.Dispose();
            _alc = null;
            _pollCts?.Dispose();
            _pollCts = null;
            _pollThread = null;
            IsCapturing = false;

            if (_phraseSpeechMs >= MinSpeechMs && _buffer.Count > 0)
                EmitPhrase();
            ResetState();
        }
    }

    private void ResetState()
    {
        _buffer.Clear();
        _preRoll.Clear();
        _silenceMs = 0;
        _phraseSpeechMs = 0;
        _phraseTotalMs = 0;
        _inSpeech = false;
        _lastPartialTickMs = 0;
    }

    private void PollLoop(CancellationToken ct)
    {
        var scratch = new short[CaptureBufferSamples];
        while (!ct.IsCancellationRequested)
        {
            int available;
            lock (_lock)
            {
                if (_capture == null || _device == null) return;
                available = GetAvailableSamples(_capture, _device);
            }

            if (available <= 0)
            {
                Thread.Sleep(PollIntervalMs);
                continue;
            }

            int toRead = Math.Min(available, CaptureBufferSamples);

            lock (_lock)
            {
                if (_capture == null || _device == null) return;
                fixed (short* p = scratch)
                {
                    _capture.CaptureSamples(_device, p, toRead);
                }
            }

            ProcessChunk(scratch, toRead);
        }
    }

    private static int GetAvailableSamples(Capture capture, Device* device)
    {
        int samples;
        capture.GetContextProperty(device, GetCaptureContextInteger.CaptureSamples, 1, &samples);
        return samples;
    }

    private void ProcessChunk(short[] buf, int sampleCount)
    {
        if (sampleCount <= 0) return;
        int chunkMs = (int)Math.Round(sampleCount / (double)SampleRate * 1000.0);
        var rms = ComputeRms(buf, sampleCount);
        bool speech = rms >= RmsSpeechThreshold;
        var level = (float)Math.Min(1.0, rms / 3500.0);
        var nowMs = Environment.TickCount64;
        if (nowMs - _lastLevelTickMs >= LevelIntervalMs)
        {
            _lastLevelTickMs = nowMs;
            LevelUpdated?.Invoke(level);
        }

        lock (_lock)
        {
            if (!_inSpeech)
            {
                int maxRoll = SampleRate * PreRollMs / 1000;
                for (int i = 0; i < sampleCount; i++)
                {
                    if (_preRoll.Count >= maxRoll) _preRoll.Dequeue();
                    _preRoll.Enqueue(buf[i]);
                }
                if (speech)
                {
                    _inSpeech = true;
                    _buffer.AddRange(_preRoll);
                    _preRoll.Clear();
                    AppendSamples(buf, sampleCount);
                    _phraseSpeechMs = chunkMs;
                    _phraseTotalMs = chunkMs;
                    _silenceMs = 0;
                }
                return;
            }

            AppendSamples(buf, sampleCount);
            _phraseTotalMs += chunkMs;
            if (speech)
            {
                _phraseSpeechMs += chunkMs;
                _silenceMs = 0;
            }
            else
            {
                _silenceMs += chunkMs;
            }

            bool endByPause = _silenceMs >= SilenceMs && _phraseSpeechMs >= MinSpeechMs;
            bool endByLength = _phraseTotalMs >= MaxPhraseMs;
            if (endByPause || endByLength)
            {
                EmitPhrase();
                _buffer.Clear();
                _silenceMs = 0;
                _phraseSpeechMs = 0;
                _phraseTotalMs = 0;
                _inSpeech = false;
            }
            else if (_phraseSpeechMs >= PartialMinSpeechMs)
            {
                var now = Environment.TickCount64;
                if (now - _lastPartialTickMs >= PartialIntervalMs)
                {
                    _lastPartialTickMs = now;
                    EmitPartial();
                }
            }
        }
    }

    private void AppendSamples(short[] buf, int count)
    {
        for (int i = 0; i < count; i++) _buffer.Add(buf[i]);
    }

    private void EmitPhrase()
    {
        var pcm = _buffer.ToArray();
        var floats = ToFloats(pcm);
        ThreadPool.QueueUserWorkItem(_ => PhraseReady?.Invoke(floats));
    }

    private void EmitPartial()
    {
        var pcm = _buffer.ToArray();
        var floats = ToFloats(pcm);
        ThreadPool.QueueUserWorkItem(_ => PartialAvailable?.Invoke(floats));
    }

    private static float[] ToFloats(short[] pcm)
    {
        var f = new float[pcm.Length];
        const float scale = 1f / 32768f;
        for (int i = 0; i < pcm.Length; i++) f[i] = pcm[i] * scale;
        return f;
    }

    private static double ComputeRms(short[] buf, int count)
    {
        long sumSq = 0;
        for (int i = 0; i < count; i++)
        {
            int s = buf[i];
            sumSq += s * s;
        }
        if (count == 0) return 0;
        return Math.Sqrt(sumSq / (double)count);
    }

    public void Dispose() => Stop();
}

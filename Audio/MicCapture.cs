using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace SoundToText.Audio;

public sealed class MicCapture : IDisposable
{
    public const int SampleRate = 16000;
    public const int BytesPerSample = 2;
    private const int SilenceMs = 650;
    private const int MinSpeechMs = 300;
    private const int MaxPhraseMs = 12000;
    private const int PreRollMs = 200;
    private const int RmsSpeechThreshold = 600;

    private WaveInEvent? _wave;
    private readonly object _lock = new();
    public bool IsCapturing { get; private set; }

    private readonly List<byte> _buffer = new(SampleRate * BytesPerSample * 8);
    private int _silenceMs;
    private int _phraseSpeechMs;
    private int _phraseTotalMs;
    private bool _inSpeech;
    private readonly Queue<byte> _preRoll = new(SampleRate * BytesPerSample * 1);

    public event Action<byte[]>? PhraseReady;
    public event Action<float>? LevelUpdated;

    public void Start()
    {
        lock (_lock)
        {
            if (IsCapturing) return;
            ResetState();
            _wave = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 30,
                NumberOfBuffers = 4
            };
            _wave.DataAvailable += OnData;
            _wave.StartRecording();
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsCapturing) return;
            try { _wave?.StopRecording(); } catch { }
            _wave?.Dispose();
            _wave = null;
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
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        int chunkMs = (int)Math.Round(e.BytesRecorded / (double)(SampleRate * BytesPerSample) * 1000.0);
        var rms = ComputeRms(e.Buffer, e.BytesRecorded);
        bool speech = rms >= RmsSpeechThreshold;
        var level = (float)Math.Min(1.0, rms / 3500.0);
        LevelUpdated?.Invoke(level);

        lock (_lock)
        {
            if (!_inSpeech)
            {
                int maxRoll = SampleRate * BytesPerSample * PreRollMs / 1000;
                for (int i = 0; i < e.BytesRecorded; i++)
                {
                    if (_preRoll.Count >= maxRoll) _preRoll.Dequeue();
                    _preRoll.Enqueue(e.Buffer[i]);
                }
                if (speech)
                {
                    _inSpeech = true;
                    _buffer.AddRange(_preRoll);
                    _preRoll.Clear();
                    AppendSamples(e.Buffer, e.BytesRecorded);
                    _phraseSpeechMs = chunkMs;
                    _phraseTotalMs = chunkMs;
                    _silenceMs = 0;
                }
                return;
            }

            AppendSamples(e.Buffer, e.BytesRecorded);
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
        }
    }

    private void AppendSamples(byte[] buf, int count)
    {
        for (int i = 0; i < count; i++) _buffer.Add(buf[i]);
    }

    private void EmitPhrase()
    {
        var pcm = _buffer.ToArray();
        var wav = WrapWav(pcm, SampleRate, 1, 16);
        ThreadPool.QueueUserWorkItem(_ => PhraseReady?.Invoke(wav));
    }

    private static double ComputeRms(byte[] buf, int count)
    {
        long sumSq = 0;
        int samples = count / 2;
        for (int i = 0; i + 1 < count; i += 2)
        {
            short s = (short)(buf[i] | (buf[i + 1] << 8));
            sumSq += s * s;
        }
        if (samples == 0) return 0;
        return Math.Sqrt(sumSq / (double)samples);
    }

    private static byte[] WrapWav(byte[] pcm, int sampleRate, int channels, int bitsPerSample)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + pcm.Length);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(pcm.Length);
        bw.Write(pcm);
        return ms.ToArray();
    }

    public void Dispose() => Stop();
}

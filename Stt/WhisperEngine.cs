using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace SoundToText.Stt;

public sealed class WhisperEngine : IDisposable
{
    private const string ModelFile = "ggml-small.en.bin";
    private const GgmlType ModelKind = GgmlType.SmallEn;

    private WhisperFactory? _factory;
    private string _modelPath = "";

    public async Task InitializeAsync(Action<string>? progress = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundToText", "models");
        Directory.CreateDirectory(dir);
        _modelPath = Path.Combine(dir, ModelFile);

        if (!File.Exists(_modelPath))
        {
            progress?.Invoke("Downloading model (~466MB)…");
            using var stream = await WhisperGgmlDownloader.GetGgmlModelAsync(ModelKind, QuantizationType.NoQuantization);
            await using var fs = File.Create(_modelPath);
            await stream.CopyToAsync(fs);
        }

        progress?.Invoke("Loading model…");
        _factory = WhisperFactory.FromPath(_modelPath);
    }

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        if (_factory == null) throw new InvalidOperationException("Model not loaded");

        await using var processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
            .WithNoContext()
            .WithSingleSegment()
            .WithTemperature(0.0f)
            .WithNoSpeechThreshold(0.5f)
            .Build();

        await using var ms = new MemoryStream(wav);

        var sb = new System.Text.StringBuilder();
        await foreach (var seg in processor.ProcessAsync(ms, ct))
        {
            sb.Append(seg.Text);
        }
        return Sanitize(sb.ToString());
    }

    private static readonly string[] Hallucinations =
    {
        "[BLANK_AUDIO]", "[ Silence ]", "(silence)", "[silence]",
        "Thanks for watching!", "Thank you for watching.",
        "Thank you for watching!", "you", "Bye.", "Bye bye.",
    };

    private static string Sanitize(string s)
    {
        s = s.Trim();
        foreach (var h in Hallucinations)
            if (string.Equals(s.Trim('.', ' ', '!', '?'), h.Trim('.', ' ', '!', '?'),
                    StringComparison.OrdinalIgnoreCase))
                return "";
        if (s.StartsWith("[") && s.Contains(']'))
            s = s[(s.IndexOf(']') + 1)..].TrimStart();
        return s;
    }

    public void Dispose() => _factory?.Dispose();
}

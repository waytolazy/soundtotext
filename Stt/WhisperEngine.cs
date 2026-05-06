using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace SoundToText.Stt;

public sealed class WhisperEngine : IDisposable
{
    private const GgmlType FinalModelKind = GgmlType.LargeV3Turbo;
    private const QuantizationType FinalModelQuant = QuantizationType.Q5_0;
    private const string FinalModelFile = "ggml-large-v3-turbo-q5_0.bin";
    private const string FinalModelSize = "~600 MB";

    private const GgmlType PartialModelKind = GgmlType.BaseEn;
    private const QuantizationType PartialModelQuant = QuantizationType.NoQuantization;
    private const string PartialModelFile = "ggml-base.en.bin";
    private const string PartialModelSize = "~140 MB";

    private WhisperFactory? _finalFactory;
    private WhisperFactory? _partialFactory;
    private bool _gpuEnabled;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task InitializeAsync(Action<string>? progress = null)
    {
        var dir = Path.Combine(AppPaths.LocalDataDir, "models");
        Directory.CreateDirectory(dir);

        var finalPath = Path.Combine(dir, FinalModelFile);
        if (!File.Exists(finalPath))
        {
            progress?.Invoke($"Downloading large-v3-turbo Q5_0 ({FinalModelSize})…");
            await DownloadModelAsync(FinalModelKind, FinalModelQuant, finalPath);
        }

        var partialPath = Path.Combine(dir, PartialModelFile);
        if (!File.Exists(partialPath))
        {
            progress?.Invoke($"Downloading base.en for live preview ({PartialModelSize})…");
            await DownloadModelAsync(PartialModelKind, PartialModelQuant, partialPath);
        }

        progress?.Invoke("Loading models…");
        _gpuEnabled = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();
        try
        {
            var opts = new WhisperFactoryOptions { UseGpu = _gpuEnabled };
            _finalFactory   = WhisperFactory.FromPath(finalPath,   opts);
            _partialFactory = WhisperFactory.FromPath(partialPath, opts);
            Logger.Log($"Whisper factories loaded (UseGpu={_gpuEnabled})");
        }
        catch (Exception ex) when (_gpuEnabled)
        {
            Logger.Ex("GPU init failed, falling back to CPU", ex);
            _gpuEnabled = false;
            var opts = new WhisperFactoryOptions { UseGpu = false };
            _finalFactory   = WhisperFactory.FromPath(finalPath,   opts);
            _partialFactory = WhisperFactory.FromPath(partialPath, opts);
        }

        progress?.Invoke("Warming up…");
        await PrewarmAsync();
        progress?.Invoke($"Ready ({(_gpuEnabled ? "GPU" : "CPU")})");
    }

    private static async Task DownloadModelAsync(GgmlType kind, QuantizationType quant, string finalPath)
    {
        using var stream = await WhisperGgmlDownloader.GetGgmlModelAsync(kind, quant);
        var tmp = finalPath + ".part";
        await using (var fs = File.Create(tmp))
            await stream.CopyToAsync(fs);
        File.Move(tmp, finalPath, overwrite: true);
    }

    private async Task PrewarmAsync()
    {
        try
        {
            var silence = new float[16000];
            await using var p1 = BuildFinalProcessor();
            await foreach (var _ in p1.ProcessAsync(silence)) { }
            await using var p2 = BuildPartialProcessor();
            await foreach (var _ in p2.ProcessAsync(silence)) { }
        }
        catch (Exception ex) { Logger.Ex("Prewarm", ex); }
    }

    private WhisperProcessor BuildFinalProcessor() =>
        _finalFactory!.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
            .WithNoContext()
            .WithSingleSegment()
            .WithTemperature(0.0f)
            .WithNoSpeechThreshold(0.5f)
            .Build();

    private WhisperProcessor BuildPartialProcessor() =>
        _partialFactory!.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
            .WithNoContext()
            .WithSingleSegment()
            .WithTemperature(0.0f)
            .WithNoSpeechThreshold(0.6f)
            .Build();

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        if (_finalFactory == null) throw new InvalidOperationException("Model not loaded");

        await _gate.WaitAsync(ct);
        try
        {
            await using var processor = BuildFinalProcessor();
            var sb = new System.Text.StringBuilder();
            await foreach (var seg in processor.ProcessAsync(samples, ct))
                sb.Append(seg.Text);
            return Sanitize(sb.ToString());
        }
        finally { _gate.Release(); }
    }

    public async Task<string> TranscribePartialAsync(float[] samples, CancellationToken ct = default)
    {
        if (_partialFactory == null) throw new InvalidOperationException("Partial model not loaded");

        await _gate.WaitAsync(ct);
        try
        {
            await using var processor = BuildPartialProcessor();
            var sb = new System.Text.StringBuilder();
            await foreach (var seg in processor.ProcessAsync(samples, ct))
                sb.Append(seg.Text);
            return Sanitize(sb.ToString());
        }
        finally { _gate.Release(); }
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

    public void Dispose()
    {
        _finalFactory?.Dispose();
        _partialFactory?.Dispose();
    }
}

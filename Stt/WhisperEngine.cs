using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace SoundToText.Stt;

public sealed class WhisperEngine : IDisposable
{
    private const GgmlType ModelKind = GgmlType.LargeV3Turbo;
    private const QuantizationType ModelQuant = QuantizationType.Q5_0;
    private const string ModelFile = "ggml-large-v3-turbo-q5_0.bin";
    private const string ModelSizeLabel = "~600 MB";

    private WhisperFactory? _factory;
    private string _modelPath = "";
    private bool _gpuEnabled;
    private readonly System.Threading.SemaphoreSlim _gate = new(1, 1);

    public async Task InitializeAsync(Action<string>? progress = null)
    {
        var dir = Path.Combine(AppPaths.LocalDataDir, "models");
        Directory.CreateDirectory(dir);
        _modelPath = Path.Combine(dir, ModelFile);

        if (!File.Exists(_modelPath))
        {
            progress?.Invoke($"Downloading large-v3-turbo Q5_0 ({ModelSizeLabel})…");
            using var stream = await WhisperGgmlDownloader.GetGgmlModelAsync(ModelKind, ModelQuant);
            var tmp = _modelPath + ".part";
            await using (var fs = File.Create(tmp))
                await stream.CopyToAsync(fs);
            File.Move(tmp, _modelPath, overwrite: true);
        }

        progress?.Invoke("Loading model…");

        _gpuEnabled = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();
        try
        {
            _factory = WhisperFactory.FromPath(_modelPath, new WhisperFactoryOptions { UseGpu = _gpuEnabled });
            Logger.Log($"Whisper factory loaded (UseGpu={_gpuEnabled})");
        }
        catch (Exception ex) when (_gpuEnabled)
        {
            Logger.Ex("GPU init failed, falling back to CPU", ex);
            _gpuEnabled = false;
            _factory = WhisperFactory.FromPath(_modelPath, new WhisperFactoryOptions { UseGpu = false });
        }

        progress?.Invoke("Warming up…");
        await PrewarmAsync();
        progress?.Invoke($"Ready ({(_gpuEnabled ? "GPU" : "CPU")})");
    }

    private async Task PrewarmAsync()
    {
        try
        {
            var silence = new float[16000];
            await using var processor = BuildProcessor();
            await foreach (var _ in processor.ProcessAsync(silence)) { }
        }
        catch (Exception ex) { Logger.Ex("Prewarm", ex); }
    }

    private WhisperProcessor BuildProcessor() =>
        _factory!.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
            .WithNoContext()
            .WithSingleSegment()
            .WithTemperature(0.0f)
            .WithNoSpeechThreshold(0.5f)
            .Build();

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        if (_factory == null) throw new InvalidOperationException("Model not loaded");

        await _gate.WaitAsync(ct);
        try
        {
            await using var processor = BuildProcessor();

            var sb = new System.Text.StringBuilder();
            await foreach (var seg in processor.ProcessAsync(samples, ct))
                sb.Append(seg.Text);

            return Sanitize(sb.ToString());
        }
        finally
        {
            _gate.Release();
        }
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

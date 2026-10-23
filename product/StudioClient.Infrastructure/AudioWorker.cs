using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flamoris.StudioClient.Core;

namespace Flamoris.StudioClient.Infrastructure;

public sealed record AudioIdentity(string Sha256, long SizeBytes, string MtimeUtcTicks);
public sealed record WavePoint(double Time, double Min, double Max, double Rms);
public sealed record Chord(double Start, double End, string Label);
public sealed record Tonality(string Key, string Scale, double Similarity);
public sealed record AudioFeatures(string Format, int SchemaVersion, string Algorithm, string Mode,
    AudioIdentity Source, double Duration, int SampleRate, WavePoint[] Waveform, double? Bpm,
    double[] Beats, Tonality? Tonality, Chord[] Chords, string? Preview)
{
    public void Validate()
    {
        if (Format != "flamoris.audio-features" || SchemaVersion != 1 || Mode is not ("prepare" or "analyze") ||
            !double.IsFinite(Duration) || Duration <= 0 || Duration > 7200 || SampleRate != 11025 ||
            Source is null || Source.Sha256.Length != 64 || !Source.Sha256.All(Uri.IsHexDigit) || Source.SizeBytes <= 0 || Source.SizeBytes > AudioWorker.MaxSourceBytes ||
            !long.TryParse(Source.MtimeUtcTicks, out _) || Waveform is null || Waveform.Length > 72001 || Beats is null || Beats.Length > 30000 ||
            Chords is null || Chords.Length > 30000 || Preview is not (null or "preview.wav"))
            throw new InvalidDataException("解析結果の形式・上限が不正です。");
        if (Bpm.HasValue && (!double.IsFinite(Bpm.Value) || Bpm.Value is < 30 or > 300)) throw new InvalidDataException("BPMが不正です。");
        double previous = -1;
        foreach (var point in Waveform)
        {
            if (!double.IsFinite(point.Time) || point.Time < previous || point.Time > Duration + .2 ||
                !double.IsFinite(point.Min) || !double.IsFinite(point.Max) || !double.IsFinite(point.Rms) ||
                point.Min < -1 || point.Max > 1 || point.Min > point.Max || point.Rms is < 0 or > 1)
                throw new InvalidDataException("波形データが不正です。");
            previous = point.Time;
        }
        previous = -1;
        foreach (var beat in Beats)
        { if (!double.IsFinite(beat) || beat <= previous || beat < 0 || beat > Duration) throw new InvalidDataException("拍データが不正です。"); previous = beat; }
        foreach (var chord in Chords)
            if (chord is null || !double.IsFinite(chord.Start) || !double.IsFinite(chord.End) || chord.Start < 0 || chord.End <= chord.Start || chord.End > Duration + .001 ||
                chord.Label.Length > 8) throw new InvalidDataException("コードデータが不正です。");
    }
    public async Task<bool> IsCurrentAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != Source.SizeBytes) return false;
        return StringComparer.OrdinalIgnoreCase.Equals(Source.Sha256, await LocalFiles.FingerprintAsync(path, token));
    }
}

public sealed record AudioJobResult(AudioFeatures Features, string Directory)
{
    public string? PreviewPath => Features.Preview is null ? null : Path.Combine(Directory, "preview.wav");
}

public sealed class AudioWorker(string? python = null, string? workerPath = null, string? cacheRoot = null) : IDisposable
{
    public const long MaxSourceBytes = 8L * 1024 * 1024 * 1024;
    private readonly SemaphoreSlim oneJob = new(1, 1);
    private readonly string interpreter = python ?? ResolvePython();
    private readonly string worker = workerPath ?? Path.Combine(AppContext.BaseDirectory, "audio-worker", "worker.py");
    public string CacheRoot { get; } = cacheRoot ?? Path.Combine(Path.GetTempPath(), "FLAMORIS", "StudioClient", Guid.NewGuid().ToString("N"));
    private static string ResolvePython()
    {
        if (Environment.GetEnvironmentVariable("FLAMORIS_STUDIO_CLIENT_PYTHON") is { Length: > 0 } configured) return configured;
        var packaged = Path.Combine(AppContext.BaseDirectory, "audio-env", "Scripts", "python.exe");
        return OperatingSystem.IsWindows() && File.Exists(packaged) ? packaged : OperatingSystem.IsWindows() ? "python" : "python3";
    }
    public async Task<AudioJobResult> RunAsync(string input, bool analyze, IProgress<(int Percent, string Stage)>? progress, CancellationToken token)
    {
        if (!await oneJob.WaitAsync(0, token)) throw new InvalidOperationException("音源の処理中です。完了かキャンセルを待ってください。");
        string? directory = null;
        try
        {
            input = Path.GetFullPath(input);
            var file = new FileInfo(input);
            if (!file.Exists || file.Length <= 0 || file.Length > MaxSourceBytes) throw new InvalidDataException("音源は8 GiB以下のファイルを選んでください。");
            directory = Path.Combine(CacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromMinutes(30));
            using var process = new Process { StartInfo = new ProcessStartInfo(interpreter)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
            // -I ignores PYTHONUTF8/PYTHONIOENCODING: redirected Windows pipes
            // otherwise inherit the code page and Japanese progress can fail.
            foreach (var arg in new[] { "-I", "-X", "utf8", "-u", worker, analyze ? "analyze" : "prepare", "--input", input, "--output", directory }) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            using var cancel = limit.Token.Register(() => Kill(process));
            var errors = DrainAsync(process.StandardError, limit.Token);
            var status = "解析に失敗しました。Python・NumPy・FFmpegの設定を確認してください。";
            try
            {
                while (await process.StandardOutput.ReadLineAsync(limit.Token) is { } line)
                {
                    if (line.Length > 16384) throw new InvalidDataException("解析応答が上限を超えました。");
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    if (root.TryGetProperty("progress", out var percent))
                        progress?.Report((Math.Clamp(percent.GetInt32(), 0, 100), root.GetProperty("stage").GetString() ?? "処理中"));
                    if (root.TryGetProperty("error", out var error)) status = error.GetString() ?? status;
                }
                await process.WaitForExitAsync(limit.Token); await errors;
                if (process.ExitCode != 0) throw new InvalidDataException(status);
                var bytes = (await LocalFiles.ReadAsync(Path.Combine(directory, "audio.json"), limit.Token)).Bytes;
                var features = JsonSerializer.Deserialize<AudioFeatures>(bytes, new JsonSerializerOptions
                { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    RespectRequiredConstructorParameters = true, MaxDepth = 32 }) ?? throw new InvalidDataException("解析結果が空です。");
                features.Validate();
                if (features.Mode != (analyze ? "analyze" : "prepare") || !await features.IsCurrentAsync(input, limit.Token))
                    throw new IOException("処理中に音源が変わりました。もう一度読み込んでください。");
                if (features.Preview is not null && !File.Exists(Path.Combine(directory, "preview.wav"))) throw new IOException("試聴用音源が見つかりません。");
                var result = new AudioJobResult(features, directory); directory = null; return result;
            }
            finally
            {
                Kill(process); await process.WaitForExitAsync(CancellationToken.None);
                try { await errors; } catch (OperationCanceledException) { }
            }
        }
        finally { if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true); oneJob.Release(); }
    }
    private static void Kill(Process process)
    { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    { var buffer = new char[4096]; while (await reader.ReadAsync(buffer.AsMemory(), token) != 0) { } }
    public void Dispose()
    {
        if (Directory.Exists(CacheRoot))
            try { Directory.Delete(CacheRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        oneJob.Dispose();
    }
}

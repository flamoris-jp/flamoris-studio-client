using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Flamoris.StudioClient.Core;

public static partial class TimingFormats
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, MaxDepth = 32
    };
    public static byte[] SaveProject(TimingProject project) => JsonSerializer.SerializeToUtf8Bytes(project.Validate(), JsonOptions);
    public static TimingProject LoadProject(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("ファイルは16 MiBまでです。");
        return (JsonSerializer.Deserialize<TimingProject>(bytes, JsonOptions) ?? throw new InvalidDataException("空のプロジェクトです。")).Validate();
    }
    public sealed record Exchange(string Format, int SchemaVersion, int Timebase, Guid DocumentId, ImmutableArray<LyricLine> Lines);
    public static byte[] ExportTiming(TimingProject project) => JsonSerializer.SerializeToUtf8Bytes(
        new Exchange("flamoris.lyrics-timing", 1, LyricTime.Timebase, project.Id, project.Validate().Lines), JsonOptions);

    public static ImmutableArray<LyricLine> ImportJson(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("ファイルは16 MiBまでです。");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = json.RootElement;
        if (root.TryGetProperty("format", out var format) && format.GetString() == "flamoris.lyrics-timing")
        {
            var exchange = JsonSerializer.Deserialize<Exchange>(bytes, JsonOptions)!;
            if (exchange.SchemaVersion != 1 || exchange.Timebase != LyricTime.Timebase || exchange.DocumentId == Guid.Empty)
                throw new InvalidDataException("未対応の歌詞交換形式です。");
            TimingProject.ValidateLines(exchange.Lines); return exchange.Lines;
        }
        if (root.TryGetProperty("format", out format) && format.GetString() != "ponpon_export")
            throw new InvalidDataException("未対応の歌詞交換形式です。");
        if (!root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("歌詞のlinesがありません。");
        var result = ImmutableArray.CreateBuilder<LyricLine>();
        foreach (var line in lines.EnumerateArray())
        {
            if (result.Count == 20000) throw new InvalidDataException("歌詞は20,000行までです。");
            var text = line.GetProperty("text").GetString() ?? "";
            long? Read(string primary, string fallback)
            {
                if (!line.TryGetProperty(primary, out var value) && !line.TryGetProperty(fallback, out value)) return null;
                return value.ValueKind == JsonValueKind.Null ? null : LyricTime.FromSeconds(value.GetDecimal());
            }
            var id = line.TryGetProperty("lineId", out var lineId) && lineId.ValueKind == JsonValueKind.String && Guid.TryParse(lineId.GetString(), out var parsedId) ? parsedId : Guid.NewGuid();
            result.Add(new(id, text, Read("pressTime", "start"), Read("releaseTime", "end")));
        }
        var output = result.ToImmutable(); TimingProject.ValidateLines(output); return output;
    }
    public static byte[] ExportPonpon(TimingProject project) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        format = "ponpon_export", version = 1, timeUnit = "seconds",
        project = new { title = "PONPON", audioFile = Path.GetFileName(project.MusicPath) ?? "music.wav", mode = "press_release", createdAt = DateTimeOffset.UtcNow.ToString("O") },
        lines = project.Validate().Lines.Select((l, i) => new { id = i, index = i, lineId = l.Id, text = l.Text, state = l.IsTimed ? "done" : "pending",
            pressTime = l.StartTicks.HasValue ? (decimal?)LyricTime.Seconds(l.StartTicks.Value) : null,
            releaseTime = l.EndTicks.HasValue ? (decimal?)LyricTime.Seconds(l.EndTicks.Value) : null })
    }, JsonOptions);

    public static string ExportSrt(TimingProject project)
    {
        var output = new StringBuilder(); var index = 0;
        foreach (var l in project.Validate().Lines.Where(l => l.IsTimed).OrderBy(l => l.StartTicks).ThenBy(l => l.Id))
        {
            if (l.Text.Contains("\n\n") || l.Text.Contains("\r")) throw new InvalidDataException("SRTの歌詞内に空行を含められません。");
            long Milliseconds(long value) => (value + 5000) / 10000;
            var start = Milliseconds(l.StartTicks!.Value); var end = Milliseconds(l.EndTicks!.Value);
            if (end <= start) throw new InvalidDataException("SRTの1ミリ秒精度では表現できない歌詞があります。");
            output.Append(++index).Append('\n').Append(Stamp(start)).Append(" --> ").Append(Stamp(end)).Append('\n').Append(l.Text).Append("\n\n");
        }
        return output.ToString();
    }
    private static string Stamp(long ms) => $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}";
    [GeneratedRegex(@"^(\d{2,}):(\d{2}):(\d{2}),(\d{3}) --> (\d{2,}):(\d{2}):(\d{2}),(\d{3})$")]
    private static partial Regex TimestampPattern();
    public static ImmutableArray<LyricLine> ImportSrt(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("ファイルは16 MiBまでです。");
        var result = ImmutableArray.CreateBuilder<LyricLine>();
        foreach (var block in Regex.Split(text.TrimStart('\uFEFF').Replace("\r", "").Trim(), @"\n\s*\n"))
        {
            var lines = block.Split('\n');
            if (lines.Length < 3 || !int.TryParse(lines[0], out var cue) || cue <= 0) throw new InvalidDataException("SRTの行番号が不正です。");
            var m = TimestampPattern().Match(lines[1]);
            if (!m.Success) throw new InvalidDataException("SRTの時間指定が不正です。");
            long Time(int offset)
            {
                long N(int i) => long.Parse(m.Groups[i + offset].Value, CultureInfo.InvariantCulture);
                if (N(2) >= 60 || N(3) >= 60) throw new InvalidDataException("SRTの分・秒が不正です。");
                return checked((N(1) * 3600000 + N(2) * 60000 + N(3) * 1000 + N(4)) * 10000);
            }
            result.Add(new(Guid.NewGuid(), string.Join('\n', lines.Skip(2)), Time(0), Time(4)));
        }
        var output = result.ToImmutable(); TimingProject.ValidateLines(output); return output;
    }
}

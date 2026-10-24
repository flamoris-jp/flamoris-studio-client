using System.Collections.Immutable;
using System.Text;
using Flamoris.StudioClient.Core;

namespace Flamoris.StudioClient.Infrastructure;

public sealed record ImportedLyrics(string Text, ImmutableArray<LyricLine> Lines, bool PreserveTiming);

public static class LyricsImport
{
    public const int MaxCharacters = 4_000_000;
    public static ImmutableArray<LyricLine> FromText(string text)
    {
        if (text.Length > MaxCharacters) throw new InvalidDataException("歌詞の入力は4,000,000文字までです。");
        var builder = ImmutableArray.CreateBuilder<LyricLine>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (builder.Count == 20000 || line.Length > 4096) throw new InvalidDataException("歌詞は20,000行、1行4,096文字までです。");
            builder.Add(new(Guid.NewGuid(), line));
        }
        var result = builder.ToImmutable(); TimingProject.ValidateLines(result); return result;
    }

    public static string SingleFile(string[] paths)
    {
        if (paths.Length != 1) throw new InvalidDataException("歌詞ファイルは1つずつ選んでください。");
        var path = paths[0];
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal))
            throw new InvalidDataException("単一のローカル歌詞ファイルを選んでください。");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".txt" or ".json" or ".srt")) throw new InvalidDataException("対応形式はTXT / JSON / SRTです。");
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("フォルダーやリンクは読み込めません。元の歌詞ファイルを選んでください。");
        return path;
    }

    public static async Task<ImportedLyrics> LoadAsync(string path)
    {
        path = SingleFile([path]);
        var read = await LocalFiles.ReadAsync(path);
        return Decode(Path.GetExtension(path), read.Bytes);
    }

    public static ImportedLyrics Decode(string extension, byte[] bytes)
    {
        if (bytes.Length > TimingFormats.MaxBytes) throw new InvalidDataException("ファイルは16 MiBまでです。");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidDataException("文字コードを読み取れません。UTF-8で保存してください。"); }
        var ext = extension.ToLowerInvariant();
        var lines = ext switch
        {
            ".txt" => FromText(text),
            ".srt" => TimingFormats.ImportSrt(text),
            ".json" => TimingFormats.ImportJson(Encoding.UTF8.GetBytes(text)),
            _ => throw new InvalidDataException("対応形式はTXT / JSON / SRTです。")
        };
        if (ext != ".txt") text = string.Join('\n', lines.Select(l => l.Text));
        if (text.Length > MaxCharacters) throw new InvalidDataException("歌詞の入力は4,000,000文字までです。");
        return new(text, lines, ext != ".txt");
    }
}

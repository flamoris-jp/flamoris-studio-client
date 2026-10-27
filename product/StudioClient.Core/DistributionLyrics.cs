using System.Text.RegularExpressions;

namespace Flamoris.StudioClient.Core;

public sealed record LyricsReview(string Text, int ChangeCount, IReadOnlyList<string> Changes,
    int WarningCount, IReadOnlyList<string> Warnings);

// Conservative, offline formatting. Spelling, names, acronyms and sung parentheses
// require human review; this is not a store submission or an approval validator.
public static class DistributionLyrics
{
    public const int MaxCharacters = 4_000_000;
    public const int MaxLines = 20_000;
    public const int MaxLineCharacters = 4096;
    public const int MaxDetails = 200;
    private static readonly TimeSpan MatchLimit = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Section = new(
        @"^[\[［]\s*(?:(?:pre[ -]?)?chorus|verse|intro|outro|bridge|hook|refrain|interlude|instrumental|breakdown|break|solo|post[ -]?chorus|drop|ending|サビ|大サビ|イントロ|アウトロ|間奏|Aメロ|Bメロ)(?:\s*\d+)?\s*[\]］]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchLimit);
    private static readonly Regex Capitals = new(@"\b[A-Z]{2,}\b", RegexOptions.CultureInvariant, MatchLimit);
    private static readonly Regex NonLyrics = new(
        @"https?://|www\.|\b(?:repeat|chorus)\s*(?:[x×]\s*\d+|\d+\s*times)|(?:繰り返し|サビ)\s*[x×]?\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchLimit);
    private const string TrailingMarks = ".,;:、。，．；：…‥";
    private const string ClosingMarks = "\"'”’」』）)]】";

    public static string FromProject(TimingProject project)
    {
        project.Validate();
        // Reattach only originally skipped annotation-only rows to their next
        // surviving ID. Never restore deleted ordinary lyrics or stale edits.
        var current = project.Lines.ToDictionary(l => l.Id);
        var before = new Dictionary<Guid, List<string>>();
        var pending = new List<string>();
        foreach (var line in project.OriginalLines)
        {
            if (current.ContainsKey(line.Id))
            {
                if (pending.Count > 0) { before[line.Id] = [.. pending]; pending.Clear(); }
            }
            else if (LyricText.IsAnnotation(line.Text)) pending.Add(line.Text);
        }
        var output = new List<string>();
        foreach (var line in project.Lines)
        {
            if (before.TryGetValue(line.Id, out var annotations)) output.AddRange(annotations);
            output.Add(line.Text);
        }
        output.AddRange(pending);
        var text = string.Join('\n', output);
        Validate(text);
        return text;
    }

    public static LyricsReview Format(string source)
    {
        Validate(source);
        var changes = new List<string>(); var count = 0;
        void Changed(int line, string message)
        { count++; if (changes.Count < MaxDetails) changes.Add($"元の{line}行目: {message}"); }
        var output = new List<string>();
        var boundary = false; var number = 0;
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } raw)
        {
            number++;
            var text = raw.Trim().TrimStart('\uFEFF').Trim();
            if (text != raw) Changed(number, "前後の空白・BOMを除去");
            if (Section.IsMatch(text))
            { Changed(number, "構成タグを節区切りに変換"); boundary = output.Count > 0; continue; }
            if (text.Length == 0) { boundary = output.Count > 0; continue; }
            var trimmed = RemoveTrailingMarks(text);
            if (trimmed != text) { text = trimmed; Changed(number, "行末の句点・カンマなどを除去"); }
            if (text.Length == 0) continue;
            var start = FirstLetter(text);
            if (start >= 0 && text[start] is >= 'a' and <= 'z')
            { text = text[..start] + char.ToUpperInvariant(text[start]) + text[(start + 1)..]; Changed(number, "行頭の英字を大文字に変更"); }
            if (boundary) { output.Add(""); boundary = false; }
            output.Add(text);
        }
        var result = string.Join('\n', output);
        // Blank-line normalization counts as one operation rather than claiming
        // that each stanza boundary can be inferred from the audio.
        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        if (normalized != result && count == 0) Changed(1, "空行・改行を整理");
        var review = Inspect(result);
        return review with { ChangeCount = count, Changes = changes };
    }

    public static LyricsReview Inspect(string text)
    {
        Validate(text);
        var warnings = new List<string>(); var count = 0; var number = 0; var previousBlank = false;
        void Warn(string message)
        { count++; if (warnings.Count < MaxDetails) warnings.Add($"{number}行目: {message}"); }
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            number++; var line = raw.Trim();
            if (line.Length == 0)
            {
                if (previousBlank || number == 1) Warn("余分な空行の可能性があります。節の区切り以外は削除してください");
                previousBlank = true; continue;
            }
            previousBlank = false;
            if (line.Contains('[') || line.Contains(']') || line.Contains('［') || line.Contains('］'))
                Warn("角括弧の注記を確認してください。歌詞以外なら削除してください");
            if (NonLyrics.IsMatch(line)) Warn("URL・繰り返し省略などの可能性があります。実際に歌う歌詞を確認してください");
            if (Capitals.IsMatch(line)) Warn("全大文字の単語があります。略語・固有名詞か確認してください");
            if (LyricText.IsAnnotation(line) && !Section.IsMatch(line))
                Warn("括弧だけの行です。歌うコーラスは残し、演出指示は削除してください");
            if (FirstLetter(line) is var index && index >= 0 && line[index] is >= 'a' and <= 'z')
                Warn("行頭の英字が小文字です");
            if (RemoveTrailingMarks(line) != line) Warn("行末に句点・カンマなどが残っています");
        }
        return new(text, 0, [], count, warnings);
    }

    public static void Validate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxCharacters) throw new InvalidDataException("配信用歌詞は4,000,000文字までです。");
        using var reader = new StringReader(text); var count = 0;
        while (reader.ReadLine() is { } line)
            if (++count > MaxLines || line.Length > MaxLineCharacters)
                throw new InvalidDataException("配信用歌詞は20,000行、1行4,096文字までです。");
    }

    private static int FirstLetter(string text)
    {
        // Stop at a Japanese/non-Latin letter: do not capitalize English words
        // in the middle of a Japanese line. Opening quotes/brackets are skipped.
        for (var i = 0; i < text.Length; i++) if (char.IsLetter(text[i])) return i;
        return -1;
    }

    private static string RemoveTrailingMarks(string text)
    {
        // Keep the closing quotation/parenthesis, e.g. (Hello.) -> (Hello).
        var end = text.Length;
        while (end > 0 && ClosingMarks.Contains(text[end - 1])) end--;
        var body = text[..end].TrimEnd(); var suffix = text[end..];
        while (body.Length > 0 && TrailingMarks.Contains(body[^1])) body = body[..^1].TrimEnd();
        return body + suffix;
    }
}

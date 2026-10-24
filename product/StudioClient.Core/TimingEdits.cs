using System.Collections.Immutable;
using System.Globalization;

namespace Flamoris.StudioClient.Core;

public static class TimingEdits
{
    // All edits are built from one snapshot and validated before session publication.
    public static TimingProject Shift(TimingProject project, IReadOnlySet<Guid> targets, long delta, bool followPrevious)
    {
        if (delta == 0) throw new InvalidDataException("調整量は0より大きくしてください。");
        if (!project.Lines.Any(l => targets.Contains(l.Id) && l.IsTimed))
            throw new InvalidOperationException("対象に時間を記録した歌詞がありません。");
        var lines = project.Lines.Select(l => targets.Contains(l.Id) && l.IsTimed
            ? l with { StartTicks = checked(l.StartTicks!.Value + delta), EndTicks = checked(l.EndTicks!.Value + delta) } : l).ToImmutableArray();
        if (followPrevious) lines = Align(lines, targets);
        return Finish(project, lines);
    }

    public static TimingProject Range(TimingProject project, Guid id, long start, long end, bool followPrevious, string? text = null)
    {
        var i = project.Lines.FindIndex(l => l.Id == id);
        if (i < 0) throw new InvalidOperationException("歌詞が見つかりません。");
        var old = project.Lines[i];
        var lines = project.Lines.SetItem(i, old with { StartTicks = start, EndTicks = end, Text = text ?? old.Text });
        if (followPrevious && old.StartTicks != start) lines = Align(lines, new HashSet<Guid> { id });
        return Finish(project, lines);
    }

    public static TimingProject AlignPrevious(TimingProject project, IReadOnlySet<Guid> targets)
    {
        if (!project.Lines.Where((l, i) => i > 0 && targets.Contains(l.Id) && l.IsTimed && project.Lines[i - 1].IsTimed).Any())
            throw new InvalidOperationException("前の行と対象行の両方に時間が必要です。先頭行・未記録行は対象外です。");
        return Finish(project, Align(project.Lines, targets));
    }

    private static ImmutableArray<LyricLine> Align(ImmutableArray<LyricLine> snapshot, IReadOnlySet<Guid> targets)
    {
        var result = snapshot.ToBuilder();
        for (var i = 1; i < snapshot.Length; i++)
        {
            var current = snapshot[i]; var previous = snapshot[i - 1];
            if (!targets.Contains(current.Id) || !current.IsTimed || !previous.IsTimed) continue;
            if (current.StartTicks <= previous.StartTicks)
                throw new InvalidDataException("前行の終了を合わせると長さが0以下になります。開始時刻を確認してください。");
            result[i - 1] = previous with { EndTicks = current.StartTicks };
        }
        return result.ToImmutable();
    }

    private static TimingProject Finish(TimingProject project, ImmutableArray<LyricLine> lines)
    {
        TimingProject.ValidateLines(lines);
        return project.Lines.SequenceEqual(lines) ? project : project with { Lines = lines };
    }

    // ComboBox values are milliseconds; manual input may explicitly opt into seconds.
    public static long ParseAdjustment(string input)
    {
        var text = input.Trim(); decimal multiplier;
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) { text = text[..^2].Trim(); multiplier = 10000; }
        else if (text.EndsWith("秒", StringComparison.Ordinal)) { text = text[..^1].Trim(); multiplier = LyricTime.Timebase; }
        else if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase)) { text = text[..^1].Trim(); multiplier = LyricTime.Timebase; }
        else multiplier = 10000;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw new InvalidDataException("正の調整量を入力してください。例：10ms、0.01秒（単位なしはms）。");
        var ticks = checked((long)decimal.Round(value * multiplier, 0, MidpointRounding.AwayFromZero));
        if (ticks <= 0 || ticks > TimeSpan.FromHours(24).Ticks) throw new InvalidDataException("調整量が範囲外です。");
        return ticks;
    }
}

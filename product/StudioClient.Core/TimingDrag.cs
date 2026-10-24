namespace Flamoris.StudioClient.Core;

public enum TimingDragKind { Move, Start, End }

public static class TimingDrag
{
    public static (long Start, long End) Range(LyricLine line, TimingDragKind kind, double deltaSeconds)
    {
        if (!line.IsTimed || !double.IsFinite(deltaSeconds)) throw new InvalidDataException("ドラッグ位置を読み取れません。");
        var start = line.StartTicks!.Value; var end = line.EndTicks!.Value;
        var limit = TimeSpan.FromHours(24).Ticks;
        if (start < 0 || end <= start || end > limit) throw new InvalidDataException("歌詞の時間範囲が不正です。");
        // Clamp before conversion/addition, including pointer excursions outside the view.
        var seconds = Math.Clamp(deltaSeconds, -86400, 86400);
        var delta = LyricTime.FromSeconds((decimal)seconds);
        return kind switch
        {
            TimingDragKind.Move => (start + Math.Clamp(delta, -start, limit - end), end + Math.Clamp(delta, -start, limit - end)),
            TimingDragKind.Start => (Math.Clamp(start + delta, 0, end - 1), end),
            TimingDragKind.End => (start, Math.Clamp(end + delta, start + 1, limit)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Flamoris.StudioClient.Core;

public sealed record LyricLine(Guid Id, string Text,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? StartTicks = null,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? EndTicks = null)
{
    [JsonIgnore] public bool IsTimed => StartTicks.HasValue && EndTicks.HasValue;
}

public sealed record TimingProject(int SchemaVersion, Guid Id, string? MusicPath, string? VocalPath,
    ImmutableArray<LyricLine> Lines, ImmutableArray<LyricLine> OriginalLines)
{
    public const int Version = 1;
    public static TimingProject Empty() => new(Version, Guid.NewGuid(), null, null, [], []);
    public TimingProject Validate()
    {
        if (SchemaVersion != Version || Id == Guid.Empty) throw new InvalidDataException("未対応のプロジェクト形式です。");
        ValidateLines(Lines); ValidateLines(OriginalLines);
        foreach (var path in new[] { MusicPath, VocalPath })
            if (path is not null && (path.Length > 32767 || !Path.IsPathFullyQualified(path)))
                throw new InvalidDataException("音源の参照が不正です。");
        return this;
    }
    public static void ValidateLines(ImmutableArray<LyricLine> lines)
    {
        if (lines.IsDefault || lines.Length > 20000) throw new InvalidDataException("歌詞は20,000行までです。");
        var ids = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if (line is null || line.Id == Guid.Empty || !ids.Add(line.Id) || string.IsNullOrWhiteSpace(line.Text) ||
                line.Text.Length > 4096 || line.StartTicks.HasValue != line.EndTicks.HasValue ||
                line.IsTimed && (line.StartTicks < 0 || line.EndTicks <= line.StartTicks || line.EndTicks > TimeSpan.FromHours(24).Ticks))
                throw new InvalidDataException("歌詞のID・文字・時間範囲が不正です。");
        }
    }
}

public static class LyricTime
{
    public const int Timebase = 10_000_000;
    public static long FromSeconds(decimal seconds) => checked((long)decimal.Round(seconds * Timebase, 0, MidpointRounding.AwayFromZero));
    public static decimal Seconds(long ticks) => (decimal)ticks / Timebase;
    public static string Display(long? ticks) => ticks.HasValue ? TimeSpan.FromTicks(ticks.Value).ToString(@"mm\:ss\.fff") : "--";
}

// The only persistent editing path used by keyboard, pointer and inspector.
public sealed class TimingSession
{
    private readonly LinkedList<TimingProject> undo = new();
    private readonly Stack<TimingProject> redo = new();
    public TimingProject Project { get; private set; } = TimingProject.Empty();
    public long Revision { get; private set; }
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;
    public event Action? Changed;
    public void Open(TimingProject project)
    {
        Project = project.Validate(); undo.Clear(); redo.Clear(); Revision++; Changed?.Invoke();
    }
    public bool Execute(Func<TimingProject, TimingProject> edit, long? expectedRevision = null)
    {
        if (expectedRevision.HasValue && expectedRevision != Revision) throw new InvalidOperationException("編集が更新されています。もう一度選択してください。");
        var next = edit(Project).Validate();
        if (next == Project) return false;
        undo.AddLast(Project); if (undo.Count > 200) undo.RemoveFirst();
        Project = next; redo.Clear(); Revision++; Changed?.Invoke(); return true;
    }
    public void SetLyrics(string text) => Execute(p =>
    {
        var lines = text.Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length != 0)
            .Select(s => new LyricLine(Guid.NewGuid(), s)).ToImmutableArray();
        return p with { Lines = lines, OriginalLines = lines };
    });
    public void SetRange(Guid id, long start, long end, long? expectedRevision = null) => Execute(p =>
    {
        var i = p.Lines.FindIndex(l => l.Id == id);
        if (i < 0) throw new InvalidOperationException("歌詞が見つかりません。");
        return p with { Lines = p.Lines.SetItem(i, p.Lines[i] with { StartTicks = start, EndTicks = end }) };
    }, expectedRevision);
    public void Undo()
    {
        if (undo.Last is not { } last) return;
        redo.Push(Project); Project = last.Value; undo.RemoveLast(); Revision++; Changed?.Invoke();
    }
    public void Redo()
    {
        if (!redo.TryPop(out var next)) return;
        undo.AddLast(Project); Project = next; Revision++; Changed?.Invoke();
    }
}

internal static class ArrayExtensions
{
    public static int FindIndex<T>(this ImmutableArray<T> array, Func<T, bool> predicate)
    { for (var i = 0; i < array.Length; i++) if (predicate(array[i])) return i; return -1; }
}

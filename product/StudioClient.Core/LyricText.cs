using System.Collections.Immutable;

namespace Flamoris.StudioClient.Core;

public static class LyricText
{
    // Remove only complete annotation groups, never an inline lyric parenthesis.
    public static bool IsAnnotation(string text)
    {
        var value = text.AsSpan().Trim();
        if (value.IsEmpty) return false;
        while (!value.IsEmpty)
        {
            var close = value[0] switch { '[' => ']', '(' => ')', '（' => '）', '［' => '］', _ => '\0' };
            if (close == '\0') return false;
            var depth = 0; var end = -1;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == value[0]) depth++;
                else if (value[i] == close && --depth == 0) { end = i; break; }
            }
            if (end < 0) return false;
            value = value[(end + 1)..].TrimStart();
        }
        return true;
    }

    public static ImmutableArray<LyricLine> ForTiming(ImmutableArray<LyricLine> lines) =>
        lines.Any(l => IsAnnotation(l.Text)) ? lines.Where(l => !IsAnnotation(l.Text)).ToImmutableArray() : lines;
}

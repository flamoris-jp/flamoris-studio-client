using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;

namespace Flamoris.StudioClient.App;

public sealed class TimingSurface : FrameworkElement
{
    private ImmutableArray<LyricLine> lines = [];
    private WavePoint[] waveform = [];
    private double[] beats = [];
    private double duration = 10, scale = 80, offset, viewport = 1200;
    private long revision;
    private (LyricLine Line, double X, TimingDragKind Kind, long Revision, double Scale)? drag;
    private (long Start, long End)? draft;
    private ImmutableArray<LyricLine>? draftLines;
    public Func<Guid, long, long, ImmutableArray<LyricLine>>? PreviewEdit { get; set; }
    public IReadOnlySet<Guid> SelectedIds { get; set; } = new HashSet<Guid>();
    private double LaneHeight => Math.Clamp(ActualHeight * .12, 16, 40);
    private double LaneTop => ActualHeight - 3 * (LaneHeight + 3) - 5;
    public long Playhead { get; set; }
    public Guid? Selected { get; set; }
    public event Action<Guid>? SelectLine;
    public event Action<long>? Seek;
    public event Action<Guid, long, long, long>? CommitRange;
    public bool Fit { get; set; }
    public event Action<Exception>? InteractionFailed;
    internal bool IsDragging => drag.HasValue;
    internal double LineCenterY(int index) => LaneTop + index % 3 * (LaneHeight + 3) + LaneHeight / 2;
    public void SetData(ImmutableArray<LyricLine> lyrics, AudioFeatures? audio, double seconds, long currentRevision)
    {
        if (revision != currentRevision) CancelGesture();
        lines = lyrics; waveform = audio?.Waveform ?? []; beats = audio?.Beats ?? [];
        duration = Math.Max(1, seconds); revision = currentRevision;
        InvalidateMeasure(); InvalidateVisual();
    }
    public void SetViewport(double start, double width)
    { offset = double.IsFinite(start) ? Math.Max(0, start) : 0; viewport = double.IsFinite(width) && width > 0 ? width : 1200; InvalidateVisual(); }
    public void Zoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        CancelGesture(); scale = Math.Clamp(scale * factor, 10, 500); InvalidateMeasure(); InvalidateVisual();
    }
    private double Scale => Fit ? Math.Max(1, ActualWidth) / duration : scale;
    public double TimeToPixels(long ticks) => (double)LyricTime.Seconds(ticks) * Scale;
    protected override Size MeasureOverride(Size availableSize) => new(Fit ? (double.IsFinite(availableSize.Width) ? availableSize.Width : 1000) : Math.Max(1000, duration * scale + 40), Height);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double left = Fit ? 0 : offset, right = Fit ? ActualWidth : Math.Min(ActualWidth, left + viewport);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 35, 46)), null, new Rect(left, 0, Math.Max(0, right - left), ActualHeight));
        if (right <= left) return;
        var step = new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 }.FirstOrDefault(s => s * Scale >= 85, 3600);
        for (var seconds = Math.Floor(left / Scale / step) * step; seconds * Scale < right; seconds += step)
        {
            var x = seconds * Scale;
            dc.DrawLine(new Pen(Brushes.SlateGray, .5), new Point(x, 24), new Point(x, ActualHeight));
            Label(dc, LyricTime.Display(LyricTime.FromSeconds((decimal)seconds)), x + 3, 3, 10, Brushes.LightSlateGray);
        }
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(98, 195, 182)), 1);
        var start = Math.Max(0, (int)(left / Scale * 10) - 2);
        var stop = Math.Min(waveform.Length, (int)(right / Scale * 10) + 3);
        var waveBottom = LaneTop - 8;
        var waveCenter = (24 + waveBottom) / 2;
        var amplitude = Math.Max(5, (waveBottom - 24) * .42);
        for (var i = start; i < stop; i++)
        {
            var p = waveform[i]; var x = p.Time * Scale;
            dc.DrawLine(pen, new Point(x, waveCenter - p.Max * amplitude), new Point(x, waveCenter - p.Min * amplitude));
            dc.DrawLine(new Pen(Brushes.Goldenrod, 1), new Point(x, waveBottom), new Point(x, waveBottom - p.Rms * amplitude));
        }
        foreach (var beat in beats)
            if (beat * Scale >= left && beat * Scale <= right)
                dc.DrawLine(new Pen(Brushes.DarkSlateGray, .5), new Point(beat * Scale, 25), new Point(beat * Scale, waveBottom));
        var rendered = draftLines ?? lines;
        for (var i = 0; i < rendered.Length; i++)
        {
            var l = rendered[i]; if (!l.IsTimed) continue;
            var range = drag?.Line.Id == l.Id && draft.HasValue ? draft.Value : (l.StartTicks!.Value, l.EndTicks!.Value);
            var x = (double)LyricTime.Seconds(range.Item1) * Scale;
            var width = Math.Max(2, (double)LyricTime.Seconds(range.Item2 - range.Item1) * Scale);
            if (x + width < left || x > right) continue;
            var rect = new Rect(x, LaneTop + i % 3 * (LaneHeight + 3), width, LaneHeight);
            dc.DrawRoundedRectangle((l.Id == Selected || SelectedIds.Contains(l.Id)) ? Brushes.Teal : Brushes.DarkSlateBlue, new Pen(Brushes.LightSlateGray, .7), rect, 3, 3);
            dc.PushClip(new RectangleGeometry(rect)); Label(dc, l.Text, Math.Max(x + 5, left + 2), rect.Y + 2, Math.Min(14, LaneHeight - 4), Brushes.White); dc.Pop();
        }
        var cursor = (double)LyricTime.Seconds(Playhead) * Scale;
        if (cursor >= left && cursor <= right) dc.DrawLine(new Pen(Brushes.Coral, 1.5), new Point(cursor, 0), new Point(cursor, ActualHeight));
    }
    private void Label(DrawingContext dc, string value, double x, double y, double size, Brush brush)
        => dc.DrawText(new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Input(() =>
        {
            Focus(); var point = e.GetPosition(this);
            if (!BeginDrag(point)) Seek?.Invoke(Math.Clamp(LyricTime.FromSeconds((decimal)(point.X / Scale)), 0, TimeSpan.FromHours(24).Ticks));
        });
        e.Handled = true;
    }
    internal bool BeginDrag(Point point)
    {
        CancelGesture();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!line.IsTimed || Math.Abs(point.Y - LineCenterY(i)) > LaneHeight / 2) continue;
            var left = TimeToPixels(line.StartTicks!.Value); var right = TimeToPixels(line.EndTicks!.Value);
            if (point.X < left - 3 || point.X > right + 3) continue;
            // For very short boxes, choose the nearest end rather than always the left.
            var leftDistance = Math.Abs(point.X - left); var rightDistance = Math.Abs(point.X - right);
            var kind = Math.Min(leftDistance, rightDistance) < 6
                ? leftDistance <= rightDistance ? TimingDragKind.Start : TimingDragKind.End : TimingDragKind.Move;
            var initialRevision = revision;
            Selected = line.Id; SelectLine?.Invoke(line.Id);
            if (revision != initialRevision) return true;
            drag = (line, point.X, kind, initialRevision, Scale);
            draft = (line.StartTicks.Value, line.EndTicks.Value);
            if (!CaptureMouse()) { CancelGesture(); return true; }
            InvalidateVisual(); return true;
        }
        return false;
    }
    protected override void OnMouseMove(MouseEventArgs e) => Input(() => UpdateDrag(e.GetPosition(this)));
    internal void UpdateDrag(Point point)
    {
        if (drag is not { } gesture) return;
        try
        {
            var range = TimingDrag.Range(gesture.Line, gesture.Kind, (point.X - gesture.X) / gesture.Scale);
            // Publish a preview only after validating the entire candidate, including
            // predecessor-end follow. Invalid candidates cannot reach render or commit.
            var preview = PreviewEdit is { } edit ? edit(gesture.Line.Id, range.Start, range.End)
                : lines.SetItem(lines.IndexOf(gesture.Line), gesture.Line with { StartTicks = range.Start, EndTicks = range.End });
            TimingProject.ValidateLines(preview);
            draft = range; draftLines = preview; InvalidateVisual();
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        {
            // A rejected gesture is canceled in full; releasing cannot commit its old draft.
            CancelGesture(); InteractionFailed?.Invoke(error);
        }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { Input(CompleteDrag); e.Handled = true; }
    internal void CompleteDrag()
    {
        var gesture = drag; var final = draft;
        CancelGesture();
        if (gesture.HasValue && final.HasValue &&
            (gesture.Value.Line.StartTicks != final.Value.Start || gesture.Value.Line.EndTicks != final.Value.End))
            CommitRange?.Invoke(gesture.Value.Line.Id, final.Value.Start, final.Value.End, gesture.Value.Revision);
    }
    private void Input(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { CancelGesture(); InteractionFailed?.Invoke(error); }
    }
    public void CancelGesture() { drag = null; draft = null; draftLines = null; ReleaseMouseCapture(); InvalidateVisual(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { drag = null; draft = null; draftLines = null; InvalidateVisual(); base.OnLostMouseCapture(e); }
}

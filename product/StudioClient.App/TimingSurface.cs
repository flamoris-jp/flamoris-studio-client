using System.Collections.Immutable;
using System.Globalization;
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
    private (LyricLine Line, double X, string Kind, long Revision)? drag;
    private (long Start, long End)? draft;
    public long Playhead { get; set; }
    public Guid? Selected { get; set; }
    public event Action<Guid>? SelectLine;
    public event Action<long>? Seek;
    public event Action<Guid, long, long, long>? CommitRange;
    public bool Fit { get; set; }
    public void SetData(ImmutableArray<LyricLine> lyrics, AudioFeatures? audio, double seconds, long currentRevision)
    {
        lines = lyrics; waveform = audio?.Waveform ?? []; beats = audio?.Beats ?? [];
        duration = Math.Max(1, seconds); revision = currentRevision;
        InvalidateMeasure(); InvalidateVisual();
    }
    public void SetViewport(double start, double width) { offset = start; viewport = width > 0 ? width : 1200; InvalidateVisual(); }
    public void Zoom(double factor) { scale = Math.Clamp(scale * factor, 10, 500); InvalidateMeasure(); InvalidateVisual(); }
    private double Scale => Fit ? Math.Max(1, ActualWidth) / duration : scale;
    public double TimeToPixels(long ticks) => (double)LyricTime.Seconds(ticks) * Scale;
    protected override Size MeasureOverride(Size availableSize) => new(Fit ? (double.IsFinite(availableSize.Width) ? availableSize.Width : 1000) : Math.Max(1000, duration * scale + 40), Height);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double left = Fit ? 0 : offset, right = Fit ? ActualWidth : Math.Min(ActualWidth, left + viewport);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 35, 46)), null, new Rect(left, 0, Math.Max(0, right - left), ActualHeight));
        if (right <= left) return;
        var step = Scale >= 100 ? 1 : Scale >= 30 ? 5 : 10;
        for (var seconds = Math.Floor(left / Scale / step) * step; seconds * Scale < right; seconds += step)
        {
            var x = seconds * Scale;
            dc.DrawLine(new Pen(Brushes.SlateGray, .5), new Point(x, 24), new Point(x, ActualHeight));
            Label(dc, LyricTime.Display(LyricTime.FromSeconds((decimal)seconds)), x + 3, 3, 10, Brushes.LightSlateGray);
        }
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(98, 195, 182)), 1);
        var start = Math.Max(0, (int)(left / Scale * 10) - 2);
        var stop = Math.Min(waveform.Length, (int)(right / Scale * 10) + 3);
        for (var i = start; i < stop; i++)
        {
            var p = waveform[i]; var x = p.Time * Scale;
            dc.DrawLine(pen, new Point(x, 77 - p.Max * 42), new Point(x, 77 - p.Min * 42));
            dc.DrawLine(new Pen(Brushes.Goldenrod, 1), new Point(x, 124), new Point(x, 124 - p.Rms * 35));
        }
        foreach (var beat in beats)
            if (beat * Scale >= left && beat * Scale <= right)
                dc.DrawLine(new Pen(Brushes.DarkSlateGray, .5), new Point(beat * Scale, 25), new Point(beat * Scale, 125));
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i]; if (!l.IsTimed) continue;
            var range = drag?.Line.Id == l.Id && draft.HasValue ? draft.Value : (l.StartTicks!.Value, l.EndTicks!.Value);
            var x = (double)LyricTime.Seconds(range.Item1) * Scale;
            var width = Math.Max(2, (double)LyricTime.Seconds(range.Item2 - range.Item1) * Scale);
            if (x + width < left || x > right) continue;
            var rect = new Rect(x, 135 + i % 3 * 24, width, 21);
            dc.DrawRoundedRectangle(l.Id == Selected ? Brushes.Teal : Brushes.DarkSlateBlue, new Pen(Brushes.LightSlateGray, .7), rect, 3, 3);
            dc.PushClip(new RectangleGeometry(rect)); Label(dc, l.Text, Math.Max(x + 5, left + 2), rect.Y + 3, 11, Brushes.White); dc.Pop();
        }
        var cursor = (double)LyricTime.Seconds(Playhead) * Scale;
        if (cursor >= left && cursor <= right) dc.DrawLine(new Pen(Brushes.Coral, 1.5), new Point(cursor, 0), new Point(cursor, ActualHeight));
    }
    private void Label(DrawingContext dc, string value, double x, double y, double size, Brush brush)
        => dc.DrawText(new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var l = lines[i]; if (!l.IsTimed || p.Y < 135 + i % 3 * 24 || p.Y > 156 + i % 3 * 24) continue;
            var left = (double)LyricTime.Seconds(l.StartTicks!.Value) * Scale; var right = (double)LyricTime.Seconds(l.EndTicks!.Value) * Scale;
            if (p.X < left - 3 || p.X > right + 3) continue;
            Selected = l.Id; SelectLine?.Invoke(l.Id);
            drag = (l, p.X, Math.Abs(p.X - left) < 6 ? "left" : Math.Abs(p.X - right) < 6 ? "right" : "move", revision);
            draft = (l.StartTicks.Value, l.EndTicks.Value); CaptureMouse(); InvalidateVisual(); e.Handled = true; return;
        }
        Seek?.Invoke(Math.Max(0, LyricTime.FromSeconds((decimal)(p.X / Scale)))); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag is not { } d) return;
        var delta = LyricTime.FromSeconds((decimal)((e.GetPosition(this).X - d.X) / Scale));
        var start = d.Line.StartTicks!.Value; var end = d.Line.EndTicks!.Value;
        if (d.Kind == "move") { delta = Math.Max(delta, -start); start += delta; end += delta; }
        else if (d.Kind == "left") start = Math.Clamp(start + delta, 0, end - 1);
        else end = Math.Clamp(end + delta, start + 1, TimeSpan.FromHours(24).Ticks);
        draft = (start, end); InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var d = drag; var final = draft; drag = null; draft = null; ReleaseMouseCapture(); InvalidateVisual();
        if (d.HasValue && final.HasValue) CommitRange?.Invoke(d.Value.Line.Id, final.Value.Start, final.Value.End, d.Value.Revision);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { drag = null; draft = null; InvalidateVisual(); base.OnLostMouseCapture(e); }
}

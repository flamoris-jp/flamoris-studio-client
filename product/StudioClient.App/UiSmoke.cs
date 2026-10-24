using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flamoris.StudioClient.Core;

namespace Flamoris.StudioClient.App;

public partial class MainWindow
{
    // Explicit CI-only mode; uses synthetic text and temporary files, never user documents.
    private async Task RunUiSmoke()
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("UI smoke: " + name);
            checks.Add(name);
        }
        KeyEventArgs KeyEvent(Key key, RoutedEvent routed) => new(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(this)!, Environment.TickCount, key) { RoutedEvent = routed };
        CaptureSmoke(this, "startup.png");
        session.SetLyrics("現在の歌詞 — 宇宙へ進もう\nNEXTも同じ大きさで読む\nその次の歌詞\n最後の歌詞");
        var ids = session.Project.Lines.Select(l => l.Id).ToArray();
        session.SetRange(ids[0], 10_000_000, 20_000_000);
        session.SetRange(ids[1], 30_000_000, 40_000_000);
        LyricsGrid.SelectedItems.Add(((LyricRow[])LyricsGrid.ItemsSource)[1]);
        Check(selectedIds.SetEquals(ids.Take(2)) && Timeline.SelectedIds.SetEquals(ids.Take(2)), "grid multi-selection synchronizes timeline and edit targets");
        var before = session.Project;
        ShiftLater_Click(this, new RoutedEventArgs());
        Check(session.Project.Lines[0].StartTicks == 10_100_000 && session.Project.Lines[1].StartTicks == 30_100_000, "UI shifts selected rows by 10ms");
        Undo_Click(this, new RoutedEventArgs()); Check(session.Project == before, "one UI Undo restores batch");
        selected = ids[1]; selectedIds.Clear(); selectedIds.Add(ids[1]); SelectGrid(); UpdateInspector();
        FollowPrevious.IsChecked = true; AlignPrevious_Click(this, new RoutedEventArgs());
        Check(session.Project.Lines[0].EndTicks == 30_000_000, "UI aligns predecessor end to selected start");
        Undo_Click(this, new RoutedEventArgs());
        FollowPrevious.IsChecked = false;
        SelectedText.Focus();
        var textSpace = KeyEvent(Key.Space, Keyboard.PreviewKeyDownEvent); Key_Down(this, textSpace);
        var textS = KeyEvent(Key.S, Keyboard.PreviewKeyDownEvent); Key_Down(this, textS);
        Check(!textSpace.Handled && !textS.Handled && held is null, "S and Space remain text input inside inspector");
        Timeline.Focus(); mediaReady = true; playing = true;
        var space = KeyEvent(Key.Space, Keyboard.PreviewKeyDownEvent); Key_Down(this, space);
        var originalHold = held;
        Key_Down(this, KeyEvent(Key.Space, Keyboard.PreviewKeyDownEvent));
        Check(space.Handled && originalHold.HasValue && held == originalHold && (string?)PonButton.Tag == "Held", "Space arms once and depresses PON face");
        UpdateLayout(); CaptureSmoke(this, "pon-held.png");
        SelectedText.Focus();
        Check(held is null && !spaceHeld && PonButton.Tag is null && session.Project == before, "focus change aborts held PON without a timing edit");
        Timeline.Focus(); Key_Down(this, KeyEvent(Key.Space, Keyboard.PreviewKeyDownEvent));
        var keyUp = KeyEvent(Key.Space, Keyboard.PreviewKeyUpEvent); Key_Up(this, keyUp);
        Check(keyUp.Handled && held is null && PonButton.Tag is null, "Space release clears recording and button face");
        var s = KeyEvent(Key.S, Keyboard.PreviewKeyDownEvent); Key_Down(this, s);
        Check(s.Handled && !playing, "S pauses transport");
        mediaReady = false; player.Close();
        UpdateLayout();
        var height = TimelineScroll.ActualHeight;
        RegionSplitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        RegionSplitter.RaiseEvent(new DragDeltaEventArgs(0, 35) { RoutedEvent = Thumb.DragDeltaEvent });
        RegionSplitter.RaiseEvent(new DragCompletedEventArgs(0, 35, false) { RoutedEvent = Thumb.DragCompletedEvent });
        UpdateLayout();
        Check(TimelineScroll.ActualHeight > height + 10 && Math.Abs(Timeline.ActualHeight - TimelineScroll.ViewportHeight) < 1, "splitter drag resizes surface with viewport");
        Width = 1000; Height = 720; UpdateLayout();
        Check(LyricsGrid.ActualHeight >= 89 && TimelineScroll.ActualHeight >= 149, "minimum window retains timeline and lyric table");
        Check(CurrentLyric.FontSize == NextLyric.FontSize && NextLyric.ActualHeight > 20, "NEXT uses same readable font size");
        CaptureSmoke(this, "workspace.png");
        var dialog = new LyricsWindow { Owner = this }; dialog.Show(); dialog.UpdateLayout();
        try
        {
            CaptureSmoke(dialog, "lyrics.png");
            var input = (TextBox)dialog.FindName("LyricsInput"); input.Text = "貼り付けの歌詞\n次の歌詞";
            Check(session.Project == before, "dialog draft does not modify current document");
            var path = Path.Combine(Path.GetTempPath(), "lyrics-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                await File.WriteAllTextAsync(path, "ドロップした歌詞\nNEXTの歌詞", new UTF8Encoding(true));
                await dialog.LoadFile(path); // Same bounded import path used by picker and file-drop handler.
                Check(input.Text == "ドロップした歌詞\nNEXTの歌詞" && session.Project == before, "shared picker/drop path previews UTF8 lyrics without replacing current timing");
                CaptureSmoke(dialog, "lyrics-drop.png");
            }
            finally { File.Delete(path); }
        }
        finally { dialog.Close(); }
        saved = session.Project; // synthetic document must not prompt during normal close.
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), System.Text.Json.JsonSerializer.Serialize(checks));
    }
    private static void CaptureSmoke(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(output);
    }
}

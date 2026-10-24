using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;

namespace Flamoris.StudioClient.App;

public sealed record LyricRow(Guid Id, int Number, string Text, string Start, string End);

public partial class MainWindow : Window
{
    private readonly TimingSession session = new();
    private readonly AudioWorker worker = new();
    private readonly MediaPlayer player = new();
    private readonly Dictionary<string, AudioJobResult> prepared = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private TimingProject saved;
    private string? projectPath, projectHash, playingPath, analysisPath;
    private AudioJobResult? analysis;
    private CancellationTokenSource? jobCancellation;
    private Task<AudioJobResult>? activeJob;
    private Guid? selected;
    private readonly HashSet<Guid> selectedIds = [];
    private bool selectingGrid, spaceHeld;
    private (Guid Id, long Start, long Revision)? held;
    private bool playing, mediaReady, suppressSource, closingAllowed, analysisStale, playAfterOpen;
    private int timerCount;
    private string? SourcePath => PlaybackSource.SelectedIndex == 1 ? session.Project.VocalPath : session.Project.MusicPath;
    private long Position => Math.Max(0, player.Position.Ticks);
    private double Duration => playingPath is not null && prepared.TryGetValue(playingPath, out var job) ? job.Features.Duration : 0;

    public MainWindow()
    {
        saved = session.Project;
        InitializeComponent();
        AnalysisWave.Fit = true; Timeline.Focusable = true;
        Adjustment.ItemsSource = Enumerable.Range(1, 20).Select(i => $"{i}ms").ToArray(); Adjustment.Text = "10ms";
        session.Changed += Refresh;
        Timeline.SelectLine += id => { selected = id; selectedIds.Clear(); selectedIds.Add(id); SelectGrid(); UpdateInspector(); };
        Timeline.Seek += ticks => { AbortHold(); player.Position = TimeSpan.FromTicks(Math.Clamp(ticks, 0, LyricTime.FromSeconds((decimal)Duration))); Tick(); };
        Timeline.CommitRange += (id, start, end, revision) => Sync(() => session.Execute(p => TimingEdits.Range(p, id, start, end, FollowPrevious.IsChecked == true), revision));
        Timeline.PreviewEdit = (id, start, end) => TimingEdits.Range(session.Project, id, start, end, FollowPrevious.IsChecked == true).Lines;
        player.MediaOpened += (_, _) => { mediaReady = true; if (playAfterOpen) { player.Play(); playing = true; playAfterOpen = false; } Status("準備できたよ。再生しながらPON!を長押ししてね。"); };
        player.MediaFailed += (_, _) => { playing = false; mediaReady = false; AbortHold(); Status("試聴用音源を再生できませんでした。音声デバイスを確認してください。"); };
        player.MediaEnded += (_, _) => { ReleaseHold(LyricTime.FromSeconds((decimal)Duration)); player.Pause(); playing = false; Tick(); };
        timer.Tick += (_, _) => Tick(); timer.Start();
        PreviewKeyDown += Key_Down; PreviewKeyUp += Key_Up;
        Deactivated += (_, _) => { AbortHold(); Timeline.CancelGesture(); };
        PreviewLostKeyboardFocus += (_, _) => { if (spaceHeld) AbortHold(); };
        Closing += ClosingAsync;
        Loaded += async (_, _) =>
        {
            Timeline.SetViewport(0, TimelineScroll.ViewportWidth);
            if (Environment.GetCommandLineArgs().Contains("--smoke"))
            {
                await Task.Delay(250); UpdateLayout();
                await RunUiSmoke();
                Close();
            }
        };
        Refresh();
    }
    private void Status(string text) => StatusText.Text = text;
    private void Sync(Action action) { try { action(); } catch (Exception e) { Status(e.Message); } }
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Status("処理をキャンセルしました。"); }
        catch (Exception e) { Status(e.Message); MessageBox.Show(this, e.Message, "StudioClient", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void Tick()
    {
        if (playingPath is not null)
        {
            var position = Position;
            ClockText.Text = LyricTime.Display(position);
            Timeline.Playhead = position; Timeline.InvalidateVisual();
            var x = Timeline.TimeToPixels(position);
            if (playing && (x > TimelineScroll.HorizontalOffset + TimelineScroll.ViewportWidth - 40 || x < TimelineScroll.HorizontalOffset))
            {
                // Follow is presentation only; never advance the media clock here.
                TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, x - TimelineScroll.ViewportWidth * .3));
            }
            if (PreviewLyrics.IsChecked == true && held is null)
            {
                var current = session.Project.Lines.FirstOrDefault(l => l.IsTimed && l.StartTicks <= position && position < l.EndTicks);
                CurrentLyric.Text = current?.Text ?? "";
                var index = current is null ? -1 : session.Project.Lines.IndexOf(current);
                NextLyric.Text = index >= 0 && index + 1 < session.Project.Lines.Length ? session.Project.Lines[index + 1].Text : "";
            }
        }
        if (++timerCount % 30 == 0 && analysis is not null && analysisPath is not null && !MetadataMatches(analysisPath, analysis.Features.Source))
            MarkStale();
    }
    private static bool MetadataMatches(string path, AudioIdentity source)
    {
        var info = new System.IO.FileInfo(path);
        return info.Exists && info.Length == source.SizeBytes && info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) == source.MtimeUtcTicks;
    }
    private void MarkStale()
    {
        if (analysisStale) return;
        analysisStale = true; AnalysisLabel.Text = "音源が変更されています。再解析してください。\n" + AnalysisLabel.Text;
        ChordGrid.ItemsSource = null; AnalysisWave.SetData([], null, analysis?.Features.Duration ?? 10, 0); RefreshTimeline();
    }
    private void Refresh()
    {
        if (playingPath is not null && playingPath != SourcePath)
        { AbortHold(); player.Close(); playing = mediaReady = false; playingPath = null; }
        Title = $"FLAMORIS StudioClient — {System.IO.Path.GetFileName(projectPath) ?? "新規PONPON"}{(session.Project == saved ? "" : " *")}";
        MusicLabel.Text = session.Project.MusicPath is null ? "音源未選択" : System.IO.Path.GetFileName(session.Project.MusicPath);
        selectingGrid = true;
        LyricsGrid.ItemsSource = session.Project.Lines.Select((l, i) => new LyricRow(l.Id, i + 1, l.Text, LyricTime.Display(l.StartTicks), LyricTime.Display(l.EndTicks))).ToArray();
        if (!session.Project.Lines.Any(l => l.Id == selected)) selected = session.Project.Lines.FirstOrDefault()?.Id;
        selectedIds.RemoveWhere(id => !session.Project.Lines.Any(l => l.Id == id));
        if (selectedIds.Count == 0 && selected is { } currentId) selectedIds.Add(currentId);
        selectingGrid = false;
        SelectGrid(); UpdateInspector(); RefreshTimeline();
    }
    private void RefreshTimeline()
    {
        AudioFeatures? features = null;
        if (SourcePath is { } path)
        {
            if (analysisPath == path && analysis is not null && !analysisStale) features = analysis.Features;
            else if (prepared.TryGetValue(path, out var ready)) features = ready.Features;
        }
        var last = session.Project.Lines.Where(l => l.IsTimed).Select(l => (double)LyricTime.Seconds(l.EndTicks!.Value)).DefaultIfEmpty(10).Max();
        Timeline.SetData(session.Project.Lines, features, Math.Max(last, features?.Duration ?? 0), session.Revision);
        Timeline.Selected = selected; Timeline.SelectedIds = selectedIds.ToHashSet(); Timeline.InvalidateVisual();
    }
    private void SelectGrid()
    {
        if (LyricsGrid.ItemsSource is not LyricRow[] rows) return;
        selectingGrid = true;
        try
        {
            LyricsGrid.SelectedItems.Clear();
            foreach (var row in rows.Where(r => selectedIds.Contains(r.Id))) LyricsGrid.SelectedItems.Add(row);
            if (selected is { } id && rows.FirstOrDefault(r => r.Id == id) is { } active) LyricsGrid.CurrentItem = active;
        }
        finally { selectingGrid = false; }
        Timeline.Selected = selected; Timeline.SelectedIds = selectedIds.ToHashSet(); Timeline.InvalidateVisual();
    }
    private void UpdateInspector()
    {
        var line = session.Project.Lines.FirstOrDefault(l => l.Id == selected);
        var i = line is null ? -1 : session.Project.Lines.IndexOf(line);
        StartSeconds.Text = line?.StartTicks is { } start ? LyricTime.Seconds(start).ToString(CultureInfo.InvariantCulture) : "";
        EndSeconds.Text = line?.EndTicks is { } end ? LyricTime.Seconds(end).ToString(CultureInfo.InvariantCulture) : "";
        SelectedText.Text = line?.Text ?? "";
        CurrentLyric.Text = line?.Text ?? "歌詞を読み込んでね";
        NextLyric.Text = i >= 0 && i + 1 < session.Project.Lines.Length ? session.Project.Lines[i + 1].Text : "";
        LineCounter.Text = $"{(i >= 0 ? i + 1 : 0)} / {session.Project.Lines.Length} — {session.Project.Lines.Count(l => l.IsTimed)}行 記録済み";
    }
    private async Task<AudioJobResult> RunJob(string path, bool analyze)
    {
        if (activeJob is not null) throw new InvalidOperationException("処理中です。完了するかキャンセルしてから操作してください。");
        jobCancellation = new CancellationTokenSource(); CancelButton.IsEnabled = true; PlaybackSource.IsEnabled = false; Progress.Value = 0;
        var progress = new Progress<(int Percent, string Stage)>(v => { Progress.Value = v.Percent; Status(v.Stage); });
        try { activeJob = worker.RunAsync(path, analyze, progress, jobCancellation.Token); return await activeJob; }
        finally { activeJob = null; jobCancellation.Dispose(); jobCancellation = null; CancelButton.IsEnabled = false; PlaybackSource.IsEnabled = true; }
    }
    private async Task Prepare(string path)
    {
        var document = session.Project.Id;
        if (!prepared.TryGetValue(path, out var ready) || !await ready.Features.IsCurrentAsync(path))
        {
            ready = await RunJob(path, false);
            if (prepared.Remove(path, out var old)) DeleteJob(old);
            prepared[path] = ready;
        }
        if (session.Project.Id != document || SourcePath != path) return;
        AbortHold(); player.Close(); playing = false; mediaReady = false; playingPath = path;
        player.Open(new Uri(ready.PreviewPath!)); RefreshTimeline();
        foreach (var extra in prepared.Keys.Where(k => k != session.Project.MusicPath && k != session.Project.VocalPath).ToArray())
        { var old = prepared[extra]; prepared.Remove(extra); DeleteJob(old); }
    }
    private static void DeleteJob(AudioJobResult result)
    { try { System.IO.Directory.Delete(result.Directory, true); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { } }
    private void ClearPlayback()
    {
        AbortHold(); player.Close(); playing = mediaReady = playAfterOpen = false; playingPath = null;
        foreach (var item in prepared.Values) DeleteJob(item); prepared.Clear();
    }
    private static string? PickAudio(Window owner)
    {
        var dialog = new OpenFileDialog { Filter = "音源|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.aac;*.aiff;*.mp4|すべてのファイル|*.*", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    private async void Music_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (activeJob is not null) throw new InvalidOperationException("処理が完了するまで待ってください。");
        var path = PickAudio(this); if (path is null) return;
        ClearPlayback(); session.Execute(p => p with { MusicPath = path, VocalPath = null });
        suppressSource = true; PlaybackSource.SelectedIndex = 0; suppressSource = false;
        await Prepare(path);
    });
    private async void Vocal_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (activeJob is not null) throw new InvalidOperationException("処理が完了するまで待ってください。");
        var path = PickAudio(this); if (path is null) return;
        AbortHold(); player.Close(); playing = mediaReady = false;
        session.Execute(p => p with { VocalPath = path });
        suppressSource = true; PlaybackSource.SelectedIndex = 1; suppressSource = false;
        await Prepare(path);
    });
    private async void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || suppressSource) return;
        await Guard(async () => { AbortHold(); player.Close(); playing = mediaReady = false; playingPath = null;
            if (SourcePath is { } path) await Prepare(path); else { Status("Vocal Stemを選んでください。"); RefreshTimeline(); } });
    }
    private async void Play_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (!mediaReady && SourcePath is { } path)
        { playAfterOpen = true; try { await Prepare(path); } catch { playAfterOpen = false; throw; } }
        else TogglePlay();
    });
    private void TogglePlay()
    {
        if (!mediaReady) { Status("先に音源を開いてください。"); return; }
        if (playing) { AbortHold(); player.Pause(); playing = false; }
        else { player.Play(); playing = true; }
    }
    private void PlaySelected_Click(object sender, RoutedEventArgs e) => PlaySelected();
    private void PlaySelected()
    {
        var line = session.Project.Lines.FirstOrDefault(l => l.Id == selected);
        if (!mediaReady || line?.StartTicks is not { } ticks) { Status("時間のある歌詞と音源を選んでね。"); return; }
        AbortHold(); player.Position = TimeSpan.FromTicks(ticks); player.Play(); playing = true;
    }
    private void Start_Click(object sender, RoutedEventArgs e) { AbortHold(); player.Position = TimeSpan.Zero; Tick(); }
    private void Preview_Unchecked(object sender, RoutedEventArgs e) { if (IsLoaded) UpdateInspector(); }
    private void BeginHold()
    {
        if (held.HasValue || !mediaReady || selected is null) return;
        PreviewLyrics.IsChecked = false; UpdateInspector();
        if (!playing) { player.Play(); playing = true; }
        held = (selected.Value, Position, session.Revision); PonButton.Tag = "Held"; Status("記録中 — 離すと次の行へ");
    }
    private void ReleaseHold(long? end = null)
    {
        if (held is not { } hold) return;
        held = null; PonButton.Tag = null;
        var stop = end ?? Position;
        if (stop <= hold.Start) { Status("時間が進みませんでした。再生してから録り直してください。"); return; }
        Sync(() =>
        {
            session.Execute(p => TimingEdits.Range(p, hold.Id, hold.Start, stop, FollowPrevious.IsChecked == true), hold.Revision);
            var lines = session.Project.Lines; var index = lines.IndexOf(lines.First(l => l.Id == hold.Id));
            selected = index + 1 < lines.Length ? lines[index + 1].Id : hold.Id;
            selectedIds.Clear(); selectedIds.Add(selected.Value);
            SelectGrid(); UpdateInspector(); LyricsGrid.ScrollIntoView(LyricsGrid.SelectedItem);
            Status("記録しました。箱を動かして微調整できます。");
        });
    }
    private void AbortHold()
    {
        held = null; spaceHeld = false;
        if (PonButton is not null) { PonButton.Tag = null; PonButton.ReleaseMouseCapture(); }
    }
    private void Pon_Down(object sender, MouseButtonEventArgs e) { if (!spaceHeld) { BeginHold(); if (held.HasValue) PonButton.CaptureMouse(); } e.Handled = true; }
    private void Pon_Up(object sender, MouseButtonEventArgs e) { if (!spaceHeld) { ReleaseHold(); PonButton.ReleaseMouseCapture(); } e.Handled = true; }
    private void Pon_LostCapture(object sender, MouseEventArgs e) => AbortHold();
    private async void Key_Down(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.IsRepeat) return;
            if (e.Key == Key.S) { e.Handled = true; await Guard(async () => { await Save(false); }); }
            else if (e.Key == Key.O) { e.Handled = true; Open_Click(sender, e); }
            else if (e.Key == Key.N) { e.Handled = true; New_Click(sender, e); }
            else if (Keyboard.FocusedElement is not TextBoxBase && e.Key is Key.Z or Key.Y)
            { e.Handled = true; AbortHold(); if (e.Key == Key.Z) session.Undo(); else session.Redo(); }
            return;
        }
        if (WorkspaceTabs.SelectedIndex != 0 || Keyboard.Modifiers != ModifierKeys.None || IsTextEntry(Keyboard.FocusedElement)) return;
        if (e.Key == Key.Escape) { AbortHold(); Timeline.CancelGesture(); e.Handled = true; }
        if (e.Key == Key.S) { e.Handled = true; if (!e.IsRepeat) TogglePlay(); }
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (!e.IsRepeat && !spaceHeld && held is null) { spaceHeld = true; BeginHold(); }
        }
    }
    private static bool IsTextEntry(IInputElement? element)
    {
        for (var current = element as DependencyObject; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is TextBoxBase or PasswordBox or ComboBox or MenuItem) return true;
        return false;
    }
    private void Key_Up(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !spaceHeld) return;
        spaceHeld = false; ReleaseHold(); e.Handled = true;
    }
    private void Line_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (selectingGrid) return;
        AbortHold();
        selectedIds.Clear(); foreach (var row in LyricsGrid.SelectedItems.OfType<LyricRow>()) selectedIds.Add(row.Id);
        selected = e.AddedItems.OfType<LyricRow>().LastOrDefault()?.Id ?? LyricsGrid.SelectedItems.OfType<LyricRow>().FirstOrDefault()?.Id;
        UpdateInspector(); Timeline.Selected = selected; Timeline.SelectedIds = selectedIds.ToHashSet(); Timeline.InvalidateVisual();
    }
    private void Line_DoubleClick(object sender, MouseButtonEventArgs e) => PlaySelected();
    private void ApplyLine_Click(object sender, RoutedEventArgs e) => Sync(() =>
    {
        if (selected is not { } id) return;
        AbortHold();
        var start = LyricTime.FromSeconds(decimal.Parse(StartSeconds.Text, CultureInfo.InvariantCulture));
        var end = LyricTime.FromSeconds(decimal.Parse(EndSeconds.Text, CultureInfo.InvariantCulture));
        var text = SelectedText.Text;
        session.Execute(p => TimingEdits.Range(p, id, start, end, FollowPrevious.IsChecked == true, text));
    });
    private void Undo_Click(object sender, RoutedEventArgs e) { AbortHold(); session.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { AbortHold(); session.Redo(); }
    private void ClearLine_Click(object sender, RoutedEventArgs e) => Sync(() => { AbortHold(); session.Execute(p => p with { Lines = p.Lines.Select(l => l.Id == selected ? l with { StartTicks = null, EndTicks = null } : l).ToImmutableArray() }); });
    private void Restore_Click(object sender, RoutedEventArgs e) => Sync(() => { AbortHold(); session.Execute(p => p with { Lines = p.OriginalLines }); });
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Timeline.Zoom(1.5);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Timeline.Zoom(1 / 1.5);
    private void Timeline_Scroll(object sender, ScrollChangedEventArgs e)
    { Timeline.SetViewport(e.HorizontalOffset, e.ViewportWidth); SizeTimeline(); }
    private async Task<bool> CanDiscard()
    {
        if (session.Project == saved) return true;
        var choice = MessageBox.Show(this, "変更を保存しますか？", "PONPON", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && await Save(false);
    }
    private async void New_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (activeJob is not null) throw new InvalidOperationException("処理をキャンセルしてから新規作成してください。");
        if (!await CanDiscard()) return; ClearPlayback(); session.Open(TimingProject.Empty());
        projectPath = projectHash = null; saved = session.Project; Refresh();
    });
    private async void Open_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (activeJob is not null) throw new InvalidOperationException("処理をキャンセルしてから開いてください。");
        if (!await CanDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "PONPONプロジェクト|*.fponproj", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var read = await LocalFiles.ReadAsync(dialog.FileName); var project = TimingFormats.LoadProject(read.Bytes);
        ClearPlayback(); session.Open(project); projectPath = dialog.FileName; projectHash = read.Hash; saved = session.Project;
        Refresh();
        suppressSource = true; PlaybackSource.SelectedIndex = 0; suppressSource = false;
        if (project.MusicPath is not null && System.IO.File.Exists(project.MusicPath)) await Prepare(project.MusicPath);
        else Status("読み込みました。音源がない場合も歌詞の編集・出力はできます。");
    });
    private async Task<bool> Save(bool saveAs)
    {
        var path = projectPath; var expected = projectHash;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog { Filter = "PONPONプロジェクト|*.fponproj", DefaultExt = ".fponproj", FileName = System.IO.Path.GetFileName(path) ?? "lyrics.fponproj", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName; expected = System.IO.File.Exists(path) ? await LocalFiles.FingerprintAsync(path) : null;
        }
        var snapshot = session.Project;
        var hash = await LocalFiles.PublishAsync(path, TimingFormats.SaveProject(snapshot), expected);
        projectPath = path; projectHash = hash; saved = snapshot; Refresh(); Status("保存しました。"); return true;
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await Guard(async () => { await Save(false); });
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await Guard(async () => { await Save(true); });
    private void Import_Click(object sender, RoutedEventArgs e) => Sync(() =>
    {
        AbortHold(); Timeline.CancelGesture();
        var dialog = new LyricsWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
        if (session.Project.Lines.Length > 0 && MessageBox.Show(this,
            "現在の歌詞・タイミングを置き換えますか？\n未保存の変更も置き換えます。元に戻すで復元できます。",
            "歌詞の読み込み", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        session.Execute(p => p with { Lines = result.Lines, OriginalLines = result.Lines });
        selected = result.Lines.FirstOrDefault()?.Id; selectedIds.Clear();
        if (selected is { } id) selectedIds.Add(id); SelectGrid(); UpdateInspector();
        Status("歌詞を読み込みました。元ファイルは変更していません。");
    });
    private HashSet<Guid> EditTargets() => EditScope.SelectedIndex == 1
        ? session.Project.Lines.Select(l => l.Id).ToHashSet() : selectedIds.ToHashSet();
    private void ShiftEarlier_Click(object sender, RoutedEventArgs e) => Shift(-1);
    private void ShiftLater_Click(object sender, RoutedEventArgs e) => Shift(1);
    private void Shift(int direction) => Sync(() =>
    {
        AbortHold(); Timeline.CancelGesture();
        var delta = checked(TimingEdits.ParseAdjustment(Adjustment.Text) * direction); var targets = EditTargets();
        session.Execute(p => TimingEdits.Shift(p, targets, delta, FollowPrevious.IsChecked == true));
        Status($"{(direction < 0 ? "前" : "後ろ")}へ {Math.Abs(LyricTime.Seconds(delta)):0.#######}秒 調整しました。");
    });
    private void AlignPrevious_Click(object sender, RoutedEventArgs e) => Sync(() =>
    {
        AbortHold(); Timeline.CancelGesture(); var targets = EditTargets();
        session.Execute(p => TimingEdits.AlignPrevious(p, targets)); Status("前行の終了を対象行の開始にそろえました。");
    });
    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => SizeTimeline();
    private void SizeTimeline()
    {
        if (Timeline is not null && TimelineScroll.ViewportHeight > 0 && double.IsFinite(TimelineScroll.ViewportHeight) && Math.Abs(Timeline.Height - TimelineScroll.ViewportHeight) > .5)
            Timeline.Height = Math.Max(125, TimelineScroll.ViewportHeight);
    }
    private async Task Export(byte[] content, string filter, string name)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = name, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        var hash = System.IO.File.Exists(dialog.FileName) ? await LocalFiles.FingerprintAsync(dialog.FileName) : null;
        await LocalFiles.PublishAsync(dialog.FileName, content, hash); Status("書き出しました。");
    }
    private async void ExportSrt_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (!session.Project.Lines.Any(l => l.IsTimed)) throw new InvalidOperationException("時間を記録した歌詞がありません。");
        var pending = session.Project.Lines.Count(l => !l.IsTimed);
        if (pending > 0 && MessageBox.Show(this, $"未記録の{pending}行は出力しません。続けますか？", "SRT出力", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await Export(Encoding.UTF8.GetBytes(TimingFormats.ExportSrt(session.Project)), "SRT|*.srt", "lyrics.srt");
    });
    private async void ExportTiming_Click(object sender, RoutedEventArgs e) => await Guard(() => Export(TimingFormats.ExportTiming(session.Project), "歌詞タイミングJSON|*.json", "lyrics.timing.json"));
    private async void ExportPonpon_Click(object sender, RoutedEventArgs e) => await Guard(() => Export(TimingFormats.ExportPonpon(session.Project), "PONPON JSON|*.json", "lyrics_ponpon_export.json"));
    private async Task Analyze(string path)
    {
        var result = await RunJob(path, true);
        if (analysis is not null) DeleteJob(analysis);
        analysis = result; analysisPath = path; analysisStale = false;
        var f = result.Features;
        AnalysisLabel.Text = $"{System.IO.Path.GetFileName(path)}  /  {f.Duration:F1}秒\n推定BPM：{(f.Bpm?.ToString("F1", CultureInfo.InvariantCulture) ?? "不明")}  /  推定キー：{(f.Tonality is null ? "不明" : f.Tonality.Key + " " + f.Tonality.Scale)}";
        ChordGrid.ItemsSource = f.Chords; AnalysisWave.SetData([], f, f.Duration, 0); RefreshTimeline();
        Status("解析が完了しました。推定結果と波形を確認できます。");
    }
    private async void AnalyzeFile_Click(object sender, RoutedEventArgs e) => await Guard(async () => { if (PickAudio(this) is { } path) await Analyze(path); });
    private async void AnalyzeMusic_Click(object sender, RoutedEventArgs e) => await Guard(() => Analyze(session.Project.MusicPath ?? throw new InvalidOperationException("Musicを選んでください。")));
    private async void AnalyzeVocal_Click(object sender, RoutedEventArgs e) => await Guard(() => Analyze(session.Project.VocalPath ?? throw new InvalidOperationException("Vocal Stemを選んでください。")));
    private async void ExportAnalysis_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (analysis is null || analysisPath is null) throw new InvalidOperationException("先に解析してください。");
        if (!await analysis.Features.IsCurrentAsync(analysisPath)) { MarkStale(); throw new InvalidOperationException("音源が変わっています。再解析してください。"); }
        await Export(JsonSerializer.SerializeToUtf8Bytes(analysis.Features, TimingFormats.JsonOptions), "音声解析JSON|*.json", "audio.features.json");
    });
    private void Cancel_Click(object sender, RoutedEventArgs e) => jobCancellation?.Cancel();
    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (closingAllowed) return;
        e.Cancel = true;
        await Guard(async () =>
        {
            if (!await CanDiscard()) return;
            jobCancellation?.Cancel();
            if (activeJob is { } running) try { await running; } catch (Exception) { }
            timer.Stop(); ClearPlayback(); worker.Dispose(); closingAllowed = true;
            // Closing may complete synchronously (no dirty document/job). Queue
            // the second Close after the current cancellable Closing event returns.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        });
    }
}

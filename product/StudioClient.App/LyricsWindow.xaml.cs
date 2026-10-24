using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Flamoris.StudioClient.Infrastructure;
using Flamoris.StudioClient.Core;
using Microsoft.Win32;

namespace Flamoris.StudioClient.App;

public partial class LyricsWindow : Window
{
    private ImportedLyrics? imported;
    private bool settingText, loading;
    public ImportedLyrics? Result { get; private set; }
    public LyricsWindow()
    {
        InitializeComponent();
    }
    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (EmptyHint is null) return;
        EmptyHint.Visibility = LyricsInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (settingText) return;
        imported = null;
        ApplyButton.Content = "この歌詞で始める（1行ずつ）";
        ImportStatus.Text = "貼り付けた歌詞を新しい行として取り込みます。タイミングは確定後に記録します。";
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "歌詞ファイル|*.txt;*.json;*.srt", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) await LoadFile(dialog.FileName);
    }
    internal async Task LoadFile(string path)
    {
        if (loading) return;
        loading = true; ApplyButton.IsEnabled = false; LyricsInput.IsReadOnly = true;
        try
        {
            var next = await LyricsImport.LoadAsync(path);
            if (!IsVisible) return;
            settingText = true;
            try { LyricsInput.Text = next.Text; } finally { settingText = false; }
            imported = next;
            ApplyButton.Content = next.PreserveTiming ? "タイミングを保持して読み込む" : "この歌詞で始める（1行ずつ）";
            ImportStatus.Text = next.PreserveTiming ? "タイミングを保持します。本文を編集すると、新しい歌詞として時間を記録し直します。" : "読み込みました。「この歌詞で始める」で確定してください。";
        }
        catch (Exception e) { ImportStatus.Text = e.Message; }
        finally { loading = false; ApplyButton.IsEnabled = true; LyricsInput.IsReadOnly = false; }
    }
    private static string DroppedPath(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            throw new InvalidDataException("単一のTXT / JSON / SRTファイルをドロップしてください。歌詞の文字は貼り付けで入力できます。");
        return LyricsImport.SingleFile(paths);
    }
    private void DragOver_Lyrics(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        try
        {
            if (loading) return;
            _ = DroppedPath(e); e.Effects = DragDropEffects.Copy; DropBorder.BorderBrush = Brushes.Teal;
            ImportStatus.Text = "離すと歌詞ファイルを読み込みます。確定までは編集内容を変更しません。";
        }
        catch (Exception error) { DropBorder.BorderBrush = Brushes.IndianRed; ImportStatus.Text = error.Message; }
    }
    private void DragLeave_Lyrics(object sender, DragEventArgs e) => DropBorder.BorderBrush = Brushes.DarkSeaGreen;
    private async void Drop_Lyrics(object sender, DragEventArgs e)
    {
        e.Handled = true; DropBorder.BorderBrush = Brushes.DarkSeaGreen;
        try { await LoadFile(DroppedPath(e)); }
        catch (Exception error) { ImportStatus.Text = error.Message; }
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = imported ?? new ImportedLyrics(LyricsInput.Text, LyricsImport.FromText(LyricsInput.Text), false);
            if (LyricText.ForTiming(result.Lines).Length == 0) throw new InvalidDataException("タグ・括弧だけの行を除いた歌詞を1行以上入力してください。");
            Result = result; DialogResult = true;
        }
        catch (Exception error) { ImportStatus.Text = error.Message; }
    }
}

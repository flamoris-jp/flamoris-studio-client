using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.Win32;

namespace Flamoris.StudioClient.App;

public partial class DistributionLyricsWindow : Window
{
    private string? checkedText;
    private bool settingText, saving;

    public DistributionLyricsWindow(string source)
    {
        InitializeComponent();
        ApplyFormatting(source);
        Closing += (_, e) => { if (saving) e.Cancel = true; };
    }
    private void ApplyFormatting(string source)
    {
        var result = DistributionLyrics.Format(source);
        settingText = true;
        try { PreviewText.Text = result.Text; } finally { settingText = false; }
        ChangesText.Text = $"{result.ChangeCount}件の整形（詳細は最大{DistributionLyrics.MaxDetails}件）\n" + string.Join('\n', result.Changes);
        ShowReview(result);
    }
    private void Preview_Changed(object sender, TextChangedEventArgs e)
    {
        if (settingText || ReviewStatus is null) return;
        checkedText = null; CopyButton.IsEnabled = SaveTextButton.IsEnabled = false;
        ReviewStatus.Text = "編集後は「チェック」で確認してください。";
    }
    private void ShowReview(LyricsReview result)
    {
        checkedText = result.Text;
        WarningsText.Text = $"要確認 {result.WarningCount}件（詳細は最大{DistributionLyrics.MaxDetails}件）\n" + string.Join('\n', result.Warnings)
            + "\n\n略語・固有名詞・スペル・歌唱内容は自動判定しません。要確認は音源と照らして判断してください。審査通過を保証するものではありません。";
        var available = !string.IsNullOrWhiteSpace(result.Text) && !saving;
        CopyButton.IsEnabled = SaveTextButton.IsEnabled = available;
        ReviewStatus.Text = available ? $"要確認 {result.WarningCount}件。確認後にコピー／保存できます。" : "出力する歌詞がありません。";
    }
    private void Check_Click(object sender, RoutedEventArgs e) => Run(() => ShowReview(DistributionLyrics.Inspect(PreviewText.Text)));
    private void Format_Click(object sender, RoutedEventArgs e) => Run(() => ApplyFormatting(PreviewText.Text));
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { checkedText = null; CopyButton.IsEnabled = SaveTextButton.IsEnabled = false; ReviewStatus.Text = error.Message; }
    }
    private string CheckedOutput()
    {
        if (checkedText != PreviewText.Text || string.IsNullOrWhiteSpace(checkedText))
            throw new InvalidOperationException("歌詞をチェックしてから書き出してください。");
        DistributionLyrics.Validate(checkedText);
        return checkedText;
    }
    private void Copy_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        Clipboard.SetText(CheckedOutput()); ReviewStatus.Text = "配信用歌詞をコピーしました。";
    });
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        try
        {
            var content = Encoding.UTF8.GetBytes(CheckedOutput());
            var picker = new SaveFileDialog { Filter = "歌詞テキスト|*.txt", DefaultExt = ".txt", FileName = "lyrics_distribution.txt", OverwritePrompt = true };
            if (picker.ShowDialog(this) != true) return;
            saving = true; ReviewContent.IsEnabled = false; PreviewText.IsReadOnly = true; CopyButton.IsEnabled = SaveTextButton.IsEnabled = false;
            var expected = File.Exists(picker.FileName) ? await LocalFiles.FingerprintAsync(picker.FileName) : null;
            await LocalFiles.PublishAsync(picker.FileName, content, expected);
            ReviewStatus.Text = "配信用歌詞をTXTで保存しました。";
        }
        catch (Exception error) { ReviewStatus.Text = error.Message; }
        finally
        {
            saving = false; ReviewContent.IsEnabled = true; PreviewText.IsReadOnly = false;
            CopyButton.IsEnabled = SaveTextButton.IsEnabled = checkedText == PreviewText.Text && !string.IsNullOrWhiteSpace(checkedText);
        }
    }
}

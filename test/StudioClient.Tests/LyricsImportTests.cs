using System.Text;
using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class LyricsImportTests
{
    [TestMethod]
    public void TextSupportsBomJapaneseAndAllNewlinesWithoutInventingTimes()
    {
        foreach (var newline in new[] { "\r\n", "\n", "\r" })
        foreach (var bom in new[] { "", "\uFEFF" })
        {
            var result = LyricsImport.Decode(".TXT", Encoding.UTF8.GetBytes(bom + $"愛乃🐱{newline}{newline} 美祥 {newline}"));
            Assert.IsFalse(result.PreserveTiming); Assert.AreEqual(2, result.Lines.Length);
            Assert.AreEqual("愛乃🐱", result.Lines[0].Text); Assert.AreEqual("美祥", result.Lines[1].Text);
            Assert.IsTrue(result.Lines.All(l => !l.IsTimed));
        }
    }

    [TestMethod]
    public void JsonAndSrtUseExistingContractsAndRetainTiming()
    {
        var line = new LyricLine(Guid.NewGuid(), "宇宙", 10_000_000, 20_000_000);
        var project = TimingProject.Empty() with { Lines = [line] };
        var json = LyricsImport.Decode(".json", TimingFormats.ExportTiming(project));
        Assert.IsTrue(json.PreserveTiming); Assert.AreEqual(line, json.Lines[0]);
        var srt = LyricsImport.Decode(".srt", Encoding.UTF8.GetBytes(TimingFormats.ExportSrt(project)));
        Assert.IsTrue(srt.PreserveTiming); Assert.AreEqual(line.StartTicks, srt.Lines[0].StartTicks);
    }

    [TestMethod]
    public void RejectMalformedEncodingSizeTextAndUnknownFormats()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.Decode(".txt", [0x82, 0xA0]));
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.Decode(".txt", new byte[TimingFormats.MaxBytes + 1]));
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.FromText(new string('a', 4097)));
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.FromText(string.Join('\n', Enumerable.Repeat("a", 20001))));
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.FromText(new string('a', LyricsImport.MaxCharacters + 1)));
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.Decode(".exe", Encoding.UTF8.GetBytes("text")));
        Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.Decode(".json", Encoding.UTF8.GetBytes("{\"format\":\"other\",\"lines\":[]}")));
    }

    [TestMethod]
    public async Task FileSelectionRejectsDirectoriesUrlsMultipleAndLinks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lyrics-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "lyrics.txt"); await File.WriteAllTextAsync(file, "ぽん");
            Assert.AreEqual("ぽん", (await LyricsImport.LoadAsync(file)).Lines[0].Text);
            Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.SingleFile([file, file]));
            Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.SingleFile([]));
            Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.SingleFile(["https://example.invalid/lyrics.txt"]));
            Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.SingleFile(["lyrics.txt"]));
            var directory = Path.Combine(folder, "directory.txt"); Directory.CreateDirectory(directory);
            Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.SingleFile([directory]));
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(folder, "link.txt"); File.CreateSymbolicLink(link, file);
                Assert.ThrowsExactly<InvalidDataException>(() => LyricsImport.SingleFile([link]));
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}

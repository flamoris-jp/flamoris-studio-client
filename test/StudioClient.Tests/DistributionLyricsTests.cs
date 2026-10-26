using System.Text;
using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class DistributionLyricsTests
{
    [TestMethod]
    public void KnownSunoSectionsBecomeSingleStanzaBreaks()
    {
        var input = "\uFEFF [Verse1]\r\n first line. \r\n\r\n[Pre-Chorus 1]\r\n[Chorus]\r\n\r\nnext line,\r\n［サビ2］\r\n最後。\r\n[Outro]\r\n\r\n";
        var result = DistributionLyrics.Format(input);
        Assert.AreEqual("First line\n\nNext line\n\n最後", result.Text);
        Assert.IsTrue(result.ChangeCount > 0);
        Assert.AreEqual(0, result.WarningCount);
        Assert.AreEqual(result.Text, DistributionLyrics.Format(result.Text).Text);
    }

    [TestMethod]
    public void PreserveSungParenthesesContractionsInternalPunctuationAndQuestions()
    {
        var result = DistributionLyrics.Format("(ooh, don't go.)\nI'm here, I'm alive!\nCreator, tell me, who am I?\n私を見てる、その眼差しが\n‘rockin’\n\"hello.\"\n（そうだね！）");
        Assert.AreEqual("(Ooh, don't go)\nI'm here, I'm alive!\nCreator, tell me, who am I?\n私を見てる、その眼差しが\n‘Rockin’\n\"Hello\"\n（そうだね！）", result.Text);
        Assert.IsTrue(result.WarningCount > 0); // Parenthesized vocals need human confirmation.
    }

    [TestMethod]
    public void PreserveAcronymsNamesSpellingAndJapaneseInlineEnglish()
    {
        var result = DistributionLyrics.Format("DOUNUT\nNASA and iPhone\n日本語の lower words\n[whisper]\n[tag] actual lyrics\nhttps://example.invalid\n(Repeat x3)");
        Assert.AreEqual("DOUNUT\nNASA and iPhone\n日本語の lower words\n[Whisper]\n[Tag] actual lyrics\nHttps://example.invalid\n(Repeat x3)", result.Text);
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("全大文字")));
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("角括弧")));
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("URL")));
    }

    [TestMethod]
    public void InspectionDoesNotSilentlyReformatEditsAndUsesOutputLineNumbers()
    {
        const string input = "Hello\n\nsmall,\nNASA\n[Chorus]";
        var result = DistributionLyrics.Inspect(input);
        Assert.AreEqual(input, result.Text);
        Assert.AreEqual(0, result.ChangeCount);
        Assert.IsTrue(result.Warnings.Any(w => w.StartsWith("3行目:") && w.Contains("小文字")));
        Assert.IsTrue(result.Warnings.Any(w => w.StartsWith("3行目:") && w.Contains("行末")));
        Assert.IsTrue(result.Warnings.Any(w => w.StartsWith("5行目:")));
    }

    [TestMethod]
    public void ProjectUsesCurrentEditsAndRestoresOnlySkippedAnnotationRows()
    {
        var session = new TimingSession(); session.SetLyrics("[Verse1]\noriginal\n(ooh)\n[Chorus]\nsecond\nlast");
        var first = session.Project.Lines[0];
        session.SetRange(first.Id, 10_000_000, 20_000_000);
        session.Execute(p => p with { Lines = p.Lines.SetItem(0, p.Lines[0] with { Text = "edited" }).RemoveAt(2) });
        var snapshot = session.Project; var revision = session.Revision; var canUndo = session.CanUndo;
        var result = DistributionLyrics.Format(DistributionLyrics.FromProject(snapshot));
        Assert.AreEqual("Edited\n(Ooh)\n\nSecond", result.Text);
        Assert.AreSame(snapshot, session.Project); Assert.AreEqual(revision, session.Revision);
        Assert.AreEqual(canUndo, session.CanUndo); Assert.IsTrue(session.Project.Lines[0].IsTimed);
    }

    [TestMethod]
    public void SrtImportExportsRepeatedSungLinesWithoutTimingMetadata()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:02,000\nhello.\n\n2\n00:00:03,000 --> 00:00:04,000\nhello.\n";
        var imported = LyricsImport.Decode(".srt", Encoding.UTF8.GetBytes(srt));
        var project = TimingProject.Empty() with { Lines = imported.Lines, OriginalLines = imported.Lines };
        Assert.AreEqual("Hello\nHello", DistributionLyrics.Format(DistributionLyrics.FromProject(project)).Text);
    }

    [TestMethod]
    public void LimitsRejectOversizedInputAndBoundReviewDetails()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => DistributionLyrics.Format(new string('a', DistributionLyrics.MaxCharacters + 1)));
        Assert.ThrowsExactly<InvalidDataException>(() => DistributionLyrics.Inspect(new string('a', DistributionLyrics.MaxLineCharacters + 1)));
        Assert.ThrowsExactly<InvalidDataException>(() => DistributionLyrics.Format(string.Join('\n', Enumerable.Repeat("x", DistributionLyrics.MaxLines + 1))));
        var result = DistributionLyrics.Format(string.Join('\n', Enumerable.Repeat("NASA.", 300)));
        Assert.AreEqual(300, result.ChangeCount); Assert.AreEqual(DistributionLyrics.MaxDetails, result.Changes.Count);
        Assert.AreEqual(300, result.WarningCount); Assert.AreEqual(DistributionLyrics.MaxDetails, result.Warnings.Count);
    }

    [TestMethod]
    public void EmptyMetadataOnlyAndPunctuationOnlyHaveNoOutputLyrics()
    {
        foreach (var input in new[] { "", "\r\n \n", "[Intro]\n[Chorus]", ".\n、\n…" })
            Assert.AreEqual("", DistributionLyrics.Format(input).Text);
    }
}

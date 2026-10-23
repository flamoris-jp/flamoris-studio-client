using System.Collections.Immutable;
using System.Text;
using Flamoris.StudioClient.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class TimingTests
{
    [TestMethod]
    public void RecordingEditsAndUndoRetainOriginalAndStableIds()
    {
        var session = new TimingSession(); session.SetLyrics("セロリ\n宇宙へ");
        var original = session.Project; var id = original.Lines[0].Id;
        session.SetRange(id, 10, 20);
        var recorded = session.Project;
        session.SetRange(id, 15, 25);
        session.Undo(); Assert.AreEqual(recorded, session.Project);
        session.Redo(); Assert.AreEqual(15L, session.Project.Lines[0].StartTicks);
        Assert.AreEqual(original.OriginalLines, session.Project.OriginalLines);
        Assert.AreEqual(id, session.Project.Lines[0].Id);
    }
    [TestMethod]
    public void GestureWithStaleRevisionFailsWithoutHistoryOrStateChanges()
    {
        var session = new TimingSession(); session.SetLyrics("歌詞"); var revision = session.Revision;
        var id = session.Project.Lines[0].Id; session.SetRange(id, 10, 20);
        var before = session.Project;
        Assert.ThrowsExactly<InvalidOperationException>(() => session.SetRange(id, 15, 30, revision));
        Assert.AreEqual(before, session.Project); session.Undo(); Assert.IsFalse(session.Project.Lines[0].IsTimed);
    }
    [TestMethod]
    public void InvalidRangeIsAtomic()
    {
        var session = new TimingSession(); session.SetLyrics("歌詞"); var before = session.Project;
        Assert.ThrowsExactly<InvalidDataException>(() => session.SetRange(before.Lines[0].Id, 30, 20));
        Assert.AreEqual(before, session.Project);
    }
    [TestMethod]
    public void ProjectReopenPreservesUnicodePendingTimingAndOriginal()
    {
        var session = new TimingSession(); session.SetLyrics("愛乃🐱\n未来"); session.SetRange(session.Project.Lines[0].Id, 12345678, 23456789);
        var loaded = TimingFormats.LoadProject(TimingFormats.SaveProject(session.Project));
        CollectionAssert.AreEqual(session.Project.Lines.ToArray(), loaded.Lines.ToArray());
        CollectionAssert.AreEqual(session.Project.OriginalLines.ToArray(), loaded.OriginalLines.ToArray());
        Assert.AreEqual(session.Project.Id, loaded.Id);
    }
    [TestMethod]
    public void PortableTimingPreservesTicksWithoutPrivatePaths()
    {
        var session = new TimingSession(); session.SetLyrics("文字"); session.SetRange(session.Project.Lines[0].Id, 123456789, 234567891);
        session.Execute(p => p with { MusicPath = Path.GetFullPath("private.wav") });
        var bytes = TimingFormats.ExportTiming(session.Project);
        Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains("private.wav", StringComparison.Ordinal));
        CollectionAssert.AreEqual(session.Project.Lines.ToArray(), TimingFormats.ImportJson(bytes).ToArray());
    }
    [TestMethod]
    public void RejectUnsupportedVersionsAndUnknownProjectFields()
    {
        var bytes = TimingFormats.SaveProject(TimingProject.Empty()); var text = Encoding.UTF8.GetString(bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => TimingFormats.LoadProject(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"))));
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => TimingFormats.LoadProject(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\": 1", "\"unexpected\": true, \"schemaVersion\": 1"))));
        Assert.ThrowsExactly<InvalidDataException>(() => TimingFormats.ImportJson(Encoding.UTF8.GetBytes("{\"format\":\"flamoris.lyrics-timing\",\"schemaVersion\":2,\"timebase\":10000000,\"documentId\":\""+Guid.NewGuid()+"\",\"lines\":[]}")));
    }
    [TestMethod]
    public void PonponInputDoesNotNeedProjectOrSongMetadata()
    {
        var lines = TimingFormats.ImportJson(Encoding.UTF8.GetBytes("{\"lines\":[{\"text\":\"ぽん\",\"pressTime\":1.2345678,\"releaseTime\":2.3456789}]}"));
        Assert.AreEqual(12345678L, lines[0].StartTicks); Assert.AreEqual(23456789L, lines[0].EndTicks);
        var project = TimingProject.Empty() with { Lines = lines, OriginalLines = lines };
        var reread = TimingFormats.ImportJson(TimingFormats.ExportPonpon(project));
        Assert.AreEqual(lines[0].StartTicks, reread[0].StartTicks);
        Assert.AreEqual(lines[0].Id, reread[0].Id);
        using var legacy = System.Text.Json.JsonDocument.Parse(TimingFormats.ExportPonpon(project));
        Assert.AreEqual(0, legacy.RootElement.GetProperty("lines")[0].GetProperty("id").GetInt32());
        Assert.AreEqual("done", legacy.RootElement.GetProperty("lines")[0].GetProperty("state").GetString());
        Assert.AreEqual("press_release", legacy.RootElement.GetProperty("project").GetProperty("mode").GetString());
    }
    [TestMethod]
    public void SrtRoundTripIsMillisecondAccurateAndKeepsMultilineText()
    {
        var lines = TimingFormats.ImportSrt("1\n00:00:01,234 --> 00:00:02,345\n愛乃\n美祥\n\n2\n01:02:03,004 --> 01:02:04,005\nセロリ\n");
        var project = TimingProject.Empty() with { Lines = lines, OriginalLines = lines };
        var back = TimingFormats.ImportSrt(TimingFormats.ExportSrt(project));
        Assert.AreEqual(lines[1].StartTicks, back[1].StartTicks); Assert.AreEqual(lines[0].Text, back[0].Text);
    }
    [TestMethod]
    public void SrtCannotSilentlyCollapseSubMillisecondRanges()
    {
        var line = new LyricLine(Guid.NewGuid(), "短い", 100, 101);
        Assert.ThrowsExactly<InvalidDataException>(() => TimingFormats.ExportSrt(TimingProject.Empty() with { Lines = [line] }));
    }
    [TestMethod]
    public void ImportRejectsDuplicateIdsAndBrokenRanges()
    {
        var line = new LyricLine(Guid.NewGuid(), "同じ", 10, 20);
        Assert.ThrowsExactly<InvalidDataException>(() => (TimingProject.Empty() with { Lines = [line, line] }).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => TimingFormats.ImportSrt("1\n00:60:00,000 --> 01:00:01,000\n不正"));
        Assert.ThrowsExactly<InvalidDataException>(() => TimingFormats.ImportJson(Encoding.UTF8.GetBytes("{\"lines\":[{\"text\":\"不正\",\"pressTime\":2,\"releaseTime\":1}]}")));
    }
}

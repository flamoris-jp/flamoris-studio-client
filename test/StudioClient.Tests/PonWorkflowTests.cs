using System.Text;
using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class PonWorkflowTests
{
    [TestMethod]
    public void AnnotationGroupsAreExcludedButInlineLyricsAndUnclosedGroupsRemain()
    {
        foreach (var text in new[] { "[verse1]", " [Chorus: Female] ", "(whisper)", "（間奏）", "［Intro］", "[Verse] (soft)", "(echo (soft))" })
            Assert.IsTrue(LyricText.IsAnnotation(text), text);
        foreach (var text in new[] { "歌詞（echo）", "(hello) world", "[Verse] 歌う", "[unfinished", "(unfinished", "(one) lyric (two)", "" })
            Assert.IsFalse(LyricText.IsAnnotation(text), text);
        var session = new TimingSession(); session.SetLyrics("[verse1]\n歌詞（echo）\n(whisper)\n次の行\n（間奏）");
        CollectionAssert.AreEqual(new[] { "歌詞（echo）", "次の行" }, session.Project.Lines.Select(l => l.Text).ToArray());
        Assert.HasCount(5, session.Project.OriginalLines);
        Assert.AreEqual(session.Project.OriginalLines[1].Id, session.Project.Lines[0].Id);
    }

    [TestMethod]
    public void ExistingProjectAndTimedImportsRetainOriginalSnapshotIdsAndTimings()
    {
        foreach (var imported in new[]
        {
            LyricsImport.Decode(".srt", Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\n[verse1]\n\n2\n00:00:03,000 --> 00:00:04,000\n歌詞\n")),
            LyricsImport.Decode(".json", Encoding.UTF8.GetBytes("{\"lines\":[{\"text\":\"(whisper)\",\"pressTime\":1,\"releaseTime\":2},{\"text\":\"歌詞\",\"pressTime\":3,\"releaseTime\":4}]}"))
        })
        {
            var project = TimingProject.Empty() with { Lines = imported.Lines, OriginalLines = imported.Lines };
            var session = new TimingSession(); session.Open(TimingFormats.LoadProject(TimingFormats.SaveProject(project)));
            Assert.HasCount(1, session.Project.Lines); Assert.HasCount(2, session.Project.OriginalLines);
            Assert.AreEqual(imported.Lines[1], session.Project.Lines[0]);
            var saved = TimingFormats.LoadProject(TimingFormats.SaveProject(session.Project));
            CollectionAssert.AreEqual(imported.Lines.ToArray(), saved.OriginalLines.ToArray());
            Assert.IsFalse(TimingFormats.ExportSrt(saved).Contains("whisper", StringComparison.Ordinal));
            Assert.IsFalse(TimingFormats.ExportSrt(saved).Contains("[verse1]", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void UndoPonRestoresOverwrittenRangeAndFollowedPredecessorInOneStep()
    {
        var session = new TimingSession(); session.SetLyrics("前\n次\n最後");
        var ids = session.Project.Lines.Select(l => l.Id).ToArray();
        session.SetRange(ids[0], 10, 20); session.SetRange(ids[1], 30, 40);
        var original = session.Project;
        Assert.IsFalse(session.CanUndoPon);
        session.RecordPon(ids[1], 35, 45, true, session.Revision);
        Assert.AreEqual(35L, session.Project.Lines[0].EndTicks);
        var recording = session.UndoPon();
        Assert.AreEqual(new PonRecording(ids[1], 35), recording);
        Assert.AreEqual(original, session.Project);
        session.Redo(); Assert.IsTrue(session.CanUndoPon);
        session.UndoPon(); Assert.AreEqual(original, session.Project);
    }

    [TestMethod]
    public void RepeatedUndoPonStopsBeforeUnrelatedEditsAndRejectsStaleHolds()
    {
        var session = new TimingSession(); session.SetLyrics("1\n2\n3");
        var original = session.Project; var ids = original.Lines.Select(l => l.Id).ToArray();
        session.RecordPon(ids[0], 10, 20, false, session.Revision);
        session.RecordPon(ids[1], 30, 40, false, session.Revision);
        Assert.AreEqual(ids[1], session.UndoPon()!.LineId);
        Assert.AreEqual(ids[0], session.UndoPon()!.LineId);
        Assert.AreEqual(original, session.Project); Assert.IsNull(session.UndoPon());
        session.Redo(); session.SetRange(ids[2], 50, 60);
        var before = session.Project; var revision = session.Revision;
        Assert.IsNull(session.UndoPon()); Assert.AreEqual(before, session.Project); Assert.AreEqual(revision, session.Revision);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.RecordPon(ids[1], 30, 40, false, revision - 1));
        Assert.AreEqual(before, session.Project);
        session.Undo(); Assert.IsTrue(session.CanUndoPon);
    }

    [TestMethod]
    public void ClearTimingsKeepsLyricsSourcesAndOriginalAndIsOneUndoableEdit()
    {
        var session = new TimingSession(); session.SetLyrics("[verse1]\n歌詞\n未記録");
        session.Execute(p => p with { MusicPath = Path.GetFullPath("music.wav"), VocalPath = Path.GetFullPath("stem.wav") });
        var id = session.Project.Lines[0].Id;
        session.RecordPon(id, 10, 20, false, session.Revision);
        var before = session.Project;
        session.Execute(TimingEdits.ClearTimings);
        Assert.IsTrue(session.Project.Lines.All(l => !l.IsTimed));
        Assert.AreEqual(before.MusicPath, session.Project.MusicPath); Assert.AreEqual(before.VocalPath, session.Project.VocalPath);
        Assert.AreEqual(before.Id, session.Project.Id); Assert.AreEqual(before.OriginalLines, session.Project.OriginalLines);
        CollectionAssert.AreEqual(before.Lines.Select(l => (l.Id, l.Text)).ToArray(), session.Project.Lines.Select(l => (l.Id, l.Text)).ToArray());
        Assert.IsFalse(session.CanUndoPon);
        var revision = session.Revision; Assert.IsFalse(session.Execute(TimingEdits.ClearTimings)); Assert.AreEqual(revision, session.Revision);
        session.Undo(); Assert.AreEqual(before, session.Project); Assert.IsTrue(session.CanUndoPon);
        session.Redo(); Assert.IsTrue(session.Project.Lines.All(l => !l.IsTimed));
    }
}

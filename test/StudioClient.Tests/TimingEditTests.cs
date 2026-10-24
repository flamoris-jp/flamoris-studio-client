using System.Collections.Immutable;
using Flamoris.StudioClient.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class TimingEditTests
{
    private static TimingProject Fixture() => TimingProject.Empty() with { Lines = [
        new(Guid.NewGuid(), "前", 20_000_000, 30_000_000),
        new(Guid.NewGuid(), "次", 35_000_000, 40_000_000),
        new(Guid.NewGuid(), "未記録"),
        new(Guid.NewGuid(), "終", 60_000_000, 70_000_000)] };

    [TestMethod]
    public void ShiftMultipleRowsIsOneUndoAndPreservesIdsLengthsAndOriginal()
    {
        var project = Fixture(); project = project with { OriginalLines = project.Lines };
        var session = new TimingSession(); session.Open(project);
        var targets = new HashSet<Guid> { project.Lines[0].Id, project.Lines[1].Id, project.Lines[2].Id };
        session.Execute(p => TimingEdits.Shift(p, targets, 100_000, false));
        var shifted = session.Project;
        Assert.AreEqual(20_100_000L, shifted.Lines[0].StartTicks);
        Assert.AreEqual(30_100_000L, shifted.Lines[0].EndTicks);
        Assert.AreEqual(project.Lines[3], shifted.Lines[3]);
        Assert.AreEqual(project.Lines[2], shifted.Lines[2]);
        Assert.AreEqual(project.OriginalLines, shifted.OriginalLines);
        session.Undo(); Assert.AreEqual(project, session.Project); Assert.IsFalse(session.CanUndo);
        session.Redo(); Assert.AreEqual(shifted, session.Project);
        var loaded = TimingFormats.LoadProject(TimingFormats.SaveProject(shifted));
        CollectionAssert.AreEqual(shifted.Lines.ToArray(), loaded.Lines.ToArray());
        CollectionAssert.AreEqual(shifted.Lines.ToArray(), TimingFormats.ImportJson(TimingFormats.ExportTiming(shifted)).ToArray());
        Assert.AreEqual(shifted.Lines[0].StartTicks, TimingFormats.ImportSrt(TimingFormats.ExportSrt(shifted))[0].StartTicks);
    }

    [TestMethod]
    public void BoundaryOrEmptyTargetFailureLeavesAllRowsAndHistoryUnchanged()
    {
        var session = new TimingSession(); session.Open(Fixture()); var before = session.Project;
        var all = before.Lines.Select(l => l.Id).ToHashSet();
        Assert.ThrowsExactly<InvalidDataException>(() => session.Execute(p => TimingEdits.Shift(p, all, -21_000_000, false)));
        Assert.ThrowsExactly<InvalidDataException>(() => session.Execute(p => TimingEdits.Shift(p, all, TimeSpan.FromHours(24).Ticks, false)));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Execute(p => TimingEdits.Shift(p, new HashSet<Guid>(), 1, false)));
        Assert.AreEqual(before, session.Project); Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public void AlignChangesOnlyImmediatePredecessorEndAndIsIdempotent()
    {
        var session = new TimingSession(); session.Open(Fixture()); var before = session.Project;
        var targets = new HashSet<Guid> { before.Lines[1].Id, before.Lines[3].Id };
        session.Execute(p => TimingEdits.AlignPrevious(p, targets));
        Assert.AreEqual(35_000_000L, session.Project.Lines[0].EndTicks);
        Assert.AreEqual(before.Lines[1], session.Project.Lines[1]);
        Assert.AreEqual(before.Lines[2], session.Project.Lines[2]); // Never jump over pending rows.
        var revision = session.Revision;
        session.Execute(p => TimingEdits.AlignPrevious(p, targets)); Assert.AreEqual(revision, session.Revision);
        session.Undo(); Assert.AreEqual(before, session.Project);
    }

    [TestMethod]
    public void FollowRangeIsAtomicAndChecksGestureRevision()
    {
        var session = new TimingSession(); session.Open(Fixture()); var before = session.Project;
        var id = before.Lines[1].Id; var revision = session.Revision;
        session.Execute(p => TimingEdits.Range(p, id, 36_000_000, 41_000_000, true), revision);
        Assert.AreEqual(36_000_000L, session.Project.Lines[0].EndTicks);
        Assert.AreEqual(36_000_000L, session.Project.Lines[1].StartTicks);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Execute(p => TimingEdits.Range(p, id, 37_000_000, 42_000_000, true), revision));
        session.Undo(); Assert.AreEqual(before, session.Project);
        Assert.ThrowsExactly<InvalidDataException>(() => session.Execute(p => TimingEdits.Range(p, id, 19_000_000, 40_000_000, true)));
        Assert.AreEqual(before, session.Project);
    }

    [TestMethod]
    public void AlignAllUsesSnapshotAndShiftFollowDoesNotDependOnIterationOrder()
    {
        var p = Fixture(); p = p with { Lines = p.Lines.SetItem(2, p.Lines[2] with { StartTicks = 45_000_000, EndTicks = 50_000_000 }) };
        var all = p.Lines.Select(l => l.Id).ToHashSet();
        var result = TimingEdits.Shift(p, all, 10_000, true);
        for (var i = 1; i < result.Lines.Length; i++) Assert.AreEqual(result.Lines[i].StartTicks, result.Lines[i - 1].EndTicks);
        Assert.AreEqual(70_010_000L, result.Lines[3].EndTicks);
        Assert.AreEqual(20_010_000L, result.Lines[0].StartTicks);
        var reversed = TimingEdits.Shift(p, all.Reverse().ToHashSet(), 10_000, true);
        CollectionAssert.AreEqual(result.Lines.ToArray(), reversed.Lines.ToArray());
    }

    [TestMethod]
    public void FirstOrPendingAlignHasNoHiddenTargetAndEndTrimDoesNotReAlign()
    {
        var p = Fixture();
        Assert.ThrowsExactly<InvalidOperationException>(() => TimingEdits.AlignPrevious(p, new HashSet<Guid> { p.Lines[0].Id, p.Lines[2].Id, p.Lines[3].Id }));
        var result = TimingEdits.Range(p, p.Lines[1].Id, 35_000_000, 42_000_000, true);
        Assert.AreEqual(p.Lines[0], result.Lines[0]);
    }

    [TestMethod]
    public void AdjustmentsHaveExplicitUnitsAndRepeatedTicksDoNotDrift()
    {
        Assert.AreEqual(100_000L, TimingEdits.ParseAdjustment("10ms"));
        Assert.AreEqual(100_000L, TimingEdits.ParseAdjustment("0.010秒"));
        Assert.AreEqual(100_000L, TimingEdits.ParseAdjustment("0.010s"));
        Assert.AreEqual(100_000L, TimingEdits.ParseAdjustment("10"));
        foreach (var bad in new[] { "0", "-1ms", "NaN", "1,000", "0.000001ms", "1minute" })
            Assert.ThrowsExactly<InvalidDataException>(() => TimingEdits.ParseAdjustment(bad));
        var p = Fixture(); var before = p; var targets = p.Lines.Select(l => l.Id).ToHashSet();
        for (var i = 0; i < 1000; i++) p = TimingEdits.Shift(p, targets, TimingEdits.ParseAdjustment("1ms"), false);
        for (var i = 0; i < 1000; i++) p = TimingEdits.Shift(p, targets, -TimingEdits.ParseAdjustment("1ms"), false);
        CollectionAssert.AreEqual(before.Lines.ToArray(), p.Lines.ToArray());
    }
}

using Flamoris.StudioClient.Core;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class DragAndRecoveryTests
{
    private string root = null!;
    [TestInitialize] public void Initialize() => root = Path.Combine(Path.GetTempPath(), "studio-recovery-tests-" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestMethod]
    public void DragBoundsKeepBothEdgesAndMoveWithinValidTime()
    {
        var line = new LyricLine(Guid.NewGuid(), "test", 10_000_000, 20_000_000);
        Assert.AreEqual((0L, 20_000_000L), TimingDrag.Range(line, TimingDragKind.Start, -1e300));
        Assert.AreEqual((19_999_999L, 20_000_000L), TimingDrag.Range(line, TimingDragKind.Start, 1e300));
        Assert.AreEqual((10_000_000L, 10_000_001L), TimingDrag.Range(line, TimingDragKind.End, -1e300));
        Assert.AreEqual((0L, 10_000_000L), TimingDrag.Range(line, TimingDragKind.Move, -1e300));
        var limit = TimeSpan.FromHours(24).Ticks;
        Assert.AreEqual((limit - 10_000_000, limit), TimingDrag.Range(line, TimingDragKind.Move, 1e300));
        Assert.AreEqual((10_000_000L, limit), TimingDrag.Range(line, TimingDragKind.End, 1e300));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.ThrowsExactly<InvalidDataException>(() => TimingDrag.Range(line, TimingDragKind.End, value));
    }

    [TestMethod]
    public void SubTickLyricAndRepeatedResizeRemainValid()
    {
        var line = new LyricLine(Guid.NewGuid(), "short", 0, 1);
        foreach (var kind in Enum.GetValues<TimingDragKind>())
            foreach (var delta in new[] { -1e6, -1, 0, .0000001, 1, 1e6 })
            {
                var range = TimingDrag.Range(line, kind, delta);
                TimingProject.ValidateLines([line with { StartTicks = range.Start, EndTicks = range.End }]);
            }
    }

    [TestMethod]
    public void CheckpointsSurviveAbruptClosureButExcludeOtherLiveInstances()
    {
        var session = new TimingSession(); session.SetLyrics("元の歌詞\n次");
        session.SetRange(session.Project.Lines[0].Id, 10, 20);
        using var reader = new RecoveryStore(root);
        string path;
        using (var writer = new RecoveryStore(root))
        {
            writer.Checkpoint(session.Project); path = writer.CheckpointPath;
            Assert.HasCount(0, reader.Candidates());
            Assert.ThrowsExactly<IOException>(() => reader.Read(path));
            Assert.ThrowsExactly<IOException>(() => reader.Remove(path));
        }
        CollectionAssert.AreEqual(new[] { path }, reader.Candidates().ToArray());
        var loaded = reader.Read(path);
        CollectionAssert.AreEqual(session.Project.Lines.ToArray(), loaded.Lines.ToArray());
        CollectionAssert.AreEqual(session.Project.OriginalLines.ToArray(), loaded.OriginalLines.ToArray());
        reader.Checkpoint(loaded); reader.Remove(path);
        Assert.IsTrue(File.Exists(reader.CheckpointPath)); Assert.IsFalse(File.Exists(path));
        reader.Clear(); Assert.IsFalse(File.Exists(reader.CheckpointPath));
    }

    [TestMethod]
    public void FailedCheckpointPreservesLastValidatedProjectAndDoesNotTouchManualSave()
    {
        var session = new TimingSession(); session.SetLyrics("keep");
        using var store = new RecoveryStore(root); store.Checkpoint(session.Project);
        var before = File.ReadAllBytes(store.CheckpointPath);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Checkpoint(session.Project with { Lines = [new(Guid.Empty, "bad")] }));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(store.CheckpointPath));
        Assert.HasCount(0, Directory.GetFiles(root, "*.tmp"));
        var manual = Path.Combine(root, "manual.fponproj"); File.WriteAllText(manual, "manual");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Remove(manual));
        Assert.AreEqual("manual", File.ReadAllText(manual));
    }

    [TestMethod]
    public void RecoveryRejectsOutsideReferencesOversizeAndSymlinkFiles()
    {
        Directory.CreateDirectory(root); using var store = new RecoveryStore(root);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(Path.Combine(root, "..", Guid.NewGuid().ToString("N") + ".fponproj")));
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".fponproj");
        using (var file = File.Create(path)) file.SetLength(TimingFormats.MaxBytes + 1L);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(path));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(root, Guid.NewGuid().ToString("N") + ".fponproj"); File.CreateSymbolicLink(link, path);
            Assert.ThrowsExactly<IOException>(() => store.Read(link)); Assert.ThrowsExactly<IOException>(() => store.Remove(link));
            Assert.IsFalse(store.Candidates().Contains(link));
        }
    }

    [TestMethod]
    public void DiagnosticsRetainTypesAndFramesWithoutExceptionMessagesOrPaths()
    {
        var diagnostics = new StudioDiagnostics(root);
        try { throw new InvalidOperationException("PRIVATE_LYRIC C:/private/music.wav secret-token"); }
        catch (Exception error) { diagnostics.Write(error, "test.gesture"); }
        var text = File.ReadAllText(Path.Combine(root, "logs", "studioclient.log"));
        StringAssert.Contains(text, "InvalidOperationException");
        StringAssert.Contains(text, "test.gesture");
        Assert.IsFalse(text.Contains("PRIVATE_LYRIC", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("music.wav", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("secret-token", StringComparison.Ordinal));
    }
}

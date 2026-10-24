using Flamoris.StudioClient.Core;

namespace Flamoris.StudioClient.Infrastructure;

// Each process owns one checkpoint and an exclusive live marker. No user save path
// is accepted here. Publication is synchronous after a completed command, not per pixel.
public sealed class RecoveryStore(string root) : IDisposable
{
    private readonly string id = Guid.NewGuid().ToString("N");
    private FileStream? active;
    public string Root { get; } = Path.GetFullPath(root);
    public string CheckpointPath => Path.Combine(Root, id + ".fponproj");
    public void Checkpoint(TimingProject project)
    {
        var bytes = TimingFormats.SaveProject(project);
        if (bytes.Length > TimingFormats.MaxBytes) throw new InvalidDataException("復旧用データが16 MiBを超えています。");
        Directory.CreateDirectory(Root); RejectLink(Root);
        active ??= new FileStream(Path.Combine(Root, id + ".active"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 4096, FileOptions.DeleteOnClose);
        RejectLink(CheckpointPath);
        var temporary = Path.Combine(Root, id + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, CheckpointPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public IReadOnlyList<string> Candidates()
    {
        if (!Directory.Exists(Root)) return [];
        RejectLink(Root);
        return Directory.EnumerateFiles(Root, "*.fponproj").Take(128)
            .Where(path => path != CheckpointPath && !File.Exists(Path.ChangeExtension(path, ".active")) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(20).ToArray();
    }
    public TimingProject Read(string candidate)
    {
        CheckCandidate(candidate); RejectLink(candidate);
        if (File.Exists(Path.ChangeExtension(candidate, ".active"))) throw new IOException("起動中の復旧データは読み込めません。");
        using var input = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > TimingFormats.MaxBytes) throw new InvalidDataException("復旧用データが16 MiBを超えています。");
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        return TimingFormats.LoadProject(bytes);
    }
    public void Remove(string candidate)
    {
        CheckCandidate(candidate); RejectLink(candidate);
        if (File.Exists(Path.ChangeExtension(candidate, ".active")) && candidate != CheckpointPath)
            throw new IOException("別の起動中のアプリが使っている復旧データです。");
        File.Delete(candidate);
    }
    public void Clear() => Remove(CheckpointPath);
    private void CheckCandidate(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            Path.GetExtension(path) != ".fponproj" || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _))
            throw new InvalidDataException("復旧データの参照が不正です。");
    }
    private static void RejectLink(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("復旧用の場所にリンクは使えません。");
    }
    // Dispose preserves the checkpoint for abnormal closure; explicit Save/discard clears it.
    public void Dispose() { active?.Dispose(); active = null; }
}

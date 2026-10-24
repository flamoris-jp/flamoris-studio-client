using System.Text;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class AudioProcessTests
{
    private string root = null!;
    private string Python => Environment.GetEnvironmentVariable("FLAMORIS_STUDIO_CLIENT_PYTHON") ?? (OperatingSystem.IsWindows() ? "python" : "python3");
    [TestInitialize] public void Initialize() { root = Path.Combine(Path.GetTempPath(), "ponpon-audio-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TestCleanup] public void Cleanup() { Directory.Delete(root, true); }
    private string CreateAudio()
    {
        var path = Path.Combine(root, "音源 with spaces.wav");
        const int rate = 22050, count = rate * 3;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
        for (var i = 0; i < count; i++) writer.Write((short)(.2 * short.MaxValue * (Math.Sin(i * 2 * Math.PI * 261.63 / rate) + Math.Sin(i * 2 * Math.PI * 329.63 / rate) + Math.Sin(i * 2 * Math.PI * 392 / rate))));
        return path;
    }
    [TestMethod]
    public async Task RealWorkerAnalyzesSelectedAudioWithoutLyrics()
    {
        using var worker = new AudioWorker(Python, Path.Combine(AppContext.BaseDirectory, "audio-worker", "worker.py"), Path.Combine(root, "cache"));
        var input = CreateAudio(); var result = await worker.RunAsync(input, true, null, CancellationToken.None);
        Assert.AreEqual("analyze", result.Features.Mode); Assert.AreEqual("C", result.Features.Chords[0].Label);
        Assert.IsTrue(await result.Features.IsCurrentAsync(input)); Assert.IsNull(result.PreviewPath);
        Assert.IsTrue(result.Features.Waveform.Length > 0);
    }
    [TestMethod]
    public async Task PrepareWorkerPublishesPlayablePreviewAndCleansOwnedCacheOnDispose()
    {
        var cache = Path.Combine(root, "cache");
        using (var worker = new AudioWorker(Python, Path.Combine(AppContext.BaseDirectory, "audio-worker", "worker.py"), cache))
        {
            var result = await worker.RunAsync(CreateAudio(), false, null, CancellationToken.None);
            Assert.AreEqual("prepare", result.Features.Mode);
            var bytes = await File.ReadAllBytesAsync(result.PreviewPath!);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(bytes, 0, 4)); Assert.IsTrue(bytes.Length > 48000);
        }
        Assert.IsFalse(Directory.Exists(cache));
    }
    [TestMethod]
    public async Task CancellationJoinsWorkerCleansDraftAndRejectsConcurrentJobs()
    {
        var script = Path.Combine(root, "slow.py");
        await File.WriteAllTextAsync(script, "import json,time\nprint(json.dumps({'progress': 1, 'stage': 'ready'}), flush=True)\ntime.sleep(60)\n");
        var cache = Path.Combine(root, "cache"); using var worker = new AudioWorker(Python, script, cache);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var progress = new Progress<(int Percent, string Stage)>(_ => started.TrySetResult());
        var first = worker.RunAsync(CreateAudio(), true, progress, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => worker.RunAsync(CreateAudio(), true, null, CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await first);
        Assert.AreEqual(0, Directory.GetDirectories(cache).Length);
    }
}

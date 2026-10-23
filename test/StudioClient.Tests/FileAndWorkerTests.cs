using System.Security.Cryptography;
using System.Text;
using Flamoris.StudioClient.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class FileAndWorkerTests
{
    private string root = null!;
    [TestInitialize] public void Initialize() { root = Path.Combine(Path.GetTempPath(), "ponpon-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TestCleanup] public void Cleanup() { Directory.Delete(root, true); }
    [TestMethod]
    public async Task SaveDetectsConcurrentChangeAndRetainsOldBytes()
    {
        var path = Path.Combine(root, "project.fponproj");
        var hash = await LocalFiles.PublishAsync(path, "first"u8.ToArray(), null);
        await File.WriteAllTextAsync(path, "external");
        await Assert.ThrowsExactlyAsync<IOException>(() => LocalFiles.PublishAsync(path, "second"u8.ToArray(), hash));
        Assert.AreEqual("external", await File.ReadAllTextAsync(path));
        Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
    }
    [TestMethod]
    public async Task SaveDoesNotOverwriteWithoutExpectedFingerprint()
    {
        var path = Path.Combine(root, "exists"); await File.WriteAllTextAsync(path, "original");
        await Assert.ThrowsExactlyAsync<IOException>(() => LocalFiles.PublishAsync(path, "new"u8.ToArray(), null));
        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
    }
    [TestMethod]
    public async Task SuccessfulReplaceUsesHashAndNoTemporaryFileRemains()
    {
        var path = Path.Combine(root, "document"); var hash = await LocalFiles.PublishAsync(path, "one"u8.ToArray(), null);
        var second = await LocalFiles.PublishAsync(path, "two"u8.ToArray(), hash);
        Assert.AreEqual(second, await LocalFiles.FingerprintAsync(path)); Assert.AreEqual("two", await File.ReadAllTextAsync(path));
        Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
    }
    [TestMethod]
    public async Task SameLengthChangedAudioInvalidatesAnalysisByContent()
    {
        var path = Path.Combine(root, "audio"); await File.WriteAllTextAsync(path, "aaaa");
        var source = new AudioIdentity(await LocalFiles.FingerprintAsync(path), 4, File.GetLastWriteTimeUtc(path).Ticks.ToString());
        var features = new AudioFeatures("flamoris.audio-features", 1, "test", "analyze", source, 1, 11025, [], null, [], null, [], null);
        Assert.IsTrue(await features.IsCurrentAsync(path));
        await File.WriteAllTextAsync(path, "bbbb"); Assert.IsFalse(await features.IsCurrentAsync(path));
    }
    [TestMethod]
    public void RejectWorkerPathEscapeAndNonFiniteOutputs()
    {
        var source = new AudioIdentity(new string('A', 64), 4, "1");
        var valid = new AudioFeatures("flamoris.audio-features", 1, "test", "prepare", source, 1, 11025, [], null, [], null, [], "preview.wav");
        valid.Validate();
        Assert.ThrowsExactly<InvalidDataException>(() => (valid with { Preview = "../outside.wav" }).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => (valid with { Duration = double.NaN }).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => (valid with { Beats = [0, 0] }).Validate());
    }
}

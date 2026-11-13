using GenshinPiano.Infrastructure.Ocr;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class OcrWorkerExecutableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GPiano-WorkerName-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "GenshinPiano.exe");

    public OcrWorkerExecutableTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Source, [1, 2, 3, 4]);
    }

    [Fact]
    public void NamedWorkerSharesTheApplicationDirectoryAndContents()
    {
        var worker = new OcrWorkerExecutable(Source);
        var path = worker.Prepare();
        Assert.Equal(Path.Combine(_root, "GenshinPiano.Ocr.exe"), path);
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(path));
        Assert.Equal(path, worker.Prepare());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void UnsupportedHardLinksFallBackToAnIndependentCopy()
    {
        var path = new OcrWorkerExecutable(Source, (_, _) => false).Prepare();
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(path));
        File.WriteAllBytes(Source, [4, 3, 2, 1]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void ApplicationUpdateReplacesStaleAliasEvenWithMatchingSizeAndTimestamp()
    {
        var path = new OcrWorkerExecutable(Source).Prepare();
        var time = File.GetLastWriteTimeUtc(Source);
        var updated = Path.Combine(_root, "updated.exe");
        File.WriteAllBytes(updated, [4, 3, 2, 1]);
        File.SetLastWriteTimeUtc(updated, time);
        File.Move(updated, Source, overwrite: true);
        Assert.Equal(path, new OcrWorkerExecutable(Source).Prepare());
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(path));
    }

    [Fact]
    public void UnchangedAliasIsReusedWithoutReplacingItsFile()
    {
        var path = new OcrWorkerExecutable(Source, (_, _) => false).Prepare();
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reused = new OcrWorkerExecutable(Source, (_, _) => throw new InvalidOperationException("Should reuse existing alias."));
        Assert.Equal(path, reused.Prepare());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

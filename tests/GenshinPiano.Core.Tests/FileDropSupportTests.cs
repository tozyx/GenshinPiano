using System.Windows;
using GenshinPiano.App.Services;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class FileDropSupportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GPiano-FileDrop-" + Guid.NewGuid().ToString("N"));

    public FileDropSupportTests() => Directory.CreateDirectory(_directory);

    private string FilePath(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "test");
        return path;
    }

    [Theory]
    [InlineData(".gpiano", false)]
    [InlineData(".json", false)]
    [InlineData(".mid", false)]
    [InlineData(".midi", false)]
    [InlineData(".MID", false)]
    [InlineData(".MIDI", false)]
    [InlineData(".png", true)]
    [InlineData(".JPG", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".bmp", true)]
    [InlineData(".tif", true)]
    [InlineData(".tiff", true)]
    [InlineData(".webp", true)]
    public void MainWindowAcceptsScoresMidiAndImages_OcrOnlyAcceptsImages(string extension, bool image)
    {
        var path = FilePath("score" + extension);
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        Assert.True(FileDropSupport.TryGetFile(data, out var selected));
        Assert.Equal(path, selected);
        Assert.Equal(image, FileDropSupport.IsSupportedImagePath(path));
        Assert.Equal(image, FileDropSupport.TryGetFile(data, out selected, imagesOnly: true));
        Assert.Equal(image ? path : string.Empty, selected);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData(".svg")]
    [InlineData(".png.exe")]
    public void UnsupportedFilesAreIgnored(string extension)
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { FilePath("score" + extension) });
        Assert.False(FileDropSupport.TryGetFile(data, out _));
        Assert.False(FileDropSupport.TryGetFile(data, out _, imagesOnly: true));
    }

    [Fact]
    public void EachDropUsesFirstSupportedFileInOriginalOrder()
    {
        var text = FilePath("readme.txt");
        var midi = FilePath("score.mid");
        var image = FilePath("scan.png");
        var data = new DataObject(DataFormats.FileDrop, new[] { text, midi, image });
        Assert.True(FileDropSupport.TryGetFile(data, out var selected));
        Assert.Equal(midi, selected);
        Assert.True(FileDropSupport.TryGetFile(data, out selected, imagesOnly: true));
        Assert.Equal(image, selected);
        Assert.True(FileDropSupport.TryGetFile(new DataObject(DataFormats.FileDrop, new[] { image, midi }), out selected));
        Assert.Equal(image, selected);
    }

    [Fact]
    public void MissingFilesDirectoriesAndPlainTextAreIgnored()
    {
        var directory = Path.Combine(_directory, "folder.png");
        Directory.CreateDirectory(directory);
        var data = new DataObject(DataFormats.FileDrop, new[] { directory, Path.Combine(_directory, "missing.mid") });
        Assert.False(FileDropSupport.TryGetFile(data, out _));
        Assert.False(FileDropSupport.TryGetFile(new DataObject(DataFormats.UnicodeText, "score.mid"), out _));
        Assert.False(FileDropSupport.TryGetFile(new DataObject(DataFormats.FileDrop, Array.Empty<string>()), out _));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}

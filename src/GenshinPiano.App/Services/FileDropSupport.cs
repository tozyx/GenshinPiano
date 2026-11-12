using System.IO;
using System.Windows;
using GenshinPiano.App.ViewModels;

namespace GenshinPiano.App.Services;

public static class FileDropSupport
{
    public static bool IsSupportedImagePath(string path) => File.Exists(path) &&
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".webp";

    public static bool TryGetFile(IDataObject data, out string path, bool imagesOnly = false)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return false;
        }

        path = paths.FirstOrDefault(candidate => IsSupportedImagePath(candidate) ||
            (!imagesOnly && MainWindowViewModel.IsSupportedScorePath(candidate))) ?? string.Empty;
        return path.Length > 0;
    }
}

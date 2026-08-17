using GenshinPiano.App.Commands;
using GenshinPiano.Application.Workspace;
using Microsoft.Win32;

namespace GenshinPiano.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ScoreWorkspace _workspace;
    private string _scoreTitle;
    private string _statusText = "就绪";

    public MainWindowViewModel(ScoreWorkspace workspace)
    {
        _workspace = workspace;
        _scoreTitle = workspace.CurrentScore.Metadata.Title;

        NewCommand = new RelayCommand(CreateNew);
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync);
    }

    public string WindowTitle => $"{ScoreTitle} - GenshinPiano v3";

    public string ScoreTitle
    {
        get => _scoreTitle;
        private set
        {
            if (SetProperty(ref _scoreTitle, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public RelayCommand NewCommand { get; }

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand SaveAsCommand { get; }

    private void CreateNew()
    {
        _workspace.CreateNew();
        RefreshFromWorkspace();
        StatusText = "已新建曲谱";
    }

    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 GenshinPiano 曲谱",
            Filter = "GenshinPiano v3 曲谱 (*.gpiano)|*.gpiano|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            StatusText = "正在打开曲谱…";
            await _workspace.LoadAsync(dialog.FileName);
            RefreshFromWorkspace();
            StatusText = $"已打开 {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception)
        {
            StatusText = $"打开失败：{exception.Message}";
        }
    }

    private async Task SaveAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 GenshinPiano 曲谱",
            Filter = "GenshinPiano v3 曲谱 (*.gpiano)|*.gpiano",
            DefaultExt = ".gpiano",
            AddExtension = true,
            FileName = ScoreTitle,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            StatusText = "正在保存曲谱…";
            await _workspace.SaveAsync(dialog.FileName);
            StatusText = $"已保存到 {dialog.FileName}";
        }
        catch (Exception exception)
        {
            StatusText = $"保存失败：{exception.Message}";
        }
    }

    private void RefreshFromWorkspace() => ScoreTitle = _workspace.CurrentScore.Metadata.Title;
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Dialogs;

public partial class MidiImportDialog : Window
{
    private readonly IReadOnlyList<TrackChoice> _tracks;

    public MidiImportDialog(MidiFileInfo fileInfo)
    {
        InitializeComponent();
        FileNameText.Text = fileInfo.FileName;
        _tracks = fileInfo.Tracks.Select(track => new TrackChoice(track)).ToArray();
        TrackItemsControl.ItemsSource = _tracks;
        for (var transpose = -12; transpose <= 12; transpose++)
        {
            TransposeComboBox.Items.Add(new ComboBoxItem
            {
                Content = transpose > 0 ? $"+{transpose}" : transpose.ToString(),
                Tag = transpose,
            });
        }
        TransposeComboBox.SelectedIndex = 12;
    }

    public MidiImportOptions? Options { get; private set; }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = _tracks.Where(track => track.IsSelected).Select(track => track.Index).ToArray();
        if (selected.Length == 0)
        {
            ValidationText.SetResourceReference(TextBlock.TextProperty, "MidiImport_SelectTrack");
            return;
        }

        var transpose = TransposeComboBox.SelectedItem is ComboBoxItem { Tag: int value } ? value : 0;
        var policy = OutOfRangeComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                     Enum.TryParse<OutOfRangePolicy>(tag, out var parsed)
            ? parsed
            : OutOfRangePolicy.OctaveFold;
        Options = new MidiImportOptions(
            IgnorePercussionCheckBox.IsChecked == true,
            policy,
            transpose,
            selected);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private sealed class TrackChoice(MidiTrackInfo track)
    {
        public int Index { get; } = track.Index;
        public string Name { get; } = track.Name;
        public bool IsSelected { get; set; } = track.NoteCount > track.PercussionNoteCount;
        public string NoteSummary { get; } = $"{track.NoteCount} notes";
        public string RangeSummary { get; } = track.MinimumPitch is null
            ? "—"
            : $"MIDI {track.MinimumPitch}–{track.MaximumPitch}";
    }
}

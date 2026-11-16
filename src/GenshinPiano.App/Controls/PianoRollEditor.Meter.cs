using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor
{
    private ScoreMeterMap _meterMap = new(new TimingDefinition());
    private bool _updatingTimeSignature;

    private void SyncTimeSignature()
    {
        if (TimeSignatureBox is null) return;
        _updatingTimeSignature = true;
        try
        {
            var initial = _meterMap.GetSignature(0);
            var text = $"{initial.Numerator}/{initial.Denominator}";
            TimeSignatureBox.SelectedItem = TimeSignatureBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Content as string == text);
            TimeSignatureBox.Text = text;
        }
        finally
        {
            _updatingTimeSignature = false;
        }
    }

    private void ApplyTimeSignature(string text)
    {
        if (_updatingTimeSignature || Score is null) return;
        var parts = text.Replace('／', '/').Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out var numerator) && int.TryParse(parts[1], out var denominator) &&
            numerator is >= 1 and <= 99 && denominator <= 64 && ScoreMeterMap.IsValidSignature(numerator, denominator))
        {
            Surface.SetInitialTimeSignature(numerator, denominator);
        }
        SyncTimeSignature();
    }

    private void TimeSignatureBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingTimeSignature && TimeSignatureBox.SelectedItem is ComboBoxItem { Content: string text })
            ApplyTimeSignature(text);
    }

    private void TimeSignatureBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ApplyTimeSignature(TimeSignatureBox.Text);

    private void TimeSignatureBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (e.Key == Key.Enter) ApplyTimeSignature(TimeSignatureBox.Text);
        else SyncTimeSignature();
        Surface.Focus();
        e.Handled = true;
    }
}

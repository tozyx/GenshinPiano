using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor
{
    private bool _updatingNoteEditor;
    private bool _isDraggingGateRatioPreview;
    private double _gateRatioPercentage = 80;

    private void Surface_OnNoteEditRequested(object? sender, NoteEditRequestedEventArgs e)
    {
        var score = Score;
        if (score is null)
        {
            return;
        }

        _updatingNoteEditor = true;
        try
        {
            var ppq = score.Timing.Ppq;
            var rhythmTick = e.Note.RhythmTick ?? e.Note.DurationTick;
            SelectRhythmOption(rhythmTick, ppq);

            var gateRatio = e.Note.GateRatio ?? (e.Note.DurationMode == DurationMode.Auto
                ? NoteDurationCalculator.GetGateRatio(e.Note.Articulation)
                : e.Note.DurationTick / (double)Math.Max(1, rhythmTick));
            _gateRatioPercentage = Math.Clamp(
                gateRatio * 100,
                NoteDurationCalculator.MinimumGateRatio * 100,
                NoteDurationCalculator.MaximumGateRatio * 100);
        }
        finally
        {
            _updatingNoteEditor = false;
        }

        NoteEditorPopup.HorizontalOffset = e.Anchor.X;
        NoteEditorPopup.VerticalOffset = e.Anchor.Y;
        NoteEditorPopup.IsOpen = true;
        UpdateGateRatioDescription(_gateRatioPercentage);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => GateRatioPreviewTrack.Focus());
    }

    private void UpdateGateRatioDescription(double value)
    {
        var percentage = (int)Math.Round(value);
        GateRatioText.Text = $"{percentage}%";
        UpdateGateRatioPreview(value);
        var descriptionKey = percentage switch
        {
            <= 35 => "Editor_GateHintStaccato",
            <= 60 => "Editor_GateHintDetached",
            <= 87 => "Editor_GateHintNatural",
            _ => "Editor_GateHintLegato",
        };
        GateRatioDescription.Text = System.Windows.Application.Current.TryFindResource(descriptionKey) as string
            ?? string.Empty;
    }

    private void GateRatioPreviewTrack_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateGateRatioPreview(_gateRatioPercentage);

    private void GateRatioPreviewTrack_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var x = e.GetPosition(GateRatioPreviewTrack).X;
        var fillEdge = GetGateRatioPreviewFillEdge();
        if (Math.Abs(x - fillEdge) > 10)
        {
            return;
        }

        _isDraggingGateRatioPreview = true;
        GateRatioPreviewTrack.CaptureMouse();
        UpdateGateRatioFromPreviewPointer(e);
        e.Handled = true;
    }

    private void GateRatioPreviewTrack_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(GateRatioPreviewTrack).X;
        GateRatioPreviewTrack.Cursor = _isDraggingGateRatioPreview ||
                                       Math.Abs(x - GetGateRatioPreviewFillEdge()) <= 10
            ? Cursors.SizeWE
            : Cursors.Arrow;
        if (_isDraggingGateRatioPreview)
        {
            UpdateGateRatioFromPreviewPointer(e);
            e.Handled = true;
        }
    }

    private void GateRatioPreviewTrack_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDraggingGateRatioPreview)
        {
            return;
        }

        UpdateGateRatioFromPreviewPointer(e);
        _isDraggingGateRatioPreview = false;
        ApplySelectedGateRatio();
        GateRatioPreviewTrack.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void GateRatioPreviewTrack_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingGateRatioPreview)
        {
            GateRatioPreviewTrack.Cursor = Cursors.Arrow;
        }
    }

    private void GateRatioPreviewTrack_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        var shouldCommit = _isDraggingGateRatioPreview;
        _isDraggingGateRatioPreview = false;
        GateRatioPreviewTrack.Cursor = Cursors.Arrow;
        if (shouldCommit)
        {
            ApplySelectedGateRatio();
        }
    }

    private void UpdateGateRatioFromPreviewPointer(MouseEventArgs e)
    {
        var availableWidth = Math.Max(1, GateRatioPreviewTrack.ActualWidth - 2);
        var x = e.GetPosition(GateRatioPreviewTrack).X - 1;
        SetGateRatioPercentage(Math.Round(Math.Clamp(x / availableWidth * 100, 10, 95)));
    }

    private double GetGateRatioPreviewFillEdge() =>
        1 + Math.Max(0, GateRatioPreviewTrack.ActualWidth - 2) * _gateRatioPercentage / 100d;

    private void SetGateRatioPercentage(double percentage)
    {
        _gateRatioPercentage = Math.Clamp(percentage, 10, 95);
        UpdateGateRatioDescription(_gateRatioPercentage);
    }

    private void UpdateGateRatioPreview(double value)
    {
        if (GateRatioPreviewTrack is null || GateRatioPreviewFill is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, GateRatioPreviewTrack.ActualWidth - 2);
        GateRatioPreviewFill.Width = availableWidth * Math.Clamp(value / 100d, 0.1, 0.95);
    }

    private void GatePreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            double.TryParse(tag, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var percentage))
        {
            SetGateRatioPercentage(percentage);
            ApplySelectedGateRatio();
        }
    }

    private void RhythmLengthListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplySelectedRhythm();

    private void ApplySelectedRhythm()
    {
        if (_updatingNoteEditor || !NoteEditorPopup.IsOpen ||
            Score is null || RhythmLengthListBox.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var rhythmTick = GetRhythmTick(item, Score.Timing.Ppq);
        Surface.UpdateSelectedRhythm(rhythmTick);
    }

    private void ApplySelectedGateRatio()
    {
        if (_updatingNoteEditor || !NoteEditorPopup.IsOpen)
        {
            return;
        }

        Surface.UpdateSelectedGateRatio(_gateRatioPercentage / 100d);
    }

    private void NoteEditorPopup_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ShortcutKeyResolver.Resolve(e) == Key.Escape)
        {
            NoteEditorPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void SelectRhythmOption(long rhythmTick, int ppq) =>
        SelectRhythmOption(FindClosestRhythmIndex(rhythmTick, ppq));

    private void SelectRhythmOption(int index)
    {
        _updatingNoteEditor = true;
        RhythmLengthListBox.SelectedIndex = index;
        _updatingNoteEditor = false;
    }

    private static int FindClosestRhythmIndex(long rhythmTick, int ppq) =>
        Enumerable.Range(0, RhythmFactors.Length)
            .MinBy(index => Math.Abs(FactorToTick(RhythmFactors[index], ppq) - rhythmTick));

    private static long GetRhythmTick(FrameworkElement item, int ppq)
    {
        var factorText = item.Tag as string ?? "1";
        var factor = double.Parse(factorText, System.Globalization.CultureInfo.InvariantCulture);
        return FactorToTick(factor, ppq);
    }

    private static long FactorToTick(double factor, int ppq) =>
        Math.Max(1, checked((long)Math.Round(ppq * factor)));
}

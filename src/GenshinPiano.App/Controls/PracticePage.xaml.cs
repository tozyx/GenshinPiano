using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.App.Controls;

public partial class PracticePage : UserControl
{
    private sealed record Step(long Tick, TimeSpan Offset, IReadOnlyList<GenshinKey> Keys);
    private IReadOnlyList<Step> _steps = [];
    private readonly HashSet<GenshinKey> _matched = [];
    private MainWindowViewModel? _viewModel;
    private int _index, _combo, _hits, _attempts;
    private bool _running, _timed;
    private TimeSpan _timedPosition;
    private TimeSpan _timedOrigin;
    private DateTime _timedStarted;
    private double _playbackSpeed = 1;
    private int _practiceInstrument = AuditionInstrumentIds.WindsongLyre;
    private bool _renderClockAttached;
    private static readonly TimeSpan HitWindow = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan TimedPreRoll = TimeSpan.FromMilliseconds(1200);

    public PracticePage()
    {
        InitializeComponent();
        SetMode(PracticeSurfaceMode.VerticalRoll);
        Loaded += (_, _) => { Attach(); Surface.Focus(); };
        Unloaded += (_, _) => CancelTimer();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (ReferenceEquals(_viewModel, DataContext)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
        Reload();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentScore)) Reload();
    }

    private void Reload()
    {
        Reset(false);
        try
        {
            _steps = _viewModel is null ? [] : ScorePlaybackPlanner.Create(_viewModel.CurrentScore).Events
                .Where(x => x.KeysDown.Count > 0).Select(x => new Step(x.Tick, x.Offset, x.KeysDown)).ToArray();
            Status(_steps.Count == 0 ? "Practice_NoNotes" : "Practice_Ready");
        }
        catch (Exception ex) { _steps = []; PracticeStatusText.Text = ex.Message; }
        Refresh();
    }

    private void PracticePage_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || !ResolveKey(e, out var key)) return;
        Surface.SetKeyPressed(key, true);
        Accept(key);
        _ = PreviewAsync(key);
        e.Handled = true;
    }

    private void PracticePage_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!ResolveKey(e, out var key)) return;
        Surface.SetKeyPressed(key, false);
        e.Handled = true;
    }

    private static bool ResolveKey(KeyEventArgs e, out GenshinKey key)
    {
        var physical = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
        return Enum.TryParse(physical.ToString(), true, out key) && GenshinKeyMap.TryGetPitch(key, out _);
    }

    private async void Surface_OnPracticeKeyPressed(object? sender, GenshinKey key)
    {
        Accept(key);
        await PreviewAsync(key);
    }

    private async Task PreviewAsync(GenshinKey key)
    {
        if (System.Windows.Application.Current is not GenshinPiano.App.App app ||
            app.AuditionService is not { } auditionService ||
            _viewModel is not { } viewModel ||
            !GenshinKeyMap.TryGetPitch(key, out var pitch))
        {
            return;
        }

        try
        {
            await auditionService.PreviewNoteAsync(
                pitch,
                _practiceInstrument,
                duration: TimeSpan.FromMilliseconds(220));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Practice key preview failed: {exception.Message}");
        }
    }
    private void Accept(GenshinKey key)
    {
        if (!_running || _index >= _steps.Count) return;
        if (_timed && (_timedPosition - _steps[_index].Offset).Duration() > ScaledHitWindow)
        {
            _attempts++;
            _combo = 0;
            Surface.FlashWrongKey(key);
            Stats();
            return;
        }
        _attempts++;
        var target = _steps[_index].Keys;
        if (!target.Contains(key))
        {
            _combo = 0;
            Surface.FlashWrongKey(key);
        }
        else
        {
            if (_matched.Add(key)) _hits++;
            if (target.All(_matched.Contains)) { _combo++; Advance(); }
        }
        Stats();
    }

    private void Advance()
    {
        _matched.Clear();
        if (++_index >= _steps.Count)
        {
            _running = false;
            CancelTimer();
            Status("Practice_Complete");
        }
        Refresh(!_timed);
    }

    private void StartPracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        Surface.Focus();
        if (_steps.Count == 0) { Status("Practice_NoNotes"); return; }
        if (_index >= _steps.Count) Reset(false);
        _running = !_running;
        if (!_running) { CancelTimer(); Status("Practice_Paused"); }
        else if (_timed) { Status("Practice_TimedActive"); StartTimer(); }
        else Status("Practice_FollowActive");
        Refresh(false);
    }

    private void StopPracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        Reset(false);
        Status("Practice_Ready");
        Refresh(false);
        Surface.Focus();
    }

    private void FollowModeButton_OnClick(object sender, RoutedEventArgs e) => SetPracticeMode(false);
    private void TimedModeButton_OnClick(object sender, RoutedEventArgs e) => SetPracticeMode(true);

    private void SetPracticeMode(bool timed)
    {
        Reset(false);
        _timed = timed;
        FollowModeButton.IsChecked = !timed;
        TimedModeButton.IsChecked = timed;
        Status("Practice_Ready");
        Refresh(false);
        Surface.Focus();
    }

    private void StartTimer()
    {
        CancelTimer();
        _timedOrigin = _steps[_index].Offset;
        _timedStarted = DateTime.UtcNow;
        _renderClockAttached = true;
        CompositionTarget.Rendering += OnTimedRendering;
    }

    private void OnTimedRendering(object? sender, EventArgs e)
    {
        if (!_running || !_timed || _index >= _steps.Count) return;
        var elapsed = DateTime.UtcNow - _timedStarted;
        _timedPosition = _timedOrigin - TimedPreRoll +
                         TimeSpan.FromTicks((long)(elapsed.Ticks * _playbackSpeed));
        Surface.SetRollCursorTick(GetTickAt(_timedPosition), false);

        while (_running && _index < _steps.Count &&
               _timedPosition > _steps[_index].Offset + ScaledHitWindow)
        {
            _attempts += _steps[_index].Keys.Count(key => !_matched.Contains(key));
            _combo = 0;
            Advance();
            Stats();
        }
    }

    private TimeSpan ScaledHitWindow =>
        TimeSpan.FromTicks((long)(HitWindow.Ticks * _playbackSpeed));

    private double GetTickAt(TimeSpan offset)
    {
        if (_steps.Count == 0) return 0;
        var upper = 0;
        while (upper < _steps.Count && _steps[upper].Offset < offset) upper++;
        if (upper == 0)
        {
            var slope = _steps.Count > 1
                ? (_steps[1].Tick - _steps[0].Tick) / Math.Max(0.001, (_steps[1].Offset - _steps[0].Offset).TotalSeconds)
                : (_viewModel?.CurrentScore.Timing.Ppq ?? 480) * 2d;
            return _steps[0].Tick + (offset - _steps[0].Offset).TotalSeconds * slope;
        }
        if (upper >= _steps.Count) return _steps[^1].Tick;
        var left = _steps[upper - 1];
        var right = _steps[upper];
        var ratio = (offset - left.Offset).TotalMilliseconds /
                    Math.Max(1, (right.Offset - left.Offset).TotalMilliseconds);
        return left.Tick + (right.Tick - left.Tick) * ratio;
    }

    private void Reset(bool clearSteps)
    {
        CancelTimer();
        _running = false;
        _index = _combo = _hits = _attempts = 0;
        _matched.Clear();
        if (clearSteps) _steps = [];
    }

    private void CancelTimer()
    {
        if (_renderClockAttached) CompositionTarget.Rendering -= OnTimedRendering;
        _renderClockAttached = false;
    }

    private void PlaybackSpeedBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var wasRunning = _running && _timed;
        var currentPosition = _timedPosition;
        _playbackSpeed = PlaybackSpeedBox.SelectedIndex switch
        {
            0 => .25, 1 => .5, 3 => 1.25, _ => 1,
        };
        if (wasRunning)
        {
            _timedOrigin = currentPosition + TimedPreRoll;
            _timedStarted = DateTime.UtcNow;
        }
        Surface?.Focus();
    }

    private void NoteSpacingBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var spacing = NoteSpacingBox.SelectedIndex switch
        {
            1 => 1.25, 2 => 1.5, 3 => 2, _ => 1,
        };
        Surface?.SetRollSpacing(spacing);
        Surface?.Focus();
    }

    private void PracticeInstrumentBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PracticeInstrumentBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out var instrument) &&
            AuditionInstrumentIds.IsSampled(instrument))
        {
            _practiceInstrument = instrument;
        }
        Surface?.Focus();
    }

    private void Refresh(bool animateRoll = false)
    {
        Surface.SetPracticePosition(_steps.Select(x => x.Keys).ToArray(), _index);
        if (!_timed && _steps.Count > 0)
            Surface.SetRollCursorTick(_steps[Math.Min(_index, _steps.Count - 1)].Tick, animateRoll);
        StartPracticeButton.Content = FindResource(_running ? "Practice_Pause" : "Practice_Start");
        Stats();
    }

    private void Stats()
    {
        ComboText.Text = _combo.ToString();
        AccuracyText.Text = _attempts == 0 ? "—" : $"{_hits * 100d / _attempts:0}%";
    }

    private void Status(string key) => PracticeStatusText.Text = FindResource(key)?.ToString() ?? key;

    private void GameKeysModeButton_OnClick(object sender, RoutedEventArgs e) =>
        SetMode(PracticeSurfaceMode.GameKeys);

    private void VerticalRollModeButton_OnClick(object sender, RoutedEventArgs e) =>
        SetMode(PracticeSurfaceMode.VerticalRoll);

    private void SetMode(PracticeSurfaceMode mode)
    {
        Surface.Mode = mode;
        GameKeysModeButton.IsChecked = mode == PracticeSurfaceMode.GameKeys;
        VerticalRollModeButton.IsChecked = mode == PracticeSurfaceMode.VerticalRoll;
        Surface.Focus();
    }
}

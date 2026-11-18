using System.Reflection;
using System.Windows;
using System.Windows.Input;
using GenshinPiano.App.Controls;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class PianoRollGestureTests
{
    [Fact]
    public Task DoubleClickDeletesOnlyTheHitNote_AndCanBeUndone() => OnSta(() =>
    {
        var first = Note(0);
        var second = Note(960);
        var surface = Surface(first, second);
        surface.SelectAllNotes();
        var point = Center(surface, first);
        Assert.False(Left(surface, point, 1));
        Assert.Equal(2, Notes(surface).Count);
        Assert.True(Left(surface, point, 2));
        Assert.Equal(second.Id, Assert.Single(Notes(surface)).Id);
        Assert.Equal(1, surface.SelectedNoteCount);
        surface.UndoEdit();
        Assert.Equal([first.Id, second.Id], Notes(surface).Select(note => note.Id));
    });

    [Fact]
    public Task EmptySpaceDifferentNotesAndModifierClicks_DoNotDelete() => OnSta(() =>
    {
        var first = Note(0);
        var second = Note(960);
        var surface = Surface(first, second);
        surface.SelectAllNotes();
        Assert.False(Left(surface, new Point(1000, 100), 1));
        Assert.False(Left(surface, Center(surface, first), 2));
        Assert.False(Left(surface, Center(surface, first), 1));
        Assert.False(Left(surface, Center(surface, second), 2));
        foreach (var modifier in new[] { ModifierKeys.Control, ModifierKeys.Shift, ModifierKeys.Alt })
        {
            Left(surface, Center(surface, first), 1);
            Assert.False(Left(surface, Center(surface, first), 2, modifier));
        }
        surface.IsEditingEnabled = false;
        Assert.False(Left(surface, Center(surface, first), 1));
        Assert.False(Left(surface, Center(surface, first), 2));
        Assert.Equal(2, Notes(surface).Count);
    });

    [Fact]
    public Task DoubleRightClickRequiresTwoEmptyPresses_AndPreservesTheScore() => OnSta(() =>
    {
        var note = Note(0);
        var surface = Surface(note);
        surface.SelectAllNotes();
        var requests = 0;
        surface.NewNoteLengthRestoreRequested += (_, _) => requests++;
        var empty = new Point(1000, 100);
        var original = surface.Score;
        Right(surface, empty, 1);
        Assert.Equal(0, surface.SelectedNoteCount);
        Assert.Equal(0, requests);
        Right(surface, empty, 2);
        Assert.Equal(1, requests);
        Assert.Same(original, surface.Score);
        Right(surface, new Point(100, 5), 1);
        Right(surface, new Point(100, 5), 2);
        Right(surface, Center(surface, note), 1);
        Right(surface, empty, 2);
        Assert.Equal(1, requests);
        surface.IsEditingEnabled = false;
        Right(surface, empty, 1);
        Right(surface, empty, 2);
        Assert.Equal(1, requests);
    });

    private static NoteEvent Note(long start) => new() { Pitch = 60, StartTick = start, RhythmTick = 480, DurationTick = 384 };
    private static PianoRollSurface Surface(params NoteEvent[] notes) => new()
    {
        Score = ScoreDocument.CreateEmpty("Gestures") with { Tracks = [new() { Notes = notes.ToList() }] },
    };
    private static IReadOnlyList<NoteEvent> Notes(PianoRollSurface surface) => surface.Score!.Tracks.Single().Notes;
    private static Point Center(PianoRollSurface surface, NoteEvent note)
    {
        object?[] args = [note, surface.Score!.Timing.Ppq, Rect.Empty];
        Assert.True((bool)Invoke(surface, "TryGetNoteBounds", args)!);
        var bounds = (Rect)args[2]!;
        return new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
    private static bool Left(PianoRollSurface surface, Point point, int count, ModifierKeys modifiers = ModifierKeys.None) =>
        (bool)Invoke(surface, "TryDeleteDoubleClickedNote", point, count, modifiers)!;
    private static void Right(PianoRollSurface surface, Point point, int count)
    {
        Invoke(surface, "HandleRightButtonDown", point, count, ModifierKeys.None);
        Invoke(surface, "HandleRightButtonUp", point);
    }
    private static object? Invoke(PianoRollSurface surface, string name, params object?[] args) =>
        typeof(PianoRollSurface).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, args);

    // No real input, visible windows or keyboard focus changes.
    private static Task OnSta(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { test(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

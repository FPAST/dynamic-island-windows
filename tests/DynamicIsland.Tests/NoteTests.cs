using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class StickyNoteTests
{
    private static readonly DateTime T = new(2026, 11, 19, 10, 0, 0);

    [Fact]
    public void ShowsThePickedNoteElseTheNewest()
    {
        var a = new Note { Text = "a", Created = T }; var b = new Note { Text = "b", Created = T.AddDays(-1) };
        var notes = new List<Note> { a, b };
        Assert.Same(b, StickyNote.Current(notes, b.Created));
        Assert.Same(a, StickyNote.Current(notes, null));
        Assert.Same(a, StickyNote.Current(notes, T.AddYears(-5)));   // picked one was deleted
        Assert.Null(StickyNote.Current([], null));
    }

    [Fact]
    public void TypingIntoNothingMakesANewNoteOnTop()
    {
        var old = new Note { Text = "old", Created = T.AddDays(-1) };
        var notes = new List<Note> { old };
        var made = StickyNote.Commit(notes, null, "Купить хлеб", T);
        Assert.NotNull(made);
        Assert.Same(made, notes[0]);
        Assert.Equal("Купить хлеб", made!.Text);
        Assert.Equal(T, made.Created);
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public void TypingChangesTheCurrentNote()
    {
        var n = new Note { Text = "a", Created = T };
        var notes = new List<Note> { n };
        Assert.Same(n, StickyNote.Commit(notes, n, "a\nb", T.AddMinutes(1)));
        Assert.Equal("a\nb", n.Text);
        Assert.Equal(T, n.Created);   // the date it was made stays
        Assert.Single(notes);
    }

    [Fact]
    public void EmptyingANoteRemovesIt_AndBlankMakesNothing()
    {
        var n = new Note { Text = "a", Created = T };
        var notes = new List<Note> { n };
        Assert.Null(StickyNote.Commit(notes, n, "   \n ", T));
        Assert.Empty(notes);
        Assert.Null(StickyNote.Commit(notes, null, "", T));
        Assert.Empty(notes);
    }

    [Fact]
    public void ANoteDeletedElsewhereIsWrittenAsANewOne()
    {
        var gone = new Note { Text = "x", Created = T.AddDays(-2) };
        var notes = new List<Note>();
        var made = StickyNote.Commit(notes, gone, "still typing", T);
        Assert.NotSame(gone, made);
        Assert.Single(notes);
    }

    [Theory]
    [InlineData("Купить хлеб\nи молоко", "Купить хлеб")]
    [InlineData("\n\n  Вторая строка  ", "Вторая строка")]
    [InlineData("", "")]
    [InlineData("1234567890123456789012345", "1234567890123456789…")]
    public void TitleIsTheFirstLine(string text, string title) => Assert.Equal(title, StickyNote.Title(text, 20));
}

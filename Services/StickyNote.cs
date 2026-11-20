namespace DynamicIsland.Services;

/// <summary>
/// The note on the note widget: which one it shows and how typing changes the list. Pure (the list is given), so
/// the widget only decides when to save.
/// </summary>
public static class StickyNote
{
    /// <summary>The note picked earlier (by when it was made), else the newest, else none.</summary>
    public static Note? Current(IReadOnlyList<Note> notes, DateTime? picked) =>
        picked is { } p && notes.FirstOrDefault(n => n.Created == p) is { } found ? found : notes.FirstOrDefault();

    /// <summary>
    /// What was typed goes into the list: into the current note; a first word with no note yet makes a new one at
    /// the top; a note emptied completely is removed. Returns the note now shown (null when none).
    /// </summary>
    public static Note? Commit(List<Note> notes, Note? current, string text, DateTime now)
    {
        bool empty = string.IsNullOrWhiteSpace(text);
        if (current == null || !notes.Contains(current))
        {
            if (empty) return null;
            var made = new Note { Text = text, Created = now };
            notes.Insert(0, made);
            return made;
        }
        if (empty)
        {
            notes.Remove(current);
            return null;
        }
        current.Text = text;
        return current;
    }

    /// <summary>The first line, for lists ("Купить хлеб"), at most <paramref name="max"/> characters.</summary>
    public static string Title(string text, int max = 60)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }
}

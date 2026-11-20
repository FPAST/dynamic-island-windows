namespace DynamicIsland.Services;

/// <summary>
/// A user's list of programs ("hide the island in front of", "FPS only in"). An entry is a process name ("Zoom"),
/// the full path of a program, or a folder: everything started from inside it (a game in no list, a portable program).
/// </summary>
public static class AppList
{
    public static bool IsPathEntry(string entry) => entry.Contains('\\');

    /// <summary>Does the process with this name (no .exe) and full path ("" when unknown) match the entry?</summary>
    public static bool Matches(string entry, string name, string path) => !IsPathEntry(entry)
        ? entry.Equals(name, StringComparison.OrdinalIgnoreCase)
        : path.Length > 0 && (path.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                              path.StartsWith(entry.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    public static bool Contains(IEnumerable<string> list, string name, string path) => list.Any(e => Matches(e, name, path));
}

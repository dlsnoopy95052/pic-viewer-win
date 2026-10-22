using System.Runtime.InteropServices;

namespace PictureViewer;

/// <summary>The Pictures in one folder, in Explorer (natural) order, with a current position.</summary>
public sealed class FolderBrowser
{
    public IReadOnlyList<string> Files { get; private set; } = [];
    public int Index { get; private set; } = -1;

    public string? Current => Index >= 0 && Index < Files.Count ? Files[Index] : null;
    public bool HasPrevious => Index > 0;
    public bool HasNext => Index >= 0 && Index < Files.Count - 1;

    /// <summary>Loads the folder containing <paramref name="file"/> and positions on it.</summary>
    public void OpenFile(string file)
    {
        file = Path.GetFullPath(file);
        var dir = Path.GetDirectoryName(file)!;
        var files = List(dir);
        int i = files.FindIndex(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
        {
            // Explicitly opened file with an unlisted extension: still show it.
            files.Insert(0, file);
            i = 0;
        }
        Files = files;
        Index = i;
    }

    /// <summary>Loads <paramref name="dir"/> and positions on its first Picture. Returns false if it has none.</summary>
    public bool OpenFolder(string dir)
    {
        var files = List(Path.GetFullPath(dir));
        if (files.Count == 0) return false;
        Files = files;
        Index = 0;
        return true;
    }

    public bool Move(int delta)
    {
        int i = Math.Clamp(Index + delta, 0, Files.Count - 1);
        if (Files.Count == 0 || i == Index) return false;
        Index = i;
        return true;
    }

    /// <summary>Moves to the next Picture, wrapping to the first after the last (used by the slideshow).</summary>
    public bool Advance()
    {
        if (Files.Count < 2) return false;
        Index = (Index + 1) % Files.Count;
        return true;
    }

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= Files.Count || index == Index) return false;
        Index = index;
        return true;
    }

    /// <summary>Re-reads the folder, e.g. after the current file was renamed or deleted.</summary>
    public void Refresh()
    {
        if (Current is not { } cur) return;
        var dir = Path.GetDirectoryName(cur)!;
        var files = Directory.Exists(dir) ? List(dir) : [];
        int i = files.FindIndex(f => string.Equals(f, cur, StringComparison.OrdinalIgnoreCase));
        Files = files;
        Index = files.Count == 0 ? -1 : (i >= 0 ? i : Math.Min(Index, files.Count - 1));
    }

    static List<string> List(string dir)
    {
        var files = new List<string>();
        try
        {
            files.AddRange(Directory.EnumerateFiles(dir).Where(ImageFormats.IsSupported));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Unreadable folder: behave as empty.
        }
        files.Sort(StrCmpLogicalW);
        return files;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string x, string y);
}

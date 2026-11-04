using System.IO;
using System.Text.RegularExpressions;
using OnlyFate.Database;
namespace OnlyFate.Desktop;

public static class ExportDestinationPolicy
{
    // Both modes protect the same complete internal tree, regardless of the active workspace.
    // The attribute reader is a pure-test seam; production always uses File.GetAttributes.
    public static string Validate(string destination, Func<string, FileAttributes?>? readAttributes = null)
    {
        const string refused = "請另存至 OnlyFate 內部資料目錄以外，且未使用連結的本機資料夾。";
        string path;
        try
        {
            if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination)
                || destination.StartsWith(@"\\", StringComparison.Ordinal)
                || destination.StartsWith("//", StringComparison.Ordinal)
                || destination.IndexOf(':', 2) >= 0)
                throw new InvalidOperationException(refused);
            // Win32 strips trailing dots/spaces and supports short-name aliases. Reject these
            // ambiguous spellings rather than allowing lexical checks to miss an internal root.
            foreach (string segment in destination.Replace('/', '\\').Split('\\'))
                if (segment is not ("." or "..") && (segment.EndsWith(' ') || segment.EndsWith('.')
                    || Regex.IsMatch(segment, @"~[0-9]")))
                    throw new InvalidOperationException(refused);
            path = Path.GetFullPath(destination);
            string normalRoot = Path.GetDirectoryName(Path.GetDirectoryName(DatabaseSchema.DefaultDatabasePath))!;
            foreach (string root in new[] { normalRoot, DemoWorkspace.DefaultRoot })
            {
                string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if (path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(refused);
            }
            readAttributes ??= ReadAttributes;
            // Include the existing destination itself: an individual file can also be a link.
            for (string? ancestor = path; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
                if (readAttributes(ancestor) is FileAttributes attributes && (attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(refused);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            throw new InvalidOperationException("無法安全確認另存位置；請選擇可存取且未使用連結的本機資料夾。", error);
        }
        return path;
    }

    private static FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

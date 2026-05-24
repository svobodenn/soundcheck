using System.IO;

namespace SoundCheck.Services;

/// <summary>
/// Folder scanning + tag-based batch renaming for the File Manager view.
/// Pure logic — no UI. All disk operations are best-effort and never throw.
/// </summary>
public static class FileManager
{
    public static readonly string[] AudioExt = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };

    /// <summary>Rename templates offered in the format dropdown.</summary>
    public static readonly string[] Formats =
    {
        "Title - Artist",
        "Artist - Title",
        "Title",
        "Album - Title",
        "Artist - Album - Title",
    };

    public class AudioItem
    {
        public string FullPath = "";
        public string FileName = "";   // includes extension
        public string Folder = "";     // parent directory full path
        public string FolderName = ""; // parent directory display name
        public string Ext = "";
        public long SizeBytes;
        public string Title = "";
        public string Artist = "";
        public string Album = "";
    }

    /// <summary>Recursively scan <paramref name="root"/> for audio files and read basic tags.</summary>
    public static List<AudioItem> Scan(string root)
    {
        var result = new List<AudioItem>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;

        // IgnoreInaccessible = true → skip protected/locked subfolders instead of
        // aborting the whole walk (the previous behavior hid every folder after
        // the first inaccessible one). ReturnSpecialDirectories stays false.
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", opts); }
        catch { return result; }

        string rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(rootName)) rootName = root;

        var iter = files.GetEnumerator();
        while (true)
        {
            string f;
            // Guard MoveNext too: a transient IO error on one entry shouldn't kill the whole scan.
            try { if (!iter.MoveNext()) break; f = iter.Current; }
            catch { break; }

            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (Array.IndexOf(AudioExt, ext) < 0) continue;

            var item = new AudioItem
            {
                FullPath = f,
                FileName = Path.GetFileName(f),
                Folder = Path.GetDirectoryName(f) ?? root,
                Ext = ext,
            };
            item.FolderName = string.Equals(item.Folder, root, StringComparison.OrdinalIgnoreCase)
                ? rootName
                : Path.GetFileName(item.Folder);

            try { item.SizeBytes = new FileInfo(f).Length; } catch { }

            try
            {
                using var tag = TagLib.File.Create(f);
                item.Title  = string.IsNullOrWhiteSpace(tag.Tag.Title) ? Path.GetFileNameWithoutExtension(f) : tag.Tag.Title;
                item.Artist = tag.Tag.FirstPerformer ?? "";
                item.Album  = tag.Tag.Album ?? "";
            }
            catch
            {
                item.Title = Path.GetFileNameWithoutExtension(f);
            }
            result.Add(item);
        }
        return result;
    }

    /// <summary>Build the target file name (with extension) for an item under a format.</summary>
    public static string BuildName(AudioItem f, string format)
    {
        string title  = string.IsNullOrWhiteSpace(f.Title) ? Path.GetFileNameWithoutExtension(f.FileName) : f.Title.Trim();
        string artist = (f.Artist ?? "").Trim();
        string album  = (f.Album ?? "").Trim();

        string Join(params string[] parts) =>
            string.Join(" - ", parts.Where(p => !string.IsNullOrEmpty(p)));

        string baseName = format switch
        {
            "Title - Artist"          => Join(title, artist),
            "Artist - Title"          => Join(artist, title),
            "Title"                   => title,
            "Album - Title"           => Join(album, title),
            "Artist - Album - Title"  => Join(artist, album, title),
            _                          => title,
        };
        if (string.IsNullOrWhiteSpace(baseName)) baseName = title;
        return Sanitize(baseName) + f.Ext;
    }

    /// <summary>Strip characters illegal in Windows file names.</summary>
    public static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        s = s.Trim().TrimEnd('.');               // trailing dots are invalid on Windows
        return string.IsNullOrEmpty(s) ? "untitled" : s;
    }

    /// <summary>
    /// Rename a file to <paramref name="newFileName"/> in the same directory.
    /// Returns the new full path, the original path if it was already correct,
    /// or null on failure. Auto-resolves collisions with " (n)" suffixes.
    /// </summary>
    public static string? Rename(string fullPath, string newFileName)
    {
        try
        {
            string dir = Path.GetDirectoryName(fullPath)!;
            string target = Path.Combine(dir, newFileName);

            if (string.Equals(target, fullPath, StringComparison.OrdinalIgnoreCase))
                return fullPath; // no change needed

            if (File.Exists(target))
            {
                string stem = Path.GetFileNameWithoutExtension(newFileName);
                string ext  = Path.GetExtension(newFileName);
                int i = 2;
                do { target = Path.Combine(dir, $"{stem} ({i}){ext}"); i++; }
                while (File.Exists(target));
            }

            File.Move(fullPath, target);
            return target;
        }
        catch { return null; }
    }

    /// <summary>
    /// Write title/artist/album tags into the audio file. Returns false on
    /// failure (e.g. the file is locked because it's currently playing, or the
    /// format doesn't support tagging).
    /// </summary>
    /// <summary>Read title/artist/album + front-cover bytes from a file. Best-effort.</summary>
    public static (string title, string artist, string album, byte[]? cover) ReadTags(string fullPath)
    {
        string title = Path.GetFileNameWithoutExtension(fullPath), artist = "", album = "";
        byte[]? cover = null;
        try
        {
            using var tag = TagLib.File.Create(fullPath);
            if (!string.IsNullOrWhiteSpace(tag.Tag.Title)) title = tag.Tag.Title;
            artist = tag.Tag.FirstPerformer ?? "";
            album = tag.Tag.Album ?? "";
            if (tag.Tag.Pictures is { Length: > 0 } pics && pics[0].Data?.Data is { Length: > 0 } data)
                cover = data;
        }
        catch { }
        return (title, artist, album, cover);
    }

    public static bool WriteTags(string fullPath, string title, string artist, string album,
                                 bool changeCover = false, byte[]? coverBytes = null)
    {
        try
        {
            using var tag = TagLib.File.Create(fullPath);
            tag.Tag.Title = title;
            tag.Tag.Performers = string.IsNullOrWhiteSpace(artist) ? Array.Empty<string>() : new[] { artist };
            tag.Tag.Album = string.IsNullOrWhiteSpace(album) ? null : album;
            if (changeCover)
            {
                if (coverBytes != null && coverBytes.Length > 0)
                {
                    var pic = new TagLib.Picture(new TagLib.ByteVector(coverBytes))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = "image/jpeg",
                        Description = "Cover",
                    };
                    tag.Tag.Pictures = new TagLib.IPicture[] { pic };
                }
                else
                {
                    tag.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                }
            }
            tag.Save();
            return true;
        }
        catch { return false; }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        double kb = bytes / 1024.0;
        return kb < 1024 ? $"{kb:F1} KB" : $"{kb / 1024:F1} MB";
    }
}

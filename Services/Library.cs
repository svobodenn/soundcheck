using System.IO;
using System.Windows.Media.Imaging;
using SoundCheck.Models;

namespace SoundCheck.Services;

public static class Library
{
    /// <summary>Reads tags + cover from audio file.</summary>
    public static Track? LoadTrack(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var tag = f.Tag;
            var props = f.Properties;

            byte[]? coverBytes = null;
            BitmapImage? cover = null;
            if (tag.Pictures.Length > 0)
            {
                coverBytes = tag.Pictures[0].Data.Data;
                cover = LoadThumb(coverBytes, 80);
            }

            return new Track
            {
                Path = path,
                Title = !string.IsNullOrWhiteSpace(tag.Title)
                    ? tag.Title
                    : Path.GetFileNameWithoutExtension(path),
                Artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer)
                    ? tag.FirstPerformer
                    : "Unknown Artist",
                Album = tag.Album ?? "",
                Duration = props?.Duration ?? TimeSpan.Zero,
                CoverBytes = coverBytes,
                HasCover = coverBytes != null,
                Cover = cover,
                IsExplicit = LooksExplicit(tag.Title) || LooksExplicit(tag.Album),
            };
        }
        catch
        {
            // Fallback: file without tags
            try
            {
                return new Track
                {
                    Path = path,
                    Title = Path.GetFileNameWithoutExtension(path),
                    Artist = "Unknown Artist",
                    Duration = TimeSpan.Zero,
                };
            }
            catch { return null; }
        }
    }

    /// <summary>Heuristic: a title/album mentioning "explicit" marks the track explicit.
    /// (Users can also toggle it manually in the tag editor.)</summary>
    private static bool LooksExplicit(string? s)
        => !string.IsNullOrEmpty(s) && s.IndexOf("explicit", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Decode cover bytes to a small thumbnail BitmapImage.</summary>
    public static BitmapImage? LoadThumb(byte[]? bytes, int maxSize)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = maxSize;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    public static BitmapImage? LoadFullCover(byte[]? bytes, int maxSize = 600)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = maxSize;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
}

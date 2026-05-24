using System.IO;

namespace SoundCheck.Services;

/// <summary>
/// Lightweight app-wide logger. Writes timestamped lines to %LocalAppData%\SoundCheck\app.log
/// and keeps the most recent lines in memory for the in-app Logs viewer.
/// Thread-safe; <see cref="Changed"/> may fire on any thread (subscribers must marshal to UI).
/// </summary>
public static class Log
{
    public const int MaxBuffer = 800;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoundCheck", "app.log");

    private static readonly object _lock = new();
    private static readonly List<string> _buffer = new();

    /// <summary>Fires whenever a line is appended or the log is cleared.</summary>
    public static event Action? Changed;

    public static List<string> Recent()
    {
        lock (_lock) return new List<string>(_buffer);
    }

    public static void Info(string msg)  => Write("INFO", msg);
    public static void Warn(string msg)  => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null)
        => Write("ERROR", ex == null ? msg : $"{msg} — {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string msg)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
        lock (_lock)
        {
            _buffer.Add(line);
            if (_buffer.Count > MaxBuffer) _buffer.RemoveRange(0, _buffer.Count - MaxBuffer);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
        try { Changed?.Invoke(); } catch { }
    }

    public static void Clear()
    {
        lock (_lock) _buffer.Clear();
        try { File.WriteAllText(LogPath, ""); } catch { }
        try { Changed?.Invoke(); } catch { }
    }
}

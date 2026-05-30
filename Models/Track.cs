using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace SoundCheck.Models;

public class Track : INotifyPropertyChanged
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public BitmapImage? Cover { get; set; }
    public byte[]? CoverBytes { get; set; }

    /// <summary>True when this track has cover art stored in the library.
    /// The full bytes are loaded on demand from SQLite rather than pinned in RAM.</summary>
    public bool HasCover { get; set; }

    public string DurationStr =>
        Duration.TotalSeconds <= 0 ? "0:00"
            : Duration.TotalHours >= 1
                ? $"{(int)Duration.TotalHours}:{Duration.Minutes:00}:{Duration.Seconds:00}"
                : $"{Duration.Minutes}:{Duration.Seconds:00}";

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCurrentPlaying)); }
    }

    private bool _isCurrentPlaying;
    public bool IsCurrentPlaying
    {
        get => _isCurrent && _isCurrentPlaying;
        set { _isCurrentPlaying = value; OnPropertyChanged(); }
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    private int _index;
    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    private bool _isInQueue;
    public bool IsInQueue
    {
        get => _isInQueue;
        set { _isInQueue = value; OnPropertyChanged(); }
    }

    /// <summary>True when the backing audio file no longer exists on disk.</summary>
    private bool _isMissing;
    public bool IsMissing
    {
        get => _isMissing;
        set { _isMissing = value; OnPropertyChanged(); }
    }

    /// <summary>True → show the "E" explicit badge.</summary>
    private bool _isExplicit;
    public bool IsExplicit
    {
        get => _isExplicit;
        set { _isExplicit = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

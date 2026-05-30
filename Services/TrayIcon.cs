using System.IO;
using System.Windows;
using SoundCheck.Views;

// Aliases — TrayIcon is the one place that talks to WinForms; keep the rest of
// the codebase blissfully unaware of System.Drawing / System.Windows.Forms.
using NotifyIcon    = System.Windows.Forms.NotifyIcon;
using MouseButtons  = System.Windows.Forms.MouseButtons;
using Control       = System.Windows.Forms.Control;
using Icon          = System.Drawing.Icon;
using SystemIcons   = System.Drawing.SystemIcons;

namespace SoundCheck.Services;

/// <summary>
/// System tray icon manager. Shows an icon in the Windows notification area
/// (next to the clock) with our custom WPF popup menu on right-click.
/// Double-click restores the main window.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private TrayMenu? _menu;
    private readonly Window _ownerWindow;

    public event Action? ShowApp;
    public event Action? PlayPause;
    public event Action? Prev;
    public event Action? Next;
    public event Action? Quit;
    public event Action? ShuffleToggle;
    public event Action? RepeatToggle;
    public event Action<double>? VolumeChanged;

    private double _lastVolume = 0.8;
    private bool _lastShuffle, _lastRepeat;
    /// <summary>Keep the cached volume in sync so the popup opens with the right level.</summary>
    public void UpdateVolume(double v) { _lastVolume = v; _menu?.SetVolume(v); }

    /// <summary>Keep shuffle/repeat state in sync so the popup reflects + can toggle them.</summary>
    public void UpdateModes(bool shuffle, bool repeat)
    {
        _lastShuffle = shuffle; _lastRepeat = repeat;
        _menu?.SetShuffle(shuffle); _menu?.SetRepeat(repeat);
    }

    public TrayIcon(Window owner)
    {
        _ownerWindow = owner;
        _icon = new NotifyIcon
        {
            Icon  = LoadAppIcon(),
            Text  = "soundcheck",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowApp?.Invoke();
        // Show our themed popup on LEFT-click too (Windows convention for media
        // tools — easier than always right-clicking).
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
                ShowMenu();
        };
    }

    /// <summary>Push current track info + play-state into the (existing or future) popup.</summary>
    public void UpdateTrack(string? title, string? artist, System.Windows.Media.Imaging.BitmapImage? cover, bool playing)
    {
        // We always re-set when the menu opens (see ShowMenu), so cache here.
        _lastTitle = title; _lastArtist = artist; _lastCover = cover; _lastPlaying = playing;
        _menu?.SetTrack(title, artist, cover);
        _menu?.SetPlaying(playing);
        if (!string.IsNullOrWhiteSpace(title))
            _icon.Text = Truncate($"{artist} — {title}", 63);
        else
            _icon.Text = "soundcheck";
    }

    private string? _lastTitle, _lastArtist;
    private System.Windows.Media.Imaging.BitmapImage? _lastCover;
    private bool _lastPlaying;

    private void ShowMenu()
    {
        // Close any prior instance — Windows can fire MouseClick twice when the
        // user clicks the icon while a menu is already up.
        try { _menu?.Close(); } catch { }
        _menu = new TrayMenu { Owner = null };
        _menu.SetTrack(_lastTitle, _lastArtist, _lastCover);
        _menu.SetPlaying(_lastPlaying);
        _menu.SetVolume(_lastVolume);
        _menu.SetShuffle(_lastShuffle);
        _menu.SetRepeat(_lastRepeat);
        _menu.PlayPause += () => PlayPause?.Invoke();
        _menu.Prev      += () => Prev?.Invoke();
        _menu.Next      += () => Next?.Invoke();
        _menu.ShowApp   += () => ShowApp?.Invoke();
        _menu.Quit      += () => Quit?.Invoke();
        _menu.ShuffleToggle += () => ShuffleToggle?.Invoke();
        _menu.RepeatToggle  += () => RepeatToggle?.Invoke();
        _menu.VolumeChanged += v => VolumeChanged?.Invoke(v);

        var cursor = Control.MousePosition;
        _menu.ShowAt(cursor.X, cursor.Y);
    }

    /// <summary>Load icon from the .ico file bundled as an embedded Assets resource.</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/soundcheck.ico", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream != null) return new Icon(stream);
        }
        catch { }
        // Last-ditch fallback — bundled .ico next to exe.
        // AppContext.BaseDirectory is single-file-safe (Assembly.Location is empty there).
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var path = Path.Combine(exeDir, "Assets", "soundcheck.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { }
        return SystemIcons.Application;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        try { _menu?.Close(); } catch { }
        _icon.Visible = false;
        _icon.Dispose();
    }
}

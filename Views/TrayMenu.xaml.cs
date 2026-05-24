using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace SoundCheck.Views;

/// <summary>
/// Tray-icon context popup in our dark/gold theme. Shown above-or-below the
/// system-tray cursor position on right-click. Auto-closes on focus loss.
/// </summary>
public partial class TrayMenu : Window
{
    public event Action? PlayPause;
    public event Action? Prev;
    public event Action? Next;
    public event Action? ShowApp;
    public event Action? Quit;
    public event Action<double>? VolumeChanged;

    public TrayMenu() { InitializeComponent(); }

    private bool _volSuppress;
    /// <summary>Set the volume slider without re-raising <see cref="VolumeChanged"/>.</summary>
    public void SetVolume(double v)
    {
        _volSuppress = true;
        SldVol.Value = Math.Clamp(v, 0, 1);
        _volSuppress = false;
    }

    private void SldVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volSuppress) return;
        VolumeChanged?.Invoke(e.NewValue);
    }

    /// <summary>Update the header track info (cover + title + artist).</summary>
    public void SetTrack(string? title, string? artist, BitmapImage? cover)
    {
        TxtTitle.Text  = string.IsNullOrWhiteSpace(title)  ? "soundcheck" : title;
        TxtArtist.Text = string.IsNullOrWhiteSpace(artist) ? "ничего не играет" : artist;
        ImgCover.Source = cover;
    }

    /// <summary>Swap the play/pause glyph in the center transport button.</summary>
    public void SetPlaying(bool playing)
    {
        var key = playing ? "PauseGeo" : "PlayGeo";
        if (Application.Current.Resources[key] is System.Windows.Media.Geometry g
            && BtnPlay.Template.FindName("PathPlay", BtnPlay) is Path p)
            p.Data = g;
    }

    /// <summary>Position window near cursor, smart about screen edges + taskbar.</summary>
    public void ShowAt(int cursorX, int cursorY)
    {
        // Pre-position off-screen and show to force real layout — Measure alone
        // doesn't always give correct DesiredSize for transparent windows.
        Left = -10000; Top = -10000;
        Show();
        UpdateLayout();

        // Pick screen the cursor is on (multi-monitor support) and use its
        // WorkingArea, which already excludes the taskbar.
        var cursor = new System.Drawing.Point(cursorX, cursorY);
        var screen = Screen.FromPoint(cursor) ?? Screen.PrimaryScreen!;
        var area = screen.WorkingArea;

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double w = ActualWidth;
        double h = ActualHeight;
        double cx = cursorX / dpi;
        double cy = cursorY / dpi;
        double areaL = area.Left / dpi,  areaT = area.Top / dpi;
        double areaR = area.Right / dpi, areaB = area.Bottom / dpi;
        const double Pad = 6;

        // Center horizontally on cursor, then clamp into work area.
        double left = cx - w / 2;
        if (left + w > areaR - Pad) left = areaR - w - Pad;
        if (left < areaL + Pad)     left = areaL + Pad;

        // Prefer above the cursor (tray is bottom edge → menu floats up).
        // If there's not enough room above, drop below instead.
        double top = cy - h - 12;
        if (top < areaT + Pad) top = cy + 12;
        // Final clamp — if even that overflows, push fully inside.
        if (top + h > areaB - Pad) top = areaB - h - Pad;
        if (top < areaT + Pad)     top = areaT + Pad;

        Left = left;
        Top  = top;
        Activate();
        Focus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Pop-in: opacity 0→1, scale 0.94→1
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sc = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        MenuScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        MenuScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
    }

    // ─── Auto-close on mouse-leave (with a tiny grace period) ──────────────
    private DispatcherTimer? _closeTimer;
    private const int CloseDelayMs = 250;

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        // Cancel any pending close — user came back into the window.
        _closeTimer?.Stop();
        _closeTimer = null;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        // Small delay tolerates a quick drag-across-edge wobble.
        _closeTimer?.Stop();
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CloseDelayMs) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer?.Stop();
            _closeTimer = null;
            // Defensive: if mouse came back exactly on the tick boundary, abort.
            if (IsMouseOver) return;
            CloseFade();
        };
        _closeTimer.Start();
    }

    private void CloseFade()
    {
        _closeTimer?.Stop();
        _closeTimer = null;
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(110) };
        fade.Completed += (_, _) => { try { Close(); } catch { } };
        BeginAnimation(OpacityProperty, fade);
    }

    // Transport controls — keep menu OPEN so user can hit prev/next/play in
    // quick succession without re-opening the popup each time.
    private void BtnPlay_Click(object sender, RoutedEventArgs e) => PlayPause?.Invoke();
    private void BtnPrev_Click(object sender, RoutedEventArgs e) => Prev?.Invoke();
    private void BtnNext_Click(object sender, RoutedEventArgs e) => Next?.Invoke();

    // Navigational actions — naturally close the menu.
    private void BtnShow_Click(object sender, RoutedEventArgs e) { ShowApp?.Invoke(); CloseFade(); }
    private void BtnQuit_Click(object sender, RoutedEventArgs e) { Quit?.Invoke();    CloseFade(); }
}

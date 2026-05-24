using System.Windows;
using System.Windows.Media.Animation;
using SoundCheck.Services;

namespace SoundCheck;

// Tray icon + minimize/close-to-tray behavior. Split out of MainWindow.xaml.cs
// to keep the main file smaller (same partial class — fields are shared).
public partial class MainWindow
{
    private void InitTray()
    {
        if (_tray != null) return;
        _tray = new TrayIcon(this);
        _tray.ShowApp   += () => Dispatcher.Invoke(ShowFromTray);
        _tray.PlayPause += () => Dispatcher.Invoke(() => BtnPlay_Click(this, new RoutedEventArgs()));
        _tray.Prev      += () => Dispatcher.Invoke(() => BtnPrev_Click(this, new RoutedEventArgs()));
        _tray.Next      += () => Dispatcher.Invoke(() => BtnNext_Click(this, new RoutedEventArgs()));
        _tray.Quit      += () => Dispatcher.Invoke(QuitApplication);
        _tray.VolumeChanged += v => Dispatcher.Invoke(() => SldVolume.Value = v);
        _tray.UpdateVolume(_audio.Volume);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Persist last-played track + position so RememberPosition can restore it.
        try
        {
            if (_current != null)
            {
                AppSettings.LastTrackPath = _current.Path;
                AppSettings.LastTrackPosition = _audio.Position.TotalSeconds;
            }
        }
        catch { }

        // Window-close intercepted: hide to tray instead of quitting — unless
        // the user disabled "close to tray" or already chose "Выйти" from the tray menu.
        if (_reallyQuit || !AppSettings.CloseToTray) { _tray?.Dispose(); return; }
        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        // Fade-out (we may already be animating from BtnClose_Click), then hide.
        // BeginAnimation set up at the call site handles the fade; here we just hide.
        Hide();
        ShowInTaskbar = false;
        PauseAmbient(); // nothing is visible in the tray — stop burning CPU on animations
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Opacity = 0;
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        Activate();
        Topmost = true; Topmost = false; // bring to foreground
        Focus();
        ResumeAmbient(); // restore animations now that the window is visible again
    }

    private void QuitApplication()
    {
        _reallyQuit = true;
        Close();
    }

    /// <summary>Called when a second app launch is blocked by single-instance — bring this window forward.</summary>
    public void SurfaceFromAnotherInstance() => ShowFromTray();
}

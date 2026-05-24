using System.Diagnostics;
using System.Windows;
using SoundCheck.Services;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

// Settings overlay (+ live ApplySettings), File Manager overlay, Logs overlay,
// and database delete/restore/restart + autostart. Split out of MainWindow.xaml.cs.
public partial class MainWindow
{
    // ─── Settings overlay ──────────────────────────────────────────────────
    private bool _settingsOpen;
    private bool _settingsWired;
    private void BtnSettings_Click(object sender, RoutedEventArgs e) => ToggleSettings(!_settingsOpen);
    private void ToggleSettings(bool open)
    {
        if (!_settingsWired)
        {
            SettingsOverlayView.Closed += () => ToggleSettings(false);
            AppSettings.Changed += () => Dispatcher.Invoke(ApplySettings);
            SettingsOverlayView.DeleteDbRequested += OnDeleteDbRequested;
            SettingsOverlayView.ImportDbRequested += OnImportDbRequested;
            SettingsOverlayView.LogsRequested += () => ToggleLog(true);
            SettingsOverlayView.CleanMissingRequested += OnCleanMissingRequested;
            SettingsOverlayView.EqualizerChanged += (on, gains) => _audio.SetEqualizer(on, gains);
            LogOverlay.Closed += () => ToggleLog(false);
            _settingsWired = true;
        }
        _settingsOpen = open;
        if (open)
        {
            SettingsOverlayView.Reload();
            SettingsOverlayView.Visibility = Visibility.Visible;
            SettingsOverlayView.AnimateIn();
        }
        else
        {
            SettingsOverlayView.AnimateOut(() => SettingsOverlayView.Visibility = Visibility.Collapsed);
        }
    }

    // ─── File Manager overlay ─────────────────────────────────────────────
    private bool _fileMgrOpen;
    private bool _fileMgrWired;
    private bool _fmInitialized;
    private void BtnFileMgr_Click(object sender, RoutedEventArgs e) => ToggleFileManager(!_fileMgrOpen);

    private void ToggleFileManager(bool open)
    {
        if (!_fileMgrWired)
        {
            FileManagerOverlay.Closed += () => ToggleFileManager(false);
            FileManagerOverlay.FilesRenamed += OnFilesRenamed;
            FileManagerOverlay.EditTagsRequested += OnFmEditTagsRequested;
            _fileMgrWired = true;
        }
        _fileMgrOpen = open;
        if (open)
        {
            FileManagerOverlay.Visibility = Visibility.Visible;
            if (!_fmInitialized)
            {
                // Open at the common parent of ALL library tracks so sibling folders
                // (e.g. "maybe baby" + "свободная нация") are both visible — not just
                // the first track's own folder. Fall back to the Music folder.
                string initial = LibraryCommonFolder()
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                FileManagerOverlay.OpenFolder(initial);
                _fmInitialized = true;
            }
            FileManagerOverlay.AnimateIn();
        }
        else
        {
            FileManagerOverlay.AnimateOut(() => FileManagerOverlay.Visibility = Visibility.Collapsed);
        }
    }

    /// <summary>
    /// Longest common directory ancestor of every library track, so the File
    /// Manager opens at a folder that contains ALL track subfolders (not just
    /// the first track's own folder). Returns null if tracks span multiple
    /// drives or there are none.
    /// </summary>
    private string? LibraryCommonFolder()
    {
        var dirs = _allTracks
            .Select(t => System.IO.Path.GetDirectoryName(t.Path))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (dirs.Count == 0) return null;
        if (dirs.Count == 1) return dirs[0];

        char[] seps = { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };
        var split = dirs.Select(d => d!.Split(seps, StringSplitOptions.None)).ToList();
        int min = split.Min(s => s.Length);
        var common = new List<string>();
        for (int i = 0; i < min; i++)
        {
            var seg = split[0][i];
            if (split.All(s => string.Equals(s[i], seg, StringComparison.OrdinalIgnoreCase)))
                common.Add(seg);
            else break;
        }
        if (common.Count == 0) return null;

        var path = string.Join(System.IO.Path.DirectorySeparatorChar, common);
        if (path.EndsWith(":")) path += System.IO.Path.DirectorySeparatorChar; // drive root "C:" → "C:\"
        return System.IO.Directory.Exists(path) ? path : null;
    }

    /// <summary>Keep the library in sync when the File Manager renames files on disk.</summary>
    private void OnFilesRenamed(List<(string oldPath, string newPath)> map)
    {
        bool any = false;
        foreach (var (oldP, newP) in map)
        {
            var t = _allTracks.FirstOrDefault(x => string.Equals(x.Path, oldP, StringComparison.OrdinalIgnoreCase));
            if (t == null) continue;
            t.Path = newP;
            _storage.UpdateTrackPath(oldP, newP);
            if (_current == t) AppSettings.LastTrackPath = newP;
            any = true;
        }
        if (any)
        {
            RefreshVisible();
            RefreshQueueUi();
        }
    }

    /// <summary>
    /// Push every setting into the live UI. Called on startup and every time
    /// the user flips a switch in SettingsOverlay (via AppSettings.Changed).
    /// </summary>
    private void ApplySettings()
    {
        // Master "reduce motion" gate — when on, all ambient animations are off
        // regardless of their individual toggles.
        bool motion = !AppSettings.ReduceMotion;

        // Particles
        if (AppSettings.ParticlesEnabled && motion)
        {
            if (!_partTimer.IsEnabled) _partTimer.Start();
            ParticlesCanvas.Visibility = Visibility.Visible;
        }
        else
        {
            _partTimer.Stop();
            ParticlesCanvas.Visibility = Visibility.Collapsed;
        }
        // Floating background
        FloatBgCanvas.Visibility = (AppSettings.FloatingBgEnabled && motion) ? Visibility.Visible : Visibility.Collapsed;
        // Live equalizer in logo (start/stop pulse based on current playback state)
        if (AppSettings.LogoEqualizerEnabled && motion)
        {
            if (_audio.IsPlaying) StartLogoPulse();
        }
        else
        {
            StopLogoPulse();
        }
        // Accent color — re-resolve for the current track (cover vs. fixed manual).
        ApplyAccentNow();
        // Blurred background — re-show for the current cover or fade both layers out.
        if (AppSettings.BlurBgEnabled && _current?.CoverBytes != null)
            UpdateBlurBg(_current.CoverBytes);
        else
            UpdateBlurBg(null); // honors BlurBgEnabled: fades layers out when disabled
        // Autostart with Windows
        ApplyAutoStart(AppSettings.AutoStart);
        // Equalizer
        _audio.SetEqualizer(AppSettings.EqualizerEnabled, AppSettings.EqualizerBands);
    }

    // ─── Logs overlay ─────────────────────────────────────────────────────
    private bool _logOpen;
    private void ToggleLog(bool open)
    {
        _logOpen = open;
        if (open)
        {
            LogOverlay.Visibility = Visibility.Visible;
            LogOverlay.AnimateIn();
        }
        else
        {
            LogOverlay.AnimateOut(() => LogOverlay.Visibility = Visibility.Collapsed);
        }
    }

    // ─── Settings → DATA: remove tracks whose files are gone ──────────────
    private void OnCleanMissingRequested()
    {
        var missing = _allTracks.Where(t => t.IsMissing || !System.IO.File.Exists(t.Path)).ToList();
        if (missing.Count == 0) { ToastView.Show(Localization.T("ToastNoMissing")); return; }
        ConfirmOverlay.Show(
            Localization.T("ConfirmCleanMissingTitle"),
            string.Format(Localization.T("ConfirmCleanMissingMsg"), missing.Count),
            Localization.T("CleanMissingBtn"),
            ok =>
            {
                if (!ok) return;
                foreach (var t in missing)
                {
                    if (_current == t) { _audio.Stop(); _current = null; UpdateBarMeta(); }
                    _navHistory.RemoveAll(x => x == t);
                    _queue.RemoveAll(x => x == t); t.IsInQueue = false;
                    _allTracks.Remove(t);
                    _storage.DeleteTrack(t.Path); // also clears playlist references
                }
                RefreshVisible();
                RefreshPlaylistsUi();
                UpdateStats();
                RefreshQueueUi();
                Log.Info($"Removed {missing.Count} missing track(s)");
                ToastView.Show(string.Format(Localization.T("ToastMissingRemoved"), missing.Count));
            });
    }

    // ─── Settings → DATA: database deletion ───────────────────────────────
    private void OnDeleteDbRequested()
    {
        ConfirmOverlay.Show(
            Localization.T("ConfirmDeleteDbTitle"),
            Localization.T("ConfirmDeleteDbMsg"),
            Localization.T("ConfirmDeleteDbBtn"),
            ok =>
            {
                if (!ok) return;
                try { _audio.Stop(); } catch { }
                try { _storage.CloseAndDelete(); } catch { }
                RestartApp();
            });
    }

    private void OnImportDbRequested(string sourcePath)
    {
        ConfirmOverlay.Show(
            Localization.T("ConfirmImportTitle"),
            Localization.T("ConfirmImportMsg"),
            Localization.T("ConfirmImportBtn"),
            ok =>
            {
                if (!ok) return;
                try { _audio.Stop(); } catch { }
                try
                {
                    string dest = _storage.DbPath;
                    _storage.CloseConnection();
                    System.IO.File.Copy(sourcePath, dest, overwrite: true);
                }
                catch (Exception ex) { Debug.WriteLine($"Import failed: {ex.Message}"); }
                RestartApp();
            });
    }

    /// <summary>Relaunch a fresh instance and shut this one down (used after DB deletion / restore).</summary>
    private void RestartApp()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { }
        _reallyQuit = true;     // bypass close-to-tray on shutdown
        Application.Current.Shutdown();
    }

    private static void ApplyAutoStart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enable)
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue("SoundCheck", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("SoundCheck", throwOnMissingValue: false);
            }
        }
        catch { /* registry locked / no perms — silently skip */ }
    }
}

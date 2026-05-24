using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SoundCheck.Services;
using SoundCheck.Views;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

// Now Playing overlay + live visualizer, Profile, and the Queue panel.
// Split out of MainWindow.xaml.cs.
public partial class MainWindow
{
    private bool _npOpen;
    private DispatcherTimer? _vizTimer;
    private void ToggleNowPlaying(bool open)
    {
        _npOpen = open;
        if (open)
        {
            NowPlayingOverlay.Visibility = Visibility.Visible;
            UpdateNowPlaying();
            NowPlayingOverlay.AnimateIn();
            StartVisualizer();
        }
        else
        {
            StopVisualizer();
            NowPlayingOverlay.AnimateOut(() => NowPlayingOverlay.Visibility = Visibility.Collapsed);
        }
    }

    private void StartVisualizer()
    {
        if (_vizTimer == null)
        {
            _vizTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _vizTimer.Tick += OnVizTick;
        }
        _vizTimer.Start();
    }

    private void StopVisualizer()
    {
        _vizTimer?.Stop();
        NowPlayingOverlay.ClearSpectrum();
    }

    private void OnVizTick(object? sender, EventArgs e)
    {
        if (!_npOpen) { _vizTimer?.Stop(); return; }
        if (AppSettings.ReduceMotion) { NowPlayingOverlay.ClearSpectrum(); return; }
        if (_audio.IsPlaying) NowPlayingOverlay.UpdateSpectrum(_audio.GetFftBars(28));
        else NowPlayingOverlay.ClearSpectrum();
    }

    private void UpdateNowPlaying()
    {
        if (_current == null) return;
        var fullCover = _current.CoverBytes != null ? Library.LoadFullCover(_current.CoverBytes, 600) : null;
        var bg = _current.CoverBytes != null ? Library.LoadFullCover(_current.CoverBytes, 250) : null;
        NowPlayingOverlay.UpdateMeta(_current.Title,
            string.IsNullOrEmpty(_current.Album) ? _current.Artist : $"{_current.Artist} · {_current.Album}",
            fullCover, bg, _current.Duration);
        NowPlayingOverlay.UpdatePlaying(_audio.IsPlaying);
        NowPlayingOverlay.UpdateShuffle(_shuffle);
        NowPlayingOverlay.UpdateRepeat(_repeat == RepeatMode.One);
    }

    /// <summary>Confirm + wipe play stats/history (shared by Profile and Now Playing).</summary>
    private void RequestResetStats()
    {
        ConfirmOverlay.Show(Localization.T("ConfirmResetTitle"), Localization.T("ConfirmResetMsg"), Localization.T("ConfirmResetBtn"), ok =>
        {
            if (!ok) return;
            _storage.ResetStats();
            _storage.SetTotalListened(0);
            _totalListenedPersisted = 0;
            _sessionElapsed = TimeSpan.Zero;
            RefreshProfileUi();
            RefreshRecent();
            Log.Info("Stats and history reset");
            ToastView.Show(Localization.T("ToastStatsReset"));
        });
    }

    private void ToggleProfile(bool open)
    {
        _profileOpen = open;
        if (open)
        {
            ProfileOverlay.Visibility = Visibility.Visible;
            RefreshProfileUi();
            ProfileOverlay.AnimateIn();
        }
        else
        {
            ProfileOverlay.AnimateOut(() => ProfileOverlay.Visibility = Visibility.Collapsed);
        }
    }

    private void RefreshProfileUi()
    {
        var top = _storage.LoadTopTracks(5);
        var hist = _storage.LoadHistory(10);
        var totalPlays = _storage.TotalPlays();
        var fav = _storage.TopArtist();
        var totalSecs = _totalListenedPersisted + (long)_sessionElapsed.TotalSeconds;
        // Cover lookup by title|artist for top tracks + history rows
        var coverMap = new Dictionary<string, System.Windows.Media.Imaging.BitmapImage?>();
        foreach (var t in _allTracks)
        {
            var k = $"{t.Title}|{t.Artist}";
            if (!coverMap.ContainsKey(k)) coverMap[k] = t.Cover;
        }
        var playsByDay  = _storage.LoadPlaysPerDay(30);
        var playsByHour = _storage.LoadPlaysPerHour();
        ProfileOverlay.UpdateData(
            _allTracks.Count,
            totalSecs,
            totalPlays,
            FmtTotal((long)_sessionElapsed.TotalSeconds),
            fav,
            top,
            hist,
            coverMap,
            playsPerDay: playsByDay,
            playsPerHour: playsByHour);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Queue panel
    // ─────────────────────────────────────────────────────────────────────
    private void ToggleQueue(bool open)
    {
        _queueOpen = open;
        QueuePanelView.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation
        {
            From = open ? 360 : 0,
            To = open ? 0 : 360,
            Duration = TimeSpan.FromMilliseconds(380),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (!open) anim.Completed += (_, _) => QueuePanelView.Visibility = Visibility.Collapsed;
        QueuePanelView.SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        // HTML #qBd: dark click-to-dismiss backdrop, fades over .3s
        QueueBackdrop.Visibility = Visibility.Visible;
        var bdAnim = new DoubleAnimation { To = open ? 1 : 0, Duration = TimeSpan.FromMilliseconds(300) };
        if (!open) bdAnim.Completed += (_, _) => QueueBackdrop.Visibility = Visibility.Collapsed;
        QueueBackdrop.BeginAnimation(OpacityProperty, bdAnim);
        if (open) RefreshQueueUi();
    }

    private void QueueBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ToggleQueue(false);

    private void RefreshQueueUi()
    {
        var items = _queue.Select((t, i) => new QueuePanel.QueueItemViewModel
        {
            Index = i,
            Title = t.Title,
            Artist = t.Artist,
            Cover = t.Cover,
            IsCurrent = t == _current,
            Duration = t.Duration,
        }).ToList();
        QueuePanelView.SetItems(items);
        // Top "queue" pill removed — bottom-bar BtnQueueBottom now indicates queue state via Accent color.
        bool active = _queue.Count > 0;
        BtnQueueBottom.Foreground = active ? (Brush)FindResource("Accent") : (Brush)FindResource("T2");
        PersistQueue();
    }

    private string _lastQueuePersisted = "";
    /// <summary>Save the current queue (track paths) so it survives a restart.
    /// Skips the DB write when nothing changed (RefreshQueueUi is called often).</summary>
    private void PersistQueue()
    {
        if (!_queueRestored) return; // don't clobber the saved queue before it's restored
        var serialized = string.Join("\n", _queue.Select(t => t.Path));
        if (serialized == _lastQueuePersisted) return;
        _lastQueuePersisted = serialized;
        _storage.SetSetting("queue", serialized);
    }

    /// <summary>Rebuild the queue from saved paths on startup. Skips tracks no longer in the library.</summary>
    private void RestoreQueue()
    {
        var raw = _storage.GetSetting("queue");
        if (!string.IsNullOrEmpty(raw))
        {
            foreach (var path in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = _allTracks.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                if (t != null && !_queue.Contains(t)) { _queue.Add(t); t.IsInQueue = true; }
            }
        }
        _queueRestored = true;
        RefreshQueueUi();
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SoundCheck.Services;
using SoundCheck.Views;
using Track = SoundCheck.Models.Track;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

public partial class MainWindow : Window
{
    private readonly AudioPlayer _audio = new();
    private readonly Storage _storage = new();
    private TrayIcon? _tray;
    private bool _reallyQuit; // set true only when user picks "Quit" from tray menu
    private readonly ObservableCollection<Track> _allTracks = new();
    private readonly ObservableCollection<Track> _visible = new();
    private readonly List<Track> _queue = new();
    // The set of tracks playback flows through (a playlist, search results, or the
    // library). Next/shuffle/auto-advance operate WITHIN this, not the whole library.
    private readonly List<Track> _playContext = new();
    // Recently played track keys (title|artist) for smarter shuffle anti-repeat.
    private readonly List<string> _shuffleHistory = new();
    private Track? _current;
    private bool _shuffle;
    private RepeatMode _repeat = RepeatMode.Off;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _sessionTick;
    private TimeSpan _sessionElapsed = TimeSpan.Zero;
    private long _totalListenedPersisted;
    private bool _seeking;
    private string _searchQ = "";
    private string _sortMode = "added"; // added | title | artist | duration
    private long? _currentPlaylistId;   // null = whole library
    private bool _suppressPlaylistSel;  // guard programmatic ListBox selection changes
    private readonly List<Track> _navHistory = new();
    private bool _isPrevNav;
    private bool _historyRecordedForCurrent;
    private bool _crossfadeArmed;   // true once crossfade-advance fired for the current track
    private bool _queueOpen;
    private bool _queueRestored;
    private bool _profileOpen;

    // Particles
    private readonly List<(Ellipse el, double vx, double vy)> _particles = new();
    private readonly DispatcherTimer _partTimer;
    private readonly Random _rng = new();

    // Logo pulse
    private Storyboard? _logoPulse;

    public MainWindow()
    {
        InitializeComponent();
        TrackList.ItemsSource = _visible;
        AppSettings.Init(_storage);
        Localization.Init(AppSettings.Language);

        // Floating bg gradient (bgFloat keyframes from HTML)
        var bgX = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(18), RepeatBehavior = RepeatBehavior.Forever };
        bgX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        bgX.KeyFrames.Add(new EasingDoubleKeyFrame(40, KeyTime.FromPercent(0.33)));
        bgX.KeyFrames.Add(new EasingDoubleKeyFrame(-30, KeyTime.FromPercent(0.66)));
        bgX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        var bgY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(18), RepeatBehavior = RepeatBehavior.Forever };
        bgY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        bgY.KeyFrames.Add(new EasingDoubleKeyFrame(-25, KeyTime.FromPercent(0.33)));
        bgY.KeyFrames.Add(new EasingDoubleKeyFrame(35, KeyTime.FromPercent(0.66)));
        bgY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        FloatBgTrans.BeginAnimation(TranslateTransform.XProperty, bgX);
        FloatBgTrans.BeginAnimation(TranslateTransform.YProperty, bgY);
        // HTML bgFloat keyframes also scale: 1 → 1.05 → 0.97 → 1
        var bgS = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(18), RepeatBehavior = RepeatBehavior.Forever };
        bgS.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(0)));
        bgS.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromPercent(0.33)));
        bgS.KeyFrames.Add(new EasingDoubleKeyFrame(0.97, KeyTime.FromPercent(0.66)));
        bgS.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(1)));
        FloatBgScale.BeginAnimation(ScaleTransform.ScaleXProperty, bgS);
        FloatBgScale.BeginAnimation(ScaleTransform.ScaleYProperty, bgS);

        BuildLogoLettersWave();

        // Load settings
        if (float.TryParse(_storage.GetSetting("volume"), out var vol))
        {
            _audio.Volume = vol;
        }
        SldVolume.Value = _audio.Volume;
        _shuffle = _storage.GetSetting("shuffle") == "1";
        _repeat = _storage.GetSetting("repeat") == "one" ? RepeatMode.One : RepeatMode.Off;
        var savedSort = _storage.GetSetting("sort");
        if (savedSort is "added" or "title" or "artist" or "duration") _sortMode = savedSort;
        UpdateSortLabel();
        Localization.Changed += UpdateSortLabel; // keep the sort button label in the active language
        _totalListenedPersisted = _storage.TotalListenedSecs();
        UpdateShuffleVisual();
        UpdateRepeatVisual();

        // Load library
        foreach (var s in _storage.LoadTracks())
        {
            _allTracks.Add(new Track
            {
                Path = s.Path,
                Title = s.Title,
                Artist = s.Artist,
                Album = s.Album,
                Duration = TimeSpan.FromSeconds(s.DurationSecs),
                CoverBytes = s.CoverBlob,
                Cover = Library.LoadThumb(s.CoverBlob, 80),
                IsMissing = !File.Exists(s.Path),
                IsExplicit = s.Explicit,
            });
        }
        RefreshVisible();
        UpdateStats();
        RefreshPlaylistsUi();
        RestoreQueue(); // rebuild last session's play queue (sets _queueRestored)

        // Show toast if library was loaded from storage
        if (_allTracks.Count > 0)
        {
            Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                ToastView.Show(string.Format(Localization.T("ToastLoadedFmt"), _allTracks.Count))
            ), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Apply user preferences once the UI is ready and restore last track if asked.
        Loaded += (_, _) =>
        {
            ApplySettings();
            if (AppSettings.RememberPosition && !string.IsNullOrEmpty(AppSettings.LastTrackPath))
            {
                var path = AppSettings.LastTrackPath;
                var t = _allTracks.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                if (t != null)
                {
                    PlayTrack(t);
                    _audio.Pause(); // load + position, but don't auto-play
                    UpdatePlayButton();
                    double pos = AppSettings.LastTrackPosition;
                    if (pos > 1 && _audio.Duration.TotalSeconds > pos + 1)
                        _audio.Seek(pos / _audio.Duration.TotalSeconds);
                }
            }
        };

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += OnTick;
        _tick.Start();

        _sessionTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTick.Tick += (_, _) =>
        {
            if (_audio.IsPlaying)
            {
                _sessionElapsed = _sessionElapsed.Add(TimeSpan.FromSeconds(1));
                if ((long)_sessionElapsed.TotalSeconds % 10 == 0)
                    _storage.SetTotalListened(_totalListenedPersisted + (long)_sessionElapsed.TotalSeconds);
            }
        };
        _sessionTick.Start();

        _audio.PlaybackEnded += OnTrackEnded;

        // Queue events
        QueuePanelView.Closed += () => ToggleQueue(false);
        QueuePanelView.Cleared += () => { foreach (var t in _queue) t.IsInQueue = false; _queue.Clear(); RefreshQueueUi(); };
        QueuePanelView.Shuffled += () =>
        {
            var rnd = new Random();
            for (int i = _queue.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
            }
            RefreshQueueUi();
        };
        QueuePanelView.PlayAllRequested += () =>
        {
            if (_queue.Count == 0) return;
            var t = _queue[0];
            _queue.RemoveAt(0);
            t.IsInQueue = false;
            RefreshQueueUi();
            PlayTrack(t);
        };
        QueuePanelView.PlayAt += idx =>
        {
            if (idx >= 0 && idx < _queue.Count)
            {
                var t = _queue[idx];
                _queue.RemoveAt(idx);
                t.IsInQueue = false;
                RefreshQueueUi();
                PlayTrack(t);
            }
        };
        QueuePanelView.RemoveAt += idx =>
        {
            if (idx >= 0 && idx < _queue.Count) { _queue[idx].IsInQueue = false; _queue.RemoveAt(idx); RefreshQueueUi(); }
        };
        QueuePanelView.Reordered += (from, to) =>
        {
            if (from < 0 || from >= _queue.Count) return;
            var t = _queue[from];
            _queue.RemoveAt(from);
            if (from < to) to--;            // account for removed index shift
            to = Math.Clamp(to, 0, _queue.Count);
            _queue.Insert(to, t);
            RefreshQueueUi();
        };

        // Profile events
        ProfileOverlay.Closed += () => ToggleProfile(false);
        ProfileOverlay.ResetRequested += RequestResetStats;

        // Now Playing events
        NowPlayingOverlay.Closed += () => ToggleNowPlaying(false);
        NowPlayingOverlay.ResetStatsRequested += RequestResetStats;
        NowPlayingOverlay.PlayPause += () => { _audio.TogglePlay(); UpdatePlayButton(); NowPlayingOverlay.UpdatePlaying(_audio.IsPlaying); };
        NowPlayingOverlay.Prev += () => BtnPrev_Click(this, new RoutedEventArgs());
        NowPlayingOverlay.Next += () => BtnNext_Click(this, new RoutedEventArgs());
        NowPlayingOverlay.ShuffleToggle += () => BtnShuffle_Click(this, new RoutedEventArgs());
        NowPlayingOverlay.RepeatToggle += () => BtnRepeat_Click(this, new RoutedEventArgs());
        NowPlayingOverlay.Seek += f => _audio.Seek(f);

        // Splash hides automatically
        SplashOverlay.Finished += () => SplashOverlay.Visibility = Visibility.Collapsed;

        // Help overlay
        HelpOverlay.Closed += () => ToggleHelp(false);

        // Init filter tabs visual
        Loaded += (_, _) => SetTabActive(BtnTabAll, true);

        // Particles timer (paused when not playing)
        _partTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _partTimer.Tick += OnParticlesTick;
        Loaded += (_, _) =>
        {
            InitParticles();
            // Only tick if particles are enabled and motion isn't reduced.
            // (ApplySettings already ran on Loaded and set canvas visibility;
            //  starting unconditionally here would re-enable a disabled timer.)
            if (AppSettings.ParticlesEnabled && !AppSettings.ReduceMotion)
                _partTimer.Start();
        };

        KeyDown += MainWindow_KeyDown;

        // Pause ambient animations when the window is minimized (CPU saver).
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) PauseAmbient();
            else ResumeAmbient();
        };

        // ── Tray icon (minimize-to-tray on close) ─────────────────────────
        Loaded += (_, _) => InitTray();
        Closing += MainWindow_Closing;
    }

    /// <summary>Stop decorative animations (particles, floating cover, logo pulse)
    /// while the window is hidden/minimized — they're invisible and just burn CPU.</summary>
    private void PauseAmbient()
    {
        _partTimer.Stop();
        StopLogoPulse();
    }

    /// <summary>Restart the decorative animations honoring current settings + playback.</summary>
    private void ResumeAmbient()
    {
        bool motion = !AppSettings.ReduceMotion;
        if (AppSettings.ParticlesEnabled && motion && !_partTimer.IsEnabled) _partTimer.Start();
        if (AppSettings.LogoEqualizerEnabled && motion && _audio.IsPlaying) StartLogoPulse();
    }

    // ─────────────────────────────────────────────────────────────────────
    // File dialogs  (tray methods live in MainWindow.Tray.cs)
    // ─────────────────────────────────────────────────────────────────────
    private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddPopup.IsOpen = true;

    private void AddFilesItem_Click(object sender, RoutedEventArgs e)
    {
        AddPopup.IsOpen = false;
        BtnAddFiles_Click(sender, e);
    }

    private void AddFolderItem_Click(object sender, RoutedEventArgs e)
    {
        AddPopup.IsOpen = false;
        BtnAddFolder_Click(sender, e);
    }

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Audio (*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac)|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|All|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            AddPaths(dlg.FileNames);
        }
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            var files = Directory.EnumerateFiles(dlg.FolderName, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".aac", StringComparison.OrdinalIgnoreCase));
            AddPaths(files);
        }
    }

    private async void AddPaths(IEnumerable<string> paths)
    {
        // Dedup against the current library on the UI thread.
        var existing = new HashSet<string>(_allTracks.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
        var toLoad = new List<string>();
        int skipped = 0;
        foreach (var p in paths)
        {
            if (existing.Contains(p)) skipped++;
            else { toLoad.Add(p); existing.Add(p); }
        }

        if (toLoad.Count == 0)
        {
            if (skipped > 0) ToastView.Show(string.Format(Localization.T("ToastSkippedFmt"), skipped));
            return;
        }

        Log.Info($"Adding {toLoad.Count} file(s) (skipped {skipped} duplicate)");

        // Read tags + decode thumbnails off the UI thread (covers are frozen → cross-thread safe).
        int failed = 0;
        var loaded = await Task.Run(() =>
        {
            var list = new List<Track>();
            foreach (var path in toLoad)
            {
                try
                {
                    var t = Library.LoadTrack(path);
                    if (t != null) list.Add(t);
                    else { failed++; Log.Warn($"Could not read: {path}"); }
                }
                catch (Exception ex) { failed++; Log.Error($"Add failed: {path}", ex); }
            }
            return list;
        });

        // Commit to the library + DB on the UI thread.
        int added = 0;
        foreach (var t in loaded)
        {
            _allTracks.Add(t);
            try { _storage.SaveTrack(t); added++; }
            catch (Exception ex) { Log.Error($"Save failed: {t.Path}", ex); }
        }
        try { RefreshVisible(); UpdateStats(); }
        catch (Exception ex) { Log.Error("Refresh after add failed", ex); }

        Log.Info($"Added {added}, skipped {skipped}, failed {failed}");

        if (added > 0 || skipped > 0)
        {
            string msg = (added, skipped) switch
            {
                ( > 0, 0) => string.Format(Localization.T("ToastAddedFmt"), added),
                (0, > 0) => string.Format(Localization.T("ToastSkippedFmt"), skipped),
                _ => string.Format(Localization.T("ToastAddedSkippedFmt"), added, skipped),
            };
            ToastView.Show(msg);
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (_allTracks.Count == 0) return;
        ConfirmOverlay.Show(Localization.T("ConfirmClearTitle"), Localization.T("ConfirmClearMsg"), Localization.T("ConfirmClearBtn"), ok =>
        {
            if (!ok) return;
            _audio.Stop();
            foreach (var t in _queue) t.IsInQueue = false;
            _allTracks.Clear();
            _visible.Clear();
            _queue.Clear();
            _current = null;
            _navHistory.Clear();
            _storage.ClearTracks();
            _currentPlaylistId = null;
            TxtCollectionName.Text = Localization.T("MyCollection");
            RefreshPlaylistsUi();
            UpdateStats();
            UpdateBarMeta();
            RefreshQueueUi();
            BlurBgImg.Source = null;
            ToastView.Show(Localization.T("ToastLibraryCleared"));
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // Search
    // ─────────────────────────────────────────────────────────────────────
    private DispatcherTimer? _searchDebounce;
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQ = TxtSearch.Text?.Trim() ?? "";
        bool empty = string.IsNullOrEmpty(TxtSearch.Text);
        TxtSearchPh.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        // Hidden (not Collapsed) → reserves layout space, search border width stays constant
        BtnSearchClear.Visibility = empty ? Visibility.Hidden : Visibility.Visible;
        // Debounce: rebuilding the visible list on every keystroke is wasteful on big
        // libraries — coalesce rapid typing into a single refresh.
        if (_searchDebounce == null)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _searchDebounce.Tick += (_, _) => { _searchDebounce!.Stop(); RefreshVisible(); };
        }
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void BtnSearchClear_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = "";
        TxtSearch.Focus();
    }

    // ─── Library sorting ──────────────────────────────────────────────────
    private void BtnSort_Click(object sender, RoutedEventArgs e) => SortPopup.IsOpen = !SortPopup.IsOpen;

    private void SortItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string mode)
        {
            _sortMode = mode;
            _storage.SetSetting("sort", mode);
            UpdateSortLabel();
            RefreshVisible();
        }
        SortPopup.IsOpen = false;
    }

    private void UpdateSortLabel()
    {
        BtnSort.Content = _sortMode switch
        {
            "title"    => Localization.T("SortTitle"),
            "artist"   => Localization.T("SortArtist"),
            "duration" => Localization.T("SortDuration"),
            _           => Localization.T("SortAdded"),
        };
        BtnSort.ToolTip = Localization.T("SortBy") + ": " + BtnSort.Content;
        // Highlight the active option in the sort popup (the button no longer shows text).
        // TryFindResource (not FindResource) so it never throws if called before the
        // accent brushes are registered.
        if (SortItemsHost != null)
            foreach (var child in SortItemsHost.Children)
                if (child is Button b
                    && TryFindResource((b.Tag as string) == _sortMode ? "Accent" : "T2") is Brush br)
                    b.Foreground = br;
    }

    private void RefreshVisible()
    {
        _visible.Clear();
        // Source collection: the whole library, or the active playlist (in playlist order).
        IReadOnlyList<Track> collection;
        if (_currentPlaylistId is long pid)
        {
            var byPath = _allTracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
            collection = _storage.LoadPlaylistPaths(pid)
                .Where(p => byPath.ContainsKey(p))
                .Select(p => byPath[p])
                .ToList();
        }
        else collection = _allTracks;

        // Hero header reflects the CURRENT collection (playlist or library), not the
        // search-filtered subset — count + total duration of what you're looking at.
        long colDur = (long)collection.Sum(t => t.Duration.TotalSeconds);
        TxtHeroTracksRun.Text = collection.Count.ToString();
        TxtHeroTimeRun.Text = FmtTotal(colDur);

        IEnumerable<Track> src = collection;
        if (!string.IsNullOrEmpty(_searchQ))
        {
            var q = _searchQ.ToLowerInvariant();
            src = src.Where(t =>
                t.Title.ToLowerInvariant().Contains(q) ||
                t.Artist.ToLowerInvariant().Contains(q));
        }
        // "added" keeps the natural library (insertion) order.
        src = _sortMode switch
        {
            "title"    => src.OrderBy(t => t.Title,  StringComparer.CurrentCultureIgnoreCase),
            "artist"   => src.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(t => t.Title,  StringComparer.CurrentCultureIgnoreCase),
            "duration" => src.OrderBy(t => t.Duration),
            _           => src,
        };
        int i = 1;
        foreach (var t in src) { t.Index = i++; _visible.Add(t); }

        if (TxtHeroFound != null)
        {
            bool searching = !string.IsNullOrEmpty(_searchQ);
            TxtHeroFoundSep.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
            TxtHeroFound.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
            if (searching) TxtHeroFoundRun.Text = _visible.Count.ToString();
        }

        // Context-aware empty state: nothing-found vs empty-playlist vs empty-library.
        if (EmptyHint != null)
        {
            if (_visible.Count > 0)
            {
                EmptyHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtEmptyHint.Text =
                    !string.IsNullOrEmpty(_searchQ) ? Localization.T("TracksNotFound")
                    : _currentPlaylistId != null    ? Localization.T("PlaylistEmptyHint")
                    :                                 Localization.T("LibEmpty");
                EmptyHint.Visibility = Visibility.Visible;
            }
        }
    }

    private void BtnTabAll_Click(object sender, RoutedEventArgs e)
    {
        // "All tracks" tab — leave any active playlist and show the whole library.
        if (_currentPlaylistId != null) ShowAllTracks();
        else SetTabActive(BtnTabAll, true);
    }

    private void SetTabActive(Button tab, bool active)
    {
        // SetResourceReference creates a live DynamicResource binding — so when
        // the Accent brush is swapped on a cover-color change, this button's
        // Foreground tracks it. Using `tab.Foreground = (Brush)FindResource(...)`
        // captured a snapshot of the brush at click time and never updated again.
        tab.SetResourceReference(ForegroundProperty, active ? "Accent" : "T2");
        tab.Tag = active ? "active" : null;
    }

    // Row queue / delete buttons
    private void RowQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Track t)
        {
            if (_queue.Contains(t)) { _queue.Remove(t); t.IsInQueue = false; }
            else { _queue.Add(t); t.IsInQueue = true; }
            RefreshQueueUi();
        }
        e.Handled = true;
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Track t)
        {
            // Inside a playlist the ✕ removes the track from THAT playlist only,
            // not from the whole library.
            if (_currentPlaylistId is long pid)
            {
                _storage.RemoveFromPlaylist(pid, t.Path);
                RefreshVisible();
                RefreshPlaylistsUi();
            }
            else
            {
                DeleteTrackFromLibrary(t);
            }
        }
        e.Handled = true;
    }

    /// <summary>Remove a track from the library entirely (DB + queue + history).</summary>
    private void DeleteTrackFromLibrary(Track t)
    {
        if (_current == t) { _audio.Stop(); _current = null; UpdateBarMeta(); }
        _navHistory.RemoveAll(x => x == t);
        _queue.RemoveAll(x => x == t); t.IsInQueue = false;
        _allTracks.Remove(t);
        _storage.DeleteTrack(t.Path);
        RefreshVisible();
        UpdateStats();
        RefreshQueueUi();
    }

    // Sleep timer lives in MainWindow.Sleep.cs

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        _audio.ToggleMute();
        UpdateMuteVisual();
    }

    private void UpdateMuteVisual()
    {
        PathMute.Data = _audio.IsMuted
            ? (Geometry)FindResource("VolumeMuteGeo")
            : (Geometry)FindResource("VolumeGeo");
        // Always follows accent (cover color). Active=full opacity, idle=dim
        BtnMute.SetResourceReference(ForegroundProperty, "Accent");
        BtnMute.Opacity = _audio.IsMuted ? 1.0 : (BtnMute.IsMouseOver ? 1.0 : 0.65);
    }

    private void BtnGithub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/svobodenn/soundcheck",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
    }

    private bool _helpOpen;
    private void BtnHelp_Click(object sender, RoutedEventArgs e) => ToggleHelp(!_helpOpen);
    private void ToggleHelp(bool open)
    {
        _helpOpen = open;
        if (open)
        {
            HelpOverlay.Visibility = Visibility.Visible;
            HelpOverlay.AnimateIn();
        }
        else
        {
            HelpOverlay.AnimateOut(() => HelpOverlay.Visibility = Visibility.Collapsed);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Playback
    // ─────────────────────────────────────────────────────────────────────
    private void TrackList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // If a real drag happened, MouseMove cleared _dragItem already.
        // Otherwise (simple click), play the track and reset.
        if (e.OriginalSource is DependencyObject d)
        {
            var row = FindParent<ListViewItem>(d);
            if (row != null && row.DataContext is Track t)
            {
                if (FindParent<Button>(d) == null) { SetPlaybackContext(); PlayTrack(t); }
            }
        }
        _dragItem = null;
    }

    // Playback control + player bar live in MainWindow.Playback.cs

    // ─────────────────────────────────────────────────────────────────────
    // Stats / formatters
    // ─────────────────────────────────────────────────────────────────────
    private void UpdateStats()
    {
        TxtTabAllCount.Text = _allTracks.Count.ToString();
        UpdateHeroActions();
        RefreshRecent();
        // Hero count/duration + empty-state hint are managed by RefreshVisible
        // (it knows the playlist/search context).
    }

    /// <summary>
    /// Repopulate the sidebar "Recent" list. We display up to 6 most-recent
    /// history entries, matched against the library by Title+Artist so we can
    /// reuse the track's in-memory cover thumbnail and reach Path on click.
    /// </summary>
    private void RefreshRecent()
    {
        var hist = _storage.LoadHistory(6);
        var rows = new List<object>(hist.Count);
        foreach (var h in hist)
        {
            var track = _allTracks.FirstOrDefault(t =>
                string.Equals(t.Title,  h.Title,  StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Artist, h.Artist, StringComparison.OrdinalIgnoreCase));
            rows.Add(new
            {
                Title  = string.IsNullOrEmpty(h.Title)  ? "—" : h.Title,
                Artist = string.IsNullOrEmpty(h.Artist) ? ""  : h.Artist,
                Cover  = track?.Cover,
                Path   = track?.Path ?? "",
            });
        }
        RecentList.ItemsSource = rows;
        RecentEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecentList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem == null) return;
        // Extract Path via reflection (anonymous-object field).
        var path = (string?)RecentList.SelectedItem.GetType().GetProperty("Path")?.GetValue(RecentList.SelectedItem) ?? "";
        RecentList.SelectedIndex = -1; // clear so re-clicking the same item still works
        if (string.IsNullOrEmpty(path)) return;
        var track = _allTracks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (track != null) { _playContext.Clear(); PlayTrack(track); } // continue through the library
    }

    private static string FmtTime(TimeSpan t) =>
        t.TotalSeconds < 0 || double.IsNaN(t.TotalSeconds)
            ? "0:00"
            : t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";

    private static string FmtTotal(long secs)
    {
        bool en = Localization.Current == Localization.En;
        string sU = en ? "s" : "с";
        string mU = en ? "m" : "м";
        string hU = en ? "h" : "ч";
        if (secs < 60) return $"{secs}{sU}";
        long m = secs / 60;
        if (m < 60) return $"{m}{mU}";
        long h = m / 60;
        long rm = m % 60;
        return rm == 0 ? $"{h}{hU}" : $"{h}{hU} {rm}{mU}";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Profile overlay
    // ─────────────────────────────────────────────────────────────────────
    private void BtnProfile_Click(object sender, RoutedEventArgs e) => ToggleProfile(!_profileOpen);
    private void BtnQueue_Click(object sender, RoutedEventArgs e) => ToggleQueue(!_queueOpen);

    // Settings / File Manager / Logs / DB delete-restore live in MainWindow.Settings.cs

    private void BarCover_Click(object sender, MouseButtonEventArgs e)
    {
        if (_current != null) ToggleNowPlaying(true);
    }

    private void BarCover_MouseEnter(object sender, MouseEventArgs e)
    {
        BarCoverBorder.BorderBrush = (Brush)FindResource("AccentDim");
        var sx = new DoubleAnimation { To = 1.04, Duration = TimeSpan.FromMilliseconds(200) };
        BarCoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        BarCoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        ExpandCue.BeginAnimation(OpacityProperty, new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(150) });
    }

    private void BarCover_MouseLeave(object sender, MouseEventArgs e)
    {
        BarCoverBorder.BorderBrush = (Brush)FindResource("B1");
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
        BarCoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        BarCoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        ExpandCue.BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150) });
    }

    // Now Playing / visualizer / Profile / Queue live in MainWindow.Overlays.cs
    // Context menu + drag-drop + reorder live in MainWindow.TrackList.cs

    // ─────────────────────────────────────────────────────────────────────
    // Keyboard shortcuts
    // ─────────────────────────────────────────────────────────────────────
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            // Only three playback shortcuts remain: Space = play/pause, ← prev, → next.
            case Key.Space: BtnPlay_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Left:  BtnPrev_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Right: BtnNext_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            // Esc still closes the active overlay (UI navigation, not a media key).
            case Key.Escape:
                if (_logOpen) ToggleLog(false);
                else if (_fileMgrOpen) ToggleFileManager(false);
                else if (_settingsOpen) ToggleSettings(false);
                else if (_helpOpen) ToggleHelp(false);
                else if (_npOpen) ToggleNowPlaying(false);
                else if (_profileOpen) ToggleProfile(false);
                else if (_queueOpen) ToggleQueue(false);
                break;
        }
    }

    // Chrome buttons: window-opacity transitions + per-button click "punch" feedback (BUTTON scale).
    private bool _wantsRestoreFade;

    // ── Disable native Windows minimize/maximize "ghost rectangle" animation
    // so our WPF shrink-to-taskbar is the only thing the user sees.
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private bool _systemMinimizeAnimDisabled;
    private void DisableSystemMinimizeAnim()
    {
        if (_systemMinimizeAnimDisabled) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int)); }
        catch { /* not critical */ }
        _systemMinimizeAnimDisabled = true;
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e)
    {
        PunchButton(BtnMin);
        DisableSystemMinimizeAnim();
        // "Shrink-to-taskbar" minimize:
        //   • Root grid scales 1 → 0.08 (almost a point), origin already at bottom-center
        //     so visually it collapses toward the taskbar area
        //   • Translates down by ~half its height so it ends up below the visible window
        //   • Opacity fades to 0
        //   • Once done, hand off to actual WindowState=Minimized (or hide-to-tray)
        var dur  = TimeSpan.FromMilliseconds(280);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sa = new DoubleAnimation { To = 0.08, Duration = dur, EasingFunction = ease };
        var ty = new DoubleAnimation { To = ActualHeight * 0.45, Duration = dur, EasingFunction = ease };
        var fa = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };

        fa.Completed += (_, _) =>
        {
            if (AppSettings.MinimizeToTray)
            {
                HideToTray();
                // Reset transform so when the window comes back, it's at full size
                AppScale.ScaleX = AppScale.ScaleY = 1;
                AppEntranceTrans.Y = 0;
                Opacity = 1;
            }
            else
            {
                _wantsRestoreFade = true;
                WindowState = WindowState.Minimized;
            }
        };
        AppScale.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
        AppScale.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
        AppEntranceTrans.BeginAnimation(TranslateTransform.YProperty, ty);
        BeginAnimation(OpacityProperty, fa);

        StateChanged -= OnRestoreFromMin;
        StateChanged += OnRestoreFromMin;
    }

    private void OnRestoreFromMin(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized || !_wantsRestoreFade) return;
        _wantsRestoreFade = false;
        // Reverse: scale 0.08 → 1, translate back to 0, opacity 0 → 1.
        var dur  = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sa = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = ease };
        var ty = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
        var fa = new DoubleAnimation { From = 0, To = 1.0, Duration = dur, EasingFunction = ease };
        AppScale.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
        AppScale.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
        AppEntranceTrans.BeginAnimation(TranslateTransform.YProperty, ty);
        BeginAnimation(OpacityProperty, fa);
    }

    private void BtnMax_Click(object sender, RoutedEventArgs e)
    {
        PunchButton(BtnMax);
        // Toggle state — Windows handles its own native maximize/restore animation
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        PunchButton(BtnClose);
        // X-button now sends us to the tray instead of quitting (Quit is reachable
        // from the tray menu's "Выйти" item).
        var fade = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close(); // Closing handler hides to tray
        BeginAnimation(OpacityProperty, fade);
    }

    // Click feedback: scale BUTTON briefly 0.85 → 1.0. Doesn't touch window state, so no flicker.
    private void PunchButton(System.Windows.Controls.Button b)
    {
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = b.RenderTransform as ScaleTransform;
        if (sc == null) { sc = new ScaleTransform(1, 1); b.RenderTransform = sc; }
        var dur = TimeSpan.FromMilliseconds(140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var a = new DoubleAnimationUsingKeyFrames { Duration = dur };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromPercent(0))   { EasingFunction = ease });
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.82, KeyTime.FromPercent(0.4)) { EasingFunction = ease });
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromPercent(1))   { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Toggle between single-square (normal) and overlapping-squares (restore) icon
        if (MaxNormal != null && MaxRestore != null)
        {
            bool max = WindowState == WindowState.Maximized;
            MaxNormal.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            MaxRestore.Visibility = max ? Visibility.Visible  : Visibility.Collapsed;
        }
        UpdateWindowChrome();
    }

    private void WindowRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateWindowChrome();

    /// <summary>Rounded corners + thin white outline when windowed; flush + square when maximized.</summary>
    private void UpdateWindowChrome()
    {
        if (WindowRoot == null) return;
        bool max = WindowState == WindowState.Maximized;
        double r = max ? 0 : 10;
        WindowRoot.CornerRadius = new CornerRadius(r);
        if (WindowBorderOverlay != null)
        {
            WindowBorderOverlay.CornerRadius = new CornerRadius(r);
            WindowBorderOverlay.BorderThickness = new Thickness(max ? 0 : 1);
        }
        if (WindowRoot.ActualWidth > 0 && WindowRoot.ActualHeight > 0)
            WindowRoot.Clip = new RectangleGeometry(
                new Rect(0, 0, WindowRoot.ActualWidth, WindowRoot.ActualHeight), r, r);
    }

    protected override void OnClosed(EventArgs e)
    {
        _storage.SetTotalListened(_totalListenedPersisted + (long)_sessionElapsed.TotalSeconds);
        _audio.Dispose();
        _storage.Dispose();
        base.OnClosed(e);
    }
}

public enum RepeatMode { Off, One }

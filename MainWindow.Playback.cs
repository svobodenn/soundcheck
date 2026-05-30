using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SoundCheck.Services;
using Track = SoundCheck.Models.Track;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

// Playback control + the player bar: track switching, transport buttons,
// progress/volume, per-track accent fade + blurred background, glows/pulses.
// Split out of MainWindow.xaml.cs.
public partial class MainWindow
{
    private void PlayTrack(Track t)
    {
        // Don't try to play a file that's gone — flag it and tell the user.
        if (!System.IO.File.Exists(t.Path))
        {
            t.IsMissing = true;
            ToastView.Show(Localization.T("ToastFileMissing"));
            Log.Warn($"Play skipped, file missing: {t.Path}");
            return;
        }
        t.IsMissing = false;
        try
        {
            _crossfadeArmed = false;
            _audio.Load(t.Path, AppSettings.CrossfadeSeconds);
            _audio.Play();
            if (_current != null) { _current.IsCurrent = false; _current.IsCurrentPlaying = false; }
            _current = t;
            t.IsCurrent = true;
            t.IsCurrentPlaying = true;
            RememberPlayed(t); // shuffle anti-repeat window
            _historyRecordedForCurrent = false;
            if (!_isPrevNav)
            {
                _navHistory.Add(t);
                if (_navHistory.Count > 50) _navHistory.RemoveAt(0);
            }
            _isPrevNav = false;
            // Immediate, cheap UI feedback so the switch feels instant.
            UpdateBarMeta();
            UpdatePlayButton();
            AnimateBarGlow();
            StartLogoPulse();
            PulseArtBorder();
            PulseCurrentRow();
            if (NowPlayingOverlay.Visibility == Visibility.Visible) UpdateNowPlaying();
            Log.Info($"Playing: {t.Artist} — {t.Title}");

            // Defer the heavy cover work (color extraction, blur decode, list layout)
            // to the next idle frame so clicking a track doesn't stutter.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_current != t) return; // user already switched again
                var cover = CoverBytesFor(t);
                ApplyAccentFromCover(cover);
                UpdateBlurBg(cover);
                ScrollToCurrent();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PlayTrack failed: {ex.Message}");
            Log.Error($"PlayTrack failed: {t.Path}", ex);
            ToastView.Show(Localization.T("ToastPlayFailed"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Auto-accent + blurred BG
    // ─────────────────────────────────────────────────────────────────────

    // Smooth accent fade — re-replaces brushes in Resources every frame.
    private Color _accentFrom, _accentTo, _dimFrom, _dimTo;
    private DateTime _accentFadeStart;
    private bool _accentFadeRunning;
    private const double AccentFadeMs = 900;
    private void OnAccentFadeFrame(object? sender, EventArgs e)
    {
        var t = Math.Clamp((DateTime.Now - _accentFadeStart).TotalMilliseconds / AccentFadeMs, 0, 1);
        // CubicEase EaseOut: 1 - (1-t)^3
        double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
        var acc = LerpColor(_accentFrom, _accentTo, eased);
        var dim = LerpColor(_dimFrom,    _dimTo,    eased);
        Application.Current.Resources["Accent"]    = new SolidColorBrush(acc);
        Application.Current.Resources["AccentDim"] = new SolidColorBrush(dim);
        if (t >= 1.0)
        {
            _accentFadeRunning = false;
            CompositionTarget.Rendering -= OnAccentFadeFrame;
        }
    }
    private static Color LerpColor(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
    private void StartAccentFade(Color newAccent, Color newDim)
    {
        // Reduce-motion: swap colors instantly, no per-frame fade.
        if (AppSettings.ReduceMotion)
        {
            Application.Current.Resources["Accent"]    = new SolidColorBrush(newAccent);
            Application.Current.Resources["AccentDim"] = new SolidColorBrush(newDim);
            return;
        }
        var currentAcc = (Application.Current.Resources["Accent"] as SolidColorBrush)?.Color
                         ?? Color.FromRgb(0xC8, 0xA9, 0x6E);
        var currentDim = (Application.Current.Resources["AccentDim"] as SolidColorBrush)?.Color
                         ?? Color.FromRgb(0x6B, 0x5A, 0x38);
        _accentFrom = currentAcc; _accentTo = newAccent;
        _dimFrom    = currentDim; _dimTo    = newDim;
        _accentFadeStart = DateTime.Now;
        if (!_accentFadeRunning)
        {
            _accentFadeRunning = true;
            CompositionTarget.Rendering += OnAccentFadeFrame;
        }
    }

    /// <summary>Gold fallback used whenever no per-track / manual accent applies.</summary>
    private static readonly Color DefaultAccent = Color.FromRgb(0xC8, 0xA9, 0x6E);

    /// <summary>Manual accent from settings (<c>#RRGGBB</c>), or the default gold.</summary>
    private static Color ResolveManualAccent()
    {
        var hex = AppSettings.AccentColor;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { /* malformed → default */ }
        }
        return DefaultAccent;
    }

    /// <summary>Re-resolve and apply the accent for the current track + settings.
    /// Called when accent-related settings change.</summary>
    private void ApplyAccentNow() => ApplyAccentFromCover(CoverBytesFor(_current));

    /// <summary>Get a track's full cover bytes, fetching from SQLite on demand when
    /// they're not pinned in RAM. Keeps the library lightweight — covers live in the
    /// database and are only materialized when needed (accent/blur/now-playing/tags).</summary>
    private byte[]? CoverBytesFor(Track? t)
    {
        if (t == null) return null;
        if (t.CoverBytes != null) return t.CoverBytes;
        return t.HasCover ? _storage.GetTrackCover(t.Path) : null;
    }

    private void ApplyAccentFromCover(byte[]? coverBytes)
    {
        // When "accent from cover" is off, use the fixed manual/default accent
        // regardless of the track's artwork.
        var color = AppSettings.AccentFromCover
            ? (ColorExtractor.Extract(coverBytes) ?? ResolveManualAccent())
            : ResolveManualAccent();
        var dim = ColorExtractor.Dim(color);

        // Brushes from <SolidColorBrush x:Key="..."/> get frozen by WPF on resource lookup.
        // Solution: REPLACE the resource entries with fresh mutable brushes, then animate
        // the new brush's Color from the OLD color to the new one. UI uses {DynamicResource}
        // so it picks up the new brush instance immediately and sees the smooth color animation.
        // ResourceDictionary freezes Freezable values on insert (perf optimization).
        // Workaround: replace the brush every frame with a fresh interpolated instance.
        // DynamicResource consumers see the new brush each tick → smooth ~60fps fade.
        StartAccentFade(color, dim);

        var dur = TimeSpan.FromMilliseconds(AccentFadeMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Logo letters use DynamicResource Accent — wave color updates automatically

        // Drop-shadow glow colors — animate smoothly (skip if frozen)
        AnimateEffectColor(BarGlow, color, dur, ease);
        AnimateEffectColor(BarCoverGlow, color, dur, ease);
        AnimateEffectColor(SearchGlow, color, dur, ease);
        if (NowPlayingOverlay?.FindName("VinylShadow") is DropShadowEffect vs) AnimateEffectColor(vs, color, dur, ease);
        if (ProfileOverlay?.FindName("AvatarGlow") is DropShadowEffect ag) AnimateEffectColor(ag, color, dur, ease);
    }

    private static void AnimateEffectColor(DropShadowEffect? eff, Color c, TimeSpan dur, IEasingFunction ease)
    {
        try
        {
            if (eff == null || eff.IsFrozen) return;
            eff.BeginAnimation(DropShadowEffect.ColorProperty,
                new ColorAnimation { To = c, Duration = dur, EasingFunction = ease });
        }
        catch { }
    }

    private bool _bgUseA = true;
    private void UpdateBlurBg(byte[]? coverBytes)
    {
        // Disabled in settings → make sure both layers are hidden and bail.
        if (!AppSettings.BlurBgEnabled)
        {
            var off = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300) };
            BlurBgImg.BeginAnimation(OpacityProperty, off);
            BlurBgImg2.BeginAnimation(OpacityProperty, off);
            return;
        }
        var src = coverBytes != null ? Library.LoadFullCover(coverBytes, 250) : null;
        // HTML setBgCover: crossfade between #bgA and #bgB (target ON, other OFF after 900ms)
        var target = _bgUseA ? BlurBgImg : BlurBgImg2;
        var other = _bgUseA ? BlurBgImg2 : BlurBgImg;
        _bgUseA = !_bgUseA;
        target.Source = src;
        var fadeIn = new DoubleAnimation { To = src != null ? 0.4 : 0, Duration = TimeSpan.FromSeconds(1.5), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        target.BeginAnimation(OpacityProperty, fadeIn);
        var fadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(1.5), BeginTime = TimeSpan.FromMilliseconds(600), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        other.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ScrollToCurrent()
    {
        if (_current == null) return;
        TrackList.UpdateLayout();
        TrackList.ScrollIntoView(_current);
        // Pulse the row with gold flash
        var item = TrackList.ItemContainerGenerator.ContainerFromItem(_current) as ListViewItem;
        if (item != null)
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x33, 0xC8, 0xA9, 0x6E));
            item.Background = brush;
            var anim = new ColorAnimation
            {
                From = Color.FromArgb(0x66, 0xC8, 0xA9, 0x6E),
                To = Colors.Transparent,
                Duration = TimeSpan.FromMilliseconds(900),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }

    // Search focus glow
    private void TxtSearch_Focus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // HTML: .sw:focus-within { border-color:var(--ad); background:var(--s2) }
        SearchBorder.BorderBrush = (Brush)FindResource("AccentDim");
        SearchBorder.Background = (Brush)FindResource("S2");
        var blur = new DoubleAnimation { To = 12, Duration = TimeSpan.FromMilliseconds(180) };
        var op = new DoubleAnimation { To = 0.25, Duration = TimeSpan.FromMilliseconds(180) };
        SearchGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        SearchGlow.BeginAnimation(DropShadowEffect.OpacityProperty, op);
        SearchIcon.Stroke = (Brush)FindResource("Accent");
    }

    private void TxtSearch_Blur(object sender, KeyboardFocusChangedEventArgs e)
    {
        SearchBorder.BorderBrush = (Brush)FindResource("B1");
        SearchBorder.Background = (Brush)FindResource("S1");
        var blur = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180) };
        var op = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180) };
        SearchGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        SearchGlow.BeginAnimation(DropShadowEffect.OpacityProperty, op);
        SearchIcon.Stroke = (Brush)FindResource("T2");
    }

    private void AnimateBarGlow()
    {
        if (AppSettings.ReduceMotion) return;
        var blurUp = new DoubleAnimation { To = 28, Duration = TimeSpan.FromMilliseconds(600), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var opUp = new DoubleAnimation { To = 0.4, Duration = TimeSpan.FromMilliseconds(600), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var blurDown = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(1400), BeginTime = TimeSpan.FromMilliseconds(600), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var opDown = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(1400), BeginTime = TimeSpan.FromMilliseconds(600), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var sb = new Storyboard();
        Storyboard.SetTarget(blurUp, BarGlow); Storyboard.SetTargetProperty(blurUp, new PropertyPath(DropShadowEffect.BlurRadiusProperty));
        Storyboard.SetTarget(opUp, BarGlow); Storyboard.SetTargetProperty(opUp, new PropertyPath(DropShadowEffect.OpacityProperty));
        Storyboard.SetTarget(blurDown, BarGlow); Storyboard.SetTargetProperty(blurDown, new PropertyPath(DropShadowEffect.BlurRadiusProperty));
        Storyboard.SetTarget(opDown, BarGlow); Storyboard.SetTargetProperty(opDown, new PropertyPath(DropShadowEffect.OpacityProperty));
        sb.Children.Add(blurUp); sb.Children.Add(opUp); sb.Children.Add(blurDown); sb.Children.Add(opDown);
        sb.Begin();
    }

    // HTML @keyframes artBorder: box-shadow 0→6px gold ring then fade out (.7s)
    private void PulseArtBorder()
    {
        if (AppSettings.ReduceMotion) return;
        var blur = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
        blur.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        blur.KeyFrames.Add(new EasingDoubleKeyFrame(12, KeyTime.FromPercent(0.6)));
        blur.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        var op = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
        op.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromPercent(0)));
        op.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1)));
        BarCoverGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        BarCoverGlow.BeginAnimation(DropShadowEffect.OpacityProperty, op);
    }

    // HTML @keyframes rowPulse: row bg fades from rgba(200,169,110,.2) → S1 (.9s)
    private void PulseCurrentRow()
    {
        if (_current == null || AppSettings.ReduceMotion) return;
        // Defer to allow container generation after ItemsSource changes
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var container = TrackList.ItemContainerGenerator.ContainerFromItem(_current) as ListViewItem;
            if (container == null) return;
            var border = FindFirstChild<Border>(container);
            if (border == null) return;

            var brush = new SolidColorBrush(Color.FromArgb(0x33, 0xC8, 0xA9, 0x6E));
            border.Background = brush;
            var anim = new ColorAnimation
            {
                To = Color.FromArgb(0, 0xC8, 0xA9, 0x6E),       // fade to fully transparent
                Duration = TimeSpan.FromMilliseconds(900),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            // CRITICAL: ClearValue (not set to "orig") so DataTrigger.Setter (IsCurrent→S1) can win again.
            // Otherwise local Background sticks forever on recycled containers (the dark band bug).
            anim.Completed += (_, _) => border.ClearValue(Border.BackgroundProperty);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static T? FindFirstChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (c is T tc) return tc;
            var inner = FindFirstChild<T>(c);
            if (inner != null) return inner;
        }
        return null;
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        SpawnRipple(BtnPlay);
        if (_current == null)
        {
            if (_visible.Count > 0) PlayTrack(_visible[0]);
            return;
        }
        _audio.TogglePlay();
        UpdatePlayButton();
    }

    private void SpawnRipple(Button btn)
    {
        if (AppSettings.ReduceMotion) return;
        // Use the Grid inside button template to overlay ripple
        if (VisualTreeHelper.GetChildrenCount(btn) == 0) return;
        var content = btn.Template?.FindName("el", btn);
        var grid = (btn.Content as FrameworkElement)?.Parent as Grid
                   ?? VisualTreeHelper.GetChild(btn, 0) as Grid;
        if (grid == null) return;
        var ripple = new Ellipse
        {
            Width = btn.ActualWidth,
            Height = btn.ActualHeight,
            Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var st = new ScaleTransform(0, 0);
        ripple.RenderTransform = st;
        grid.Children.Add(ripple);
        var sx = new DoubleAnimation { To = 2.8, Duration = TimeSpan.FromMilliseconds(550), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var op = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(550), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        op.Completed += (_, _) => grid.Children.Remove(ripple);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        ripple.BeginAnimation(OpacityProperty, op);
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_allTracks.Count == 0) return;
        if (_navHistory.Count >= 2)
        {
            _navHistory.RemoveAt(_navHistory.Count - 1);
            var prev = _navHistory[^1];
            _isPrevNav = true;
            PlayTrack(prev);
        }
        else if (_current != null)
        {
            _isPrevNav = true;
            PlayTrack(_current);
        }
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e) => NextTrack();

    private void NextTrack()
    {
        // The user's explicit "play next" / queue always wins.
        if (_queue.Count > 0)
        {
            var next = _queue[0];
            _queue.RemoveAt(0);
            next.IsInQueue = false;
            RefreshQueueUi();
            PlayTrack(next);
            return;
        }
        var ctx = CurrentContext();
        if (ctx.Count == 0) return;
        Track t;
        if (_shuffle)
        {
            t = PickShuffle(ctx);
        }
        else
        {
            int idx = _current != null ? ctx.IndexOf(_current) : -1;
            t = idx >= 0 ? ctx[(idx + 1) % ctx.Count] : ctx[0]; // wrap → loops the context
        }
        PlayTrack(t);
    }

    /// <summary>The active playback set: the playlist/search results being played, or the library.</summary>
    private System.Collections.Generic.IList<Track> CurrentContext()
        => _playContext.Count > 0 ? _playContext : _allTracks;

    /// <summary>Capture the currently visible list as the playback context (call on a user-initiated play).</summary>
    private void SetPlaybackContext()
    {
        _playContext.Clear();
        _playContext.AddRange(_visible);
    }

    private static string TrackKey(Track t) => ((t.Title ?? "") + "|" + (t.Artist ?? "")).Trim().ToLowerInvariant();

    /// <summary>Pick a random track from the context, avoiding recently played ones AND
    /// near-duplicates (same title+artist) so shuffle doesn't repeat the same song.</summary>
    private Track PickShuffle(System.Collections.Generic.IList<Track> ctx)
    {
        var recent = new System.Collections.Generic.HashSet<string>(_shuffleHistory);
        var pool = new System.Collections.Generic.List<Track>();
        foreach (var t in ctx)
            if (t != _current && !recent.Contains(TrackKey(t))) pool.Add(t);
        if (pool.Count == 0)
            foreach (var t in ctx) if (t != _current) pool.Add(t); // everything's recent → relax
        if (pool.Count == 0) return _current ?? ctx[0];
        return pool[_rng.Next(pool.Count)];
    }

    /// <summary>Record a played track for shuffle anti-repeat (keeps a recent window).</summary>
    private void RememberPlayed(Track t)
    {
        var key = TrackKey(t);
        _shuffleHistory.Remove(key);
        _shuffleHistory.Add(key);
        int ctxSize = _playContext.Count > 0 ? _playContext.Count : _allTracks.Count;
        int cap = Math.Max(1, Math.Min(ctxSize - 1, 30)); // always leave at least 1 choice
        while (_shuffleHistory.Count > cap) _shuffleHistory.RemoveAt(0);
    }

    /// <summary>Would NextTrack actually have something to play (for crossfade pre-advance)?</summary>
    private bool HasNextForCrossfade() => _queue.Count > 0 || CurrentContext().Count > 0;

    private void OnTrackEnded()
    {
        Dispatcher.Invoke(() =>
        {
            if (_repeat == RepeatMode.One && _current != null) { PlayTrack(_current); return; }
            // Auto-advance: queue → context (shuffle or sequential, wrapping so the
            // playlist/library loops instead of stopping).
            NextTrack();
        });
    }

    private void BtnShuffle_Click(object sender, RoutedEventArgs e)
    {
        _shuffle = !_shuffle;
        _storage.SetSetting("shuffle", _shuffle ? "1" : "0");
        UpdateShuffleVisual();
    }

    private void UpdateShuffleVisual()
    {
        BtnShuffle.BeginAnimation(OpacityProperty, null);
        if (_shuffle)
        {
            BtnShuffle.SetResourceReference(ForegroundProperty, "Accent");
            BtnShuffle.Opacity = 1.0;
        }
        else
        {
            BtnShuffle.Foreground = (Brush)FindResource("T1");
            BtnShuffle.Opacity = BtnShuffle.IsMouseOver ? 1.0 : 0.65;
        }
        _tray?.UpdateModes(_shuffle, _repeat == RepeatMode.One);
    }

    // Smooth opacity transition on hover (HTML: .cb { transition: color 150ms })
    private void CtrlBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button b && !IsActiveCtrl(b))
            b.BeginAnimation(OpacityProperty, new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(150) });
    }
    private void CtrlBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button b && !IsActiveCtrl(b))
            b.BeginAnimation(OpacityProperty, new DoubleAnimation { To = 0.65, Duration = TimeSpan.FromMilliseconds(150) });
    }
    // Play button hover: animate inner ellipse fill from white → current accent → back
    private void BtnPlay_MouseEnter(object sender, MouseEventArgs e) => AnimatePlayFill(true);
    private void BtnPlay_MouseLeave(object sender, MouseEventArgs e) => AnimatePlayFill(false);
    private void AnimatePlayFill(bool hovered)
    {
        if (BtnPlay.Template.FindName("playFill", BtnPlay) is not SolidColorBrush fill) return;
        var target = hovered
            ? ((SolidColorBrush)Application.Current.Resources["Accent"]).Color
            : Color.FromRgb(0xE8, 0xE4, 0xDC);
        fill.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation { To = target, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private bool IsActiveCtrl(Button b)
        => (b == BtnShuffle && _shuffle)
           || (b == BtnRepeat && _repeat == RepeatMode.One)
           || (b == BtnMute   && _audio.IsMuted);

    private void BtnRepeat_Click(object sender, RoutedEventArgs e)
    {
        _repeat = _repeat == RepeatMode.One ? RepeatMode.Off : RepeatMode.One;
        _storage.SetSetting("repeat", _repeat == RepeatMode.One ? "one" : "off");
        UpdateRepeatVisual();
    }

    private void UpdateRepeatVisual()
    {
        BtnRepeat.BeginAnimation(OpacityProperty, null);
        // Active: accent color via DynamicResource + full opacity
        // Inactive: white (T1) + dim opacity
        if (_repeat == RepeatMode.One)
        {
            BtnRepeat.SetResourceReference(ForegroundProperty, "Accent");
            BtnRepeat.Opacity = 1.0;
        }
        else
        {
            BtnRepeat.Foreground = (Brush)FindResource("T1");
            BtnRepeat.Opacity = BtnRepeat.IsMouseOver ? 1.0 : 0.65;
        }
        _tray?.UpdateModes(_shuffle, _repeat == RepeatMode.One);
    }

    private void UpdatePlayButton()
    {
        PathPlay.Data = _audio.IsPlaying
            ? (Geometry)FindResource("PauseGeo")
            : (Geometry)FindResource("PlayGeo");
        if (_audio.IsPlaying) StartLogoPulse();
        else StopLogoPulse();
        // Mirror state into the tray icon so its menu reflects play/pause correctly.
        _tray?.UpdateTrack(_current?.Title, _current?.Artist, _current?.Cover, _audio.IsPlaying);
        // Update IsCurrentPlaying on current track for eq bars animation
        if (_current != null) _current.IsCurrentPlaying = _audio.IsPlaying;
        // Floating bg fades in only while playing
        // HTML: body.playing::before { opacity: 1 } but body.has-cover::before { opacity: .12 !important }
        bool hasCover = _current?.HasCover == true || _current?.CoverBytes != null;
        FloatBgCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = _audio.IsPlaying ? (hasCover ? 0.12 : 1.0) : 0.0,
            Duration = TimeSpan.FromMilliseconds(1200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void UpdateBarMeta()
    {
        // Sync tray icon tooltip + popup header on every meta change.
        _tray?.UpdateTrack(_current?.Title, _current?.Artist, _current?.Cover, _audio.IsPlaying);
        if (_current == null)
        {
            TxtBarTitle.Text = Localization.T("NoTrack");
            TxtBarArtist.Text = Localization.T("PickFromPlaylist");
            ImgBarCover.Source = null;
            BarCoverNote.Visibility = Visibility.Visible;
            TxtTimeCur.Text = "0:00";
            TxtTimeTot.Text = "0:00";
            SldProgress.Value = 0;
            return;
        }

        // Smooth fade out → swap → fade in
        var fadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) =>
        {
            TxtBarTitle.Text = _current.Title;
            TxtBarArtist.Text = string.IsNullOrEmpty(_current.Album)
                ? _current.Artist : $"{_current.Artist} · {_current.Album}";
            var barCover = CoverBytesFor(_current);
            ImgBarCover.Source = barCover != null
                ? Library.LoadFullCover(barCover, 200) : null;
            BarCoverNote.Visibility = barCover != null ? Visibility.Collapsed : Visibility.Visible;
            TxtTimeTot.Text = FmtTime(_current.Duration);

            // Art scale entrance (0.84 → 1.03 → 1)
            var artScale = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
            artScale.KeyFrames.Add(new EasingDoubleKeyFrame(0.84, KeyTime.FromPercent(0)));
            artScale.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromPercent(0.6)));
            artScale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            BarCoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, artScale.Clone());
            BarCoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, artScale);

            // Title slide-in from below
            BarMetaTrans.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = 8, To = 0, Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ImgBarCover.BeginAnimation(OpacityProperty, fadeIn);
            BarMetaPanel.BeginAnimation(OpacityProperty, fadeIn);
        };
        ImgBarCover.BeginAnimation(OpacityProperty, fadeOut);
        BarMetaPanel.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Timer / progress / history
    // ─────────────────────────────────────────────────────────────────────
    private void OnTick(object? sender, EventArgs e)
    {
        if (_current == null) return;

        // Record to history after 10s
        if (!_historyRecordedForCurrent && _audio.IsPlaying && _audio.Position.TotalSeconds >= 10)
        {
            _historyRecordedForCurrent = true;
            _storage.PushHistory(_current);
            RefreshRecent();
        }

        if (_seeking) return;
        var dur = _audio.Duration.TotalSeconds;
        if (dur <= 0) return;
        var pos = _audio.Position.TotalSeconds;
        var fraction = pos / dur;

        // Crossfade: advance early so the next track fades in over the current tail.
        int xf = AppSettings.CrossfadeSeconds;
        if (xf > 0 && !_crossfadeArmed && _audio.IsPlaying && _repeat != RepeatMode.One
            && (dur - pos) <= xf && (dur - pos) > 0.25 && HasNextForCrossfade())
        {
            _crossfadeArmed = true;
            NextTrack();   // PlayTrack crossfades because audio is still playing
            return;
        }
        // While paused the position can't move on its own — once we've drawn the
        // current spot, skip the per-tick slider re-animation and label churn.
        if (!_audio.IsPlaying && Math.Abs(pos - _lastProgressPos) < 0.05) return;
        _lastProgressPos = pos;

        // HTML: .pf { transition: width .25s linear } — smooth fill instead of jumpy 250 ms ticks
        SldProgress.BeginAnimation(System.Windows.Controls.Slider.ValueProperty, new DoubleAnimation
        {
            To = fraction, Duration = TimeSpan.FromMilliseconds(250)
        }, HandoffBehavior.SnapshotAndReplace);
        TxtTimeCur.Text = FmtTime(_audio.Position);
        UpdatePlayButton();
        if (_npOpen) NowPlayingOverlay.UpdateProgress(fraction, _audio.Position);
    }

    private void SldProgress_DragStarted(object sender, DragStartedEventArgs e)
    {
        _seeking = true;
        // Snapshot animated value to local BEFORE clearing animation, else slider flickers to 0
        var v = SldProgress.Value;
        SldProgress.BeginAnimation(System.Windows.Controls.Slider.ValueProperty, null);
        SldProgress.Value = v;
    }

    private void SldProgress_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _audio.Seek(SldProgress.Value);
        _seeking = false;
    }

    // Click on track (not thumb): snapshot animated value to local before clearing animation
    private void SldProgress_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
        var v = SldProgress.Value;
        SldProgress.BeginAnimation(System.Windows.Controls.Slider.ValueProperty, null);
        SldProgress.Value = v;
    }

    private void SldProgress_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb) return;
        // Slider's value is already at clicked position (SliderBehavior.OnDown set it)
        _audio.Seek(SldProgress.Value);
        _seeking = false;
    }

    private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _audio.Volume = (float)e.NewValue;
        _storage.SetSetting("volume", _audio.Volume.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        _tray?.UpdateVolume(_audio.Volume);
    }
}

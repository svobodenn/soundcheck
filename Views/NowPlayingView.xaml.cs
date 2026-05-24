using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SoundCheck.Views;

public partial class NowPlayingView : UserControl
{
    public event Action? Closed;
    public event Action? PlayPause;
    public event Action? Prev;
    public event Action? Next;
    public event Action? ShuffleToggle;
    public event Action? RepeatToggle;
    public event Action<double>? Seek;
    public event Action? ResetStatsRequested;

    private Storyboard? _pulseStoryboard;
    private bool _seeking;

    public NowPlayingView() { InitializeComponent(); }

    public void UpdateMeta(string title, string artist, BitmapImage? cover, BitmapImage? bg, TimeSpan duration)
    {
        TxtTitle.Text = title;
        // HTML: text-transform: uppercase; letter-spacing: .1em — emulate via uppercase + thin spaces
        TxtArtist.Text = string.IsNullOrEmpty(artist) ? "" : SpaceLetters(artist.ToUpperInvariant());
        // HTML: hide #npNote when img has src
        VinylNote.Visibility = cover != null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        VinylImg.Source = cover;
        BgImg.Source = bg;
        TxtTot.Text = FmtTime(duration);
    }

    public void AnimateIn()
    {
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sy = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        NPScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        NPScale.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        NPTrans.BeginAnimation(TranslateTransform.YProperty, sy);
    }
    public void AnimateOut(Action onDone)
    {
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var sx = new DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(220) };
        var sy = new DoubleAnimation { To = 24, Duration = TimeSpan.FromMilliseconds(220) };
        fade.Completed += (_, _) => onDone();
        BeginAnimation(OpacityProperty, fade);
        NPScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        NPScale.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        NPTrans.BeginAnimation(TranslateTransform.YProperty, sy);
    }

    public void UpdateProgress(double fraction, TimeSpan position)
    {
        if (_seeking) return;
        // HTML: .np-pf { transition: width .25s linear } — smooth progress fill
        SldProg.BeginAnimation(System.Windows.Controls.Slider.ValueProperty, new DoubleAnimation
        {
            To = fraction, Duration = TimeSpan.FromMilliseconds(250)
        }, HandoffBehavior.SnapshotAndReplace);
        TxtCur.Text = FmtTime(position);
    }

    public void UpdatePlaying(bool playing)
    {
        PathPlay.Data = playing
            ? (Geometry)FindResource("PauseGeo")
            : (Geometry)FindResource("PlayGeo");

        if (playing)
        {
            // Box-shadow pulse (HTML npPulse: 40px→60px blur, opacity .08→.18)
            if (_pulseStoryboard == null)
            {
                var blur = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4), RepeatBehavior = RepeatBehavior.Forever };
                blur.KeyFrames.Add(new EasingDoubleKeyFrame(40, KeyTime.FromPercent(0)));
                blur.KeyFrames.Add(new EasingDoubleKeyFrame(60, KeyTime.FromPercent(0.5)));
                blur.KeyFrames.Add(new EasingDoubleKeyFrame(40, KeyTime.FromPercent(1)));
                var op = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4), RepeatBehavior = RepeatBehavior.Forever };
                op.KeyFrames.Add(new EasingDoubleKeyFrame(0.08, KeyTime.FromPercent(0)));
                op.KeyFrames.Add(new EasingDoubleKeyFrame(0.18, KeyTime.FromPercent(0.5)));
                op.KeyFrames.Add(new EasingDoubleKeyFrame(0.08, KeyTime.FromPercent(1)));
                Storyboard.SetTarget(blur, VinylShadow); Storyboard.SetTargetProperty(blur, new PropertyPath(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty));
                Storyboard.SetTarget(op, VinylShadow); Storyboard.SetTargetProperty(op, new PropertyPath(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty));
                _pulseStoryboard = new Storyboard();
                _pulseStoryboard.Children.Add(blur);
                _pulseStoryboard.Children.Add(op);
                _pulseStoryboard.Begin(this, true);
            }
            else
            {
                _pulseStoryboard.Resume(this);
            }
        }
        else
        {
            _pulseStoryboard?.Pause(this);
        }
    }

    // ─── Live spectrum ────────────────────────────────────────────────────
    private System.Windows.Shapes.Rectangle[]? _spectrumBars;
    private const int SpectrumBands = 28;
    private const double SpectrumMaxH = 46;

    /// <summary>Update the spectrum bars from FFT magnitudes (0..1).</summary>
    public void UpdateSpectrum(float[] mags)
    {
        if (_spectrumBars == null)
        {
            SpectrumHost.Children.Clear();
            _spectrumBars = new System.Windows.Shapes.Rectangle[SpectrumBands];
            for (int i = 0; i < SpectrumBands; i++)
            {
                var r = new System.Windows.Shapes.Rectangle
                {
                    Width = 4,
                    Height = 2,
                    RadiusX = 2,
                    RadiusY = 2,
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
                _spectrumBars[i] = r;
                SpectrumHost.Children.Add(r);
            }
        }
        int n = Math.Min(mags.Length, SpectrumBands);
        for (int i = 0; i < n; i++)
            _spectrumBars[i].Height = 2 + Math.Clamp(mags[i], 0f, 1f) * SpectrumMaxH;
    }

    /// <summary>Collapse all bars to the baseline (when paused/closed).</summary>
    public void ClearSpectrum()
    {
        if (_spectrumBars == null) return;
        foreach (var b in _spectrumBars) b.Height = 2;
    }

    public void UpdateShuffle(bool shuffle)
    {
        BtnShuffle.Foreground = shuffle ? (Brush)FindResource("Accent") : (Brush)FindResource("T2");
    }

    public void UpdateRepeat(bool one)
    {
        BtnRepeat.Foreground = one ? (Brush)FindResource("Accent") : (Brush)FindResource("T2");
        PathRepeat.Data = one
            ? (Geometry)FindResource("RepeatOneOuterGeo")
            : (Geometry)FindResource("RepeatGeo");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
    private void BtnResetStats_Click(object sender, RoutedEventArgs e) => ResetStatsRequested?.Invoke();
    private void BtnPlay_Click(object sender, RoutedEventArgs e) => PlayPause?.Invoke();
    private void BtnPrev_Click(object sender, RoutedEventArgs e) => Prev?.Invoke();
    private void BtnNext_Click(object sender, RoutedEventArgs e) => Next?.Invoke();
    private void BtnShuffle_Click(object sender, RoutedEventArgs e) => ShuffleToggle?.Invoke();
    private void BtnRepeat_Click(object sender, RoutedEventArgs e) => RepeatToggle?.Invoke();

    private void SldProg_DragStarted(object sender, DragStartedEventArgs e)
    {
        _seeking = true;
        SldProg.BeginAnimation(System.Windows.Controls.Slider.ValueProperty, null);
    }
    private void SldProg_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Seek?.Invoke(SldProg.Value);
        _seeking = false;
    }
    private void SldProg_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb) return;
        Seek?.Invoke(SldProg.Value);
    }

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();

    // Clicks on the central card are swallowed so they don't reach the backdrop.
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static string SpaceLetters(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i < s.Length - 1 && s[i] != ' ' && s[i + 1] != ' ')
                sb.Append(' '); // thin space ≈ .1em
        }
        return sb.ToString();
    }

    private static string FmtTime(TimeSpan t) =>
        t.TotalSeconds < 0 || double.IsNaN(t.TotalSeconds) ? "0:00"
            : t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
}

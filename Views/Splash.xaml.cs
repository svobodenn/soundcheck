using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SoundCheck.Views;

public partial class Splash : UserControl
{
    public event Action? Finished;

    private const string LogoText = "soundcheck";
    private const int VisibleMs = 1300; // total visible time before fade-out
    private const int FadeMs = 380;

    public Splash()
    {
        InitializeComponent();
        BuildLetters();
        Loaded += OnLoaded;
    }

    private void BuildLetters()
    {
        // Pre-render each glyph with opacity 0 + tiny upward offset; animate in
        // staggered. Uses our mono font for consistency with the player chrome.
        foreach (var ch in LogoText)
        {
            var tb = new TextBlock
            {
                Text = ch.ToString(),
                FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
                FontSize = 22,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["T1"],
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TranslateTransform(0, 6),
            };
            LetterHost.Children.Add(tb);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnimateEqualizerBars();
        AnimateLettersIn();
        AnimateProgressLine();
        ScheduleFadeOut();
    }

    private void AnimateEqualizerBars()
    {
        // Gentle independent breathing for each bar — very subtle (0.4 → 1.0 → 0.4 with varying phases).
        var bars = new[] { Bar1Scale, Bar2Scale, Bar3Scale, Bar4Scale, Bar5Scale };
        double[] phases = { 0.0, 0.18, 0.35, 0.55, 0.70 };
        for (int i = 0; i < bars.Length; i++)
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(1400),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(phases[i] * 1400),
            };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.4, KeyTime.FromPercent(0),    new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0.5),  new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.4, KeyTime.FromPercent(1.0),  new SineEase { EasingMode = EasingMode.EaseInOut }));
            bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
    }

    private void AnimateLettersIn()
    {
        // Each letter: opacity 0→1 + translateY 6→0, staggered by 55 ms.
        for (int i = 0; i < LetterHost.Children.Count; i++)
        {
            if (LetterHost.Children[i] is not TextBlock tb) continue;
            var beginAt = TimeSpan.FromMilliseconds(i * 55);

            var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(340), BeginTime = beginAt, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            tb.BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(420), BeginTime = beginAt, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ((TranslateTransform)tb.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    private void AnimateProgressLine()
    {
        // Thin gold line that grows from 0 → 60 px under the title.
        var grow = new DoubleAnimation
        {
            From = 0, To = 60,
            Duration = TimeSpan.FromMilliseconds(VisibleMs - 200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        ProgressLine.BeginAnimation(WidthProperty, grow);
    }

    private void ScheduleFadeOut()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VisibleMs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(FadeMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            fade.Completed += (_, _) => Finished?.Invoke();
            BeginAnimation(OpacityProperty, fade);
        };
        t.Start();
    }
}

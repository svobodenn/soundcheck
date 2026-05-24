using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SoundCheck.Services;

namespace SoundCheck;

// Ambient visual effects: floating particles + the animated "soundcheck" logo
// (shimmer wave + equalizer-bar pulse). Split out of MainWindow.xaml.cs.
public partial class MainWindow
{
    // ─────────────────────────────────────────────────────────────────────
    // Particles
    // ─────────────────────────────────────────────────────────────────────
    private void InitParticles()
    {
        ParticlesCanvas.Children.Clear();
        _particles.Clear();
        var accent = ((SolidColorBrush)FindResource("Accent")).Color;
        for (int i = 0; i < 48; i++)
        {
            var size = _rng.NextDouble() * 2.4 + 1.2;
            var el = new Ellipse
            {
                Width = size, Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(
                    (byte)(_rng.NextDouble() * 130 + 60), accent.R, accent.G, accent.B)),
            };
            Canvas.SetLeft(el, _rng.NextDouble() * Math.Max(800, ActualWidth));
            Canvas.SetTop(el, _rng.NextDouble() * Math.Max(600, ActualHeight));
            ParticlesCanvas.Children.Add(el);
            var vx = (_rng.NextDouble() - 0.5) * 0.4;
            var vy = (_rng.NextDouble() - 0.7) * 0.55;
            _particles.Add((el, vx, vy));
        }
    }

    private double _lastPartW, _lastPartH;
    private void OnParticlesTick(object? sender, EventArgs e)
    {
        var w = ParticlesCanvas.ActualWidth;
        var h = ParticlesCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Re-initialize particles if window size changed significantly
        if (Math.Abs(w - _lastPartW) > 50 || Math.Abs(h - _lastPartH) > 50)
        {
            _lastPartW = w; _lastPartH = h;
            // Reposition particles to be within bounds
            foreach (var p in _particles)
            {
                var x = Canvas.GetLeft(p.el);
                var y = Canvas.GetTop(p.el);
                if (double.IsNaN(x) || x < 0 || x > w) Canvas.SetLeft(p.el, _rng.NextDouble() * w);
                if (double.IsNaN(y) || y < 0 || y > h) Canvas.SetTop(p.el, _rng.NextDouble() * h);
            }
        }

        bool playing = _audio.IsPlaying;
        // Animate opacity for canvas
        var target = playing ? 0.45 : 0;
        if (Math.Abs(ParticlesCanvas.Opacity - target) > 0.01)
            ParticlesCanvas.Opacity += (target - ParticlesCanvas.Opacity) * 0.05;

        if (!playing) return;

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            var x = Canvas.GetLeft(p.el) + p.vx;
            var y = Canvas.GetTop(p.el) + p.vy;
            if (y < -10) { y = h + 10; x = _rng.NextDouble() * w; }
            if (x < -10) x = w + 10;
            else if (x > w + 10) x = -10;
            Canvas.SetLeft(p.el, x);
            Canvas.SetTop(p.el, y);
        }
    }

    // SOUNDCHECK letters: always in accent color (track tint).
    // A subtle white shimmer (separate overlay layer) sweeps across as a wave. NO scaling.
    private void BuildLogoLettersWave()
    {
        const string text = "soundcheck";
        const double cycleSec = 3.0;
        for (int i = 0; i < text.Length; i++)
        {
            // Container Grid so the white overlay sits on top of the accent letter.
            // Lowercase glyphs are tighter — no inter-letter margin needed.
            var g = new Grid { Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            // Base letter in accent color (DynamicResource → updates with track)
            var baseTb = new TextBlock
            {
                Text = text[i].ToString(),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                FontSize = 17,
                FontWeight = FontWeights.Medium,
            };
            baseTb.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            // White shimmer overlay (T1) — opacity oscillates 0 → 0.6 → 0 as the wave passes
            var glow = new TextBlock
            {
                Text = text[i].ToString(),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                FontSize = 17,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("T1"),
                Opacity = 0,
            };
            g.Children.Add(baseTb);
            g.Children.Add(glow);
            LogoLetters.Children.Add(g);

            double delay = i * 0.10;     // 100 ms stagger between letters → wave from left
            var op = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(cycleSec),
                BeginTime = TimeSpan.FromSeconds(delay),
                RepeatBehavior = RepeatBehavior.Forever
            };
            op.KeyFrames.Add(new EasingDoubleKeyFrame(0.00, KeyTime.FromPercent(0))    { EasingFunction = new SineEase{EasingMode=EasingMode.EaseInOut} });
            op.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, KeyTime.FromPercent(0.15)) { EasingFunction = new SineEase{EasingMode=EasingMode.EaseInOut} });
            op.KeyFrames.Add(new EasingDoubleKeyFrame(0.00, KeyTime.FromPercent(0.30)) { EasingFunction = new SineEase{EasingMode=EasingMode.EaseInOut} });
            op.KeyFrames.Add(new EasingDoubleKeyFrame(0.00, KeyTime.FromPercent(1))    { EasingFunction = new SineEase{EasingMode=EasingMode.EaseInOut} });
            glow.BeginAnimation(OpacityProperty, op);
        }
    }

    private void StartLogoPulse()
    {
        if (!AppSettings.LogoEqualizerEnabled || AppSettings.ReduceMotion) return;
        if (_logoPulse != null) return;
        _logoPulse = new Storyboard();
        // 5 bars, each scales ScaleY independently between two values, different durations,
        // staggered begin times → looks like a live audio spectrum
        AddLogoBarAnim(Logo1Scale, 0.35, 0.95, 0.62, 0.00);
        AddLogoBarAnim(Logo2Scale, 0.50, 1.00, 0.55, 0.10);
        AddLogoBarAnim(Logo3Scale, 0.30, 0.85, 0.48, 0.20);
        AddLogoBarAnim(Logo4Scale, 0.55, 1.00, 0.66, 0.05);
        AddLogoBarAnim(Logo5Scale, 0.25, 0.80, 0.58, 0.18);
        _logoPulse.Begin();
    }
    private void AddLogoBarAnim(ScaleTransform t, double from, double to, double durSec, double delaySec)
    {
        var a = new DoubleAnimation
        {
            From = from, To = to,
            Duration = TimeSpan.FromSeconds(durSec),
            BeginTime = TimeSpan.FromSeconds(delaySec),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(a, t);
        Storyboard.SetTargetProperty(a, new PropertyPath(ScaleTransform.ScaleYProperty));
        _logoPulse!.Children.Add(a);
    }

    private void StopLogoPulse()
    {
        _logoPulse?.Stop();
        _logoPulse = null;
        // Reset to subtle idle heights
        Logo1Scale.ScaleY = 0.45;
        Logo2Scale.ScaleY = 0.85;
        Logo3Scale.ScaleY = 0.65;
        Logo4Scale.ScaleY = 0.95;
        Logo5Scale.ScaleY = 0.40;
    }
}

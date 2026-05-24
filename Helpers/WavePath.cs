using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SoundCheck.Helpers;

/// <summary>
/// Generates a horizontal sine-wave Geometry for the played-portion of a slider.
/// Attached to a Path — listens to width changes of a target element and regenerates.
/// </summary>
public static class WavePath
{
    public static readonly DependencyProperty TrackWidthSourceProperty =
        DependencyProperty.RegisterAttached("TrackWidthSource", typeof(FrameworkElement), typeof(WavePath),
            new PropertyMetadata(null, OnSourceChanged));
    public static void SetTrackWidthSource(DependencyObject d, FrameworkElement v) => d.SetValue(TrackWidthSourceProperty, v);
    public static FrameworkElement? GetTrackWidthSource(DependencyObject d) => d.GetValue(TrackWidthSourceProperty) as FrameworkElement;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Path path) return;
        if (e.OldValue is FrameworkElement oldFe) oldFe.SizeChanged -= (s, _) => Rebuild(path, (FrameworkElement)s);
        if (e.NewValue is FrameworkElement newFe)
        {
            newFe.SizeChanged += (s, _) => Rebuild(path, (FrameworkElement)s);
            path.Loaded += (_, _) => Rebuild(path, newFe);
        }
    }

    private static void Rebuild(Path path, FrameworkElement src)
    {
        double w = src.ActualWidth;
        if (w <= 0) return;
        // Wider, gentler waves — period 22px, amplitude 4px → calmer feel
        path.Data = BuildSineWave(w, amplitude: 4.0, period: 22.0);
    }

    /// <summary>Smooth sine-wave geometry using cubic bezier humps.</summary>
    public static Geometry BuildSineWave(double width, double amplitude = 3.0, double period = 14.0)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            // Vertical center = 0; wave oscillates between -amplitude and +amplitude
            ctx.BeginFigure(new Point(0, 0), false, false);
            double x = 0;
            bool up = true;
            while (x < width)
            {
                double nextX = Math.Min(x + period / 2, width);
                double peakY = up ? -amplitude : amplitude;
                // Single bezier hump from current point to next (period/2 wide)
                ctx.BezierTo(
                    new Point(x + (nextX - x) / 3,     peakY),
                    new Point(x + 2 * (nextX - x) / 3, peakY),
                    new Point(nextX, 0),
                    isStroked: true, isSmoothJoin: true);
                x = nextX;
                up = !up;
            }
        }
        geom.Freeze();
        return geom;
    }
}

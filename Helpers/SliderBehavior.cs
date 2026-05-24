using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SoundCheck.Helpers;

/// <summary>
/// Attach to a Slider to make ANY click (track or thumb) jump to that position
/// and support drag without releasing. Bypasses WPF's IsMoveToPointEnabled quirks.
/// </summary>
public static class SliderBehavior
{
    public static readonly DependencyProperty ClickToSeekProperty =
        DependencyProperty.RegisterAttached("ClickToSeek", typeof(bool), typeof(SliderBehavior),
            new PropertyMetadata(false, OnClickToSeekChanged));
    public static void SetClickToSeek(DependencyObject d, bool v) => d.SetValue(ClickToSeekProperty, v);
    public static bool GetClickToSeek(DependencyObject d) => (bool)d.GetValue(ClickToSeekProperty);

    private static void OnClickToSeekChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider s) return;
        if ((bool)e.NewValue)
        {
            s.PreviewMouseLeftButtonDown += OnDown;
            s.PreviewMouseMove           += OnMove;
            s.PreviewMouseLeftButtonUp   += OnUp;
            s.LostMouseCapture           += OnLost;
        }
        else
        {
            s.PreviewMouseLeftButtonDown -= OnDown;
            s.PreviewMouseMove           -= OnMove;
            s.PreviewMouseLeftButtonUp   -= OnUp;
            s.LostMouseCapture           -= OnLost;
        }
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var s = (Slider)sender;
        s.CaptureMouse();
        UpdateFromPos(s, e.GetPosition(s));
        // Do NOT set e.Handled so outer PreviewMouseDown handlers (eg progress _seeking flag) still fire
    }
    private static void OnMove(object sender, MouseEventArgs e)
    {
        var s = (Slider)sender;
        if (s.IsMouseCaptured) UpdateFromPos(s, e.GetPosition(s));
    }
    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var s = (Slider)sender;
        if (s.IsMouseCaptured) s.ReleaseMouseCapture();
    }
    private static void OnLost(object sender, MouseEventArgs e) { /* nothing */ }

    private static void UpdateFromPos(Slider s, Point p)
    {
        double w = s.ActualWidth;
        if (w <= 0) return;
        double frac = Math.Clamp(p.X / w, 0.0, 1.0);
        double val = s.Minimum + frac * (s.Maximum - s.Minimum);
        s.SetCurrentValue(Slider.ValueProperty, val);
    }
}

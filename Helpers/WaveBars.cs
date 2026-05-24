using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SoundCheck.Helpers;

/// <summary>
/// Attached property — when target Slider's ActualWidth changes, fills the ItemsControl
/// with a list of bar heights forming a smooth wave (like a calm equalizer).
/// </summary>
public static class WaveBars
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached("Source", typeof(FrameworkElement), typeof(WaveBars),
            new PropertyMetadata(null, OnSourceChanged));
    public static void SetSource(DependencyObject d, FrameworkElement v) => d.SetValue(SourceProperty, v);
    public static FrameworkElement? GetSource(DependencyObject d) => d.GetValue(SourceProperty) as FrameworkElement;

    private const double BarStride = 5;       // px (3 width + 2 margin)
    private const double MinH = 3;
    private const double MaxH = 11;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl host) return;
        if (e.NewValue is FrameworkElement fe)
        {
            fe.SizeChanged += (_, _) => Rebuild(host, fe);
            host.Loaded += (_, _) => Rebuild(host, fe);
        }
    }

    private static void Rebuild(ItemsControl host, FrameworkElement src)
    {
        double w = src.ActualWidth;
        if (w <= 0) return;
        int count = (int)System.Math.Max(8, w / BarStride);
        var heights = new List<double>(count);
        // Smooth sine wave with slight asymmetry for organic look
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)count;
            // Combine two sines of different frequencies → natural "audio waveform" vibe
            double a = System.Math.Sin(t * System.Math.PI * 9.0);
            double b = System.Math.Sin(t * System.Math.PI * 3.5 + 0.7);
            double mag = (a * 0.55 + b * 0.45 + 1.0) / 2.0; // normalize to 0..1
            heights.Add(MinH + mag * (MaxH - MinH));
        }
        host.ItemsSource = heights;
    }
}

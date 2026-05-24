using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SoundCheck.Helpers;

/// <summary>
/// MultiBinding: [Value, Maximum, Minimum, RootActualWidth] → Fill.Width (double),
/// or Thumb.Margin (Thickness left=fillWidth-6) when parameter="thumb".
/// </summary>
public class SliderFillConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4) return 0.0;
        if (values[0] is not double value) return 0.0;
        if (values[1] is not double max)   return 0.0;
        if (values[2] is not double min)   return 0.0;
        if (values[3] is not double width) return 0.0;
        double span = max - min;
        if (span <= 0) return 0.0;
        double frac = Math.Clamp((value - min) / span, 0.0, 1.0);
        double w = frac * width;
        if (parameter is string s)
        {
            if (s == "thumb")     return new Thickness(Math.Max(0, w - 6), 0, 0, 0);
            if (s == "remaining") return Math.Max(0, width - w);
        }
        return w;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

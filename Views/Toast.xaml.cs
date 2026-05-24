using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SoundCheck.Views;

public partial class Toast : UserControl
{
    private DispatcherTimer? _hideTimer;

    public Toast() { InitializeComponent(); }

    public void Show(string message, int durationMs = 2800)
    {
        TxtMsg.Text = message;

        // Cancel any pending hide
        _hideTimer?.Stop();

        // Fade in + slide up
        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = 1, Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        ToastTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation
        {
            To = 0, Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        // HTML: transform: scale(.94) → scale(1)
        var sIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sIn);
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sIn);

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = 0, Duration = TimeSpan.FromMilliseconds(320),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            ToastTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation
            {
                To = 20, Duration = TimeSpan.FromMilliseconds(320),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            var sOut = new DoubleAnimation { To = 0.94, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sOut);
            ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sOut);
        };
        _hideTimer.Start();
    }
}

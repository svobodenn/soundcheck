using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SoundCheck.Views;

public partial class HelpView : UserControl
{
    public event Action? Closed;

    public HelpView() { InitializeComponent(); }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    /// <summary>Clicking outside the card closes the overlay.</summary>
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();
    /// <summary>Clicks inside the card are swallowed so they don't reach the backdrop.</summary>
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    public void AnimateIn()
    {
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        HpScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        HpScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
    }

    public void AnimateOut(Action onDone)
    {
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        var sx = new DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(200) };
        fade.Completed += (_, _) => onDone();
        BeginAnimation(OpacityProperty, fade);
        HpScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        HpScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
    }
}

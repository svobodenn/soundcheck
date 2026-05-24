using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SoundCheck.Views;

public partial class ConfirmDialog : UserControl
{
    private Action<bool>? _onResult;

    public ConfirmDialog() { InitializeComponent(); }

    /// <summary>Show modal confirmation with custom title/message. Callback invoked with true=confirm, false=cancel.</summary>
    public void Show(string title, string message, string confirmText, Action<bool> onResult)
    {
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        BtnConfirm.Content = confirmText;
        _onResult = onResult;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        CdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        CdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
        Focus();
    }

    private void Close(bool result)
    {
        var cb = _onResult;
        _onResult = null;
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160) };
        fade.Completed += (_, _) => { Visibility = Visibility.Collapsed; cb?.Invoke(result); };
        BeginAnimation(OpacityProperty, fade);
        var sx = new DoubleAnimation { To = 0.94, Duration = TimeSpan.FromMilliseconds(160) };
        CdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        CdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object sender, RoutedEventArgs e)  => Close(false);
}

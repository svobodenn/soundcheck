using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SoundCheck.Views;

public partial class InputDialog : UserControl
{
    private Action<string?>? _onResult; // null = cancelled

    public InputDialog() { InitializeComponent(); }

    /// <summary>Prompt for a line of text. Callback gets the trimmed text, or null on cancel/empty.</summary>
    public void Show(string title, string placeholder, string initial, Action<string?> onResult)
    {
        TxtTitle.Text = title;
        TxtPlaceholder.Text = placeholder;
        TxtInput.Text = initial ?? "";
        TxtPlaceholder.Visibility = string.IsNullOrEmpty(TxtInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        TxtInput.TextChanged -= OnTextChanged;
        TxtInput.TextChanged += OnTextChanged;
        _onResult = onResult;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        IdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        IdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
        => TxtPlaceholder.Visibility = string.IsNullOrEmpty(TxtInput.Text) ? Visibility.Visible : Visibility.Collapsed;

    private void Close(string? result)
    {
        var cb = _onResult;
        _onResult = null;
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160) };
        fade.Completed += (_, _) => { Visibility = Visibility.Collapsed; cb?.Invoke(result); };
        BeginAnimation(OpacityProperty, fade);
        var sx = new DoubleAnimation { To = 0.94, Duration = TimeSpan.FromMilliseconds(160) };
        IdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        IdScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = TxtInput.Text.Trim();
        Close(string.IsNullOrEmpty(text) ? null : text);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(null);
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Close(null);
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        else if (e.Key == Key.Enter) { Ok_Click(this, new RoutedEventArgs()); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

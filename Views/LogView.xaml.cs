using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SoundCheck.Services;

namespace SoundCheck.Views;

public partial class LogView : UserControl
{
    public event Action? Closed;

    public LogView()
    {
        InitializeComponent();
        Log.Changed += OnLogChanged;
    }

    private void OnLogChanged()
    {
        // Only refresh while visible; marshal to the UI thread (Log fires on any thread).
        if (Visibility != Visibility.Visible) return;
        Dispatcher.BeginInvoke(new Action(Refresh));
    }

    public void Refresh()
    {
        var lines = Log.Recent();
        TxtLog.Text = string.Join(Environment.NewLine, lines);
        TxtEmpty.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtLog.CaretIndex = TxtLog.Text.Length;
        TxtLog.ScrollToEnd();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(TxtLog.Text); } catch { }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        Log.Clear();
        Refresh();
    }

    private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Log.LogPath) { UseShellExecute = true }); }
        catch { }
    }

    public void AnimateIn()
    {
        Refresh();
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(240), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sc = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        LgScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        LgScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        LgTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, ty);
    }

    public void AnimateOut(Action onDone)
    {
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180) };
        var sc = new DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(180) };
        var ty = new DoubleAnimation { To = 22, Duration = TimeSpan.FromMilliseconds(180) };
        fade.Completed += (_, _) => onDone();
        BeginAnimation(OpacityProperty, fade);
        LgScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        LgScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        LgTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, ty);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}

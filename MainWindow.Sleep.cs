using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

// Sleep timer (pause playback after N minutes) + the custom-minutes slider in
// the sleep menu. Split out of MainWindow.xaml.cs.
public partial class MainWindow
{
    // ── Sleep timer ───────────────────────────────────────────────────────
    private DispatcherTimer? _sleepTimer;
    private DateTime _sleepFireAt;
    private void BtnSleep_Click(object sender, RoutedEventArgs e)
    {
        // Open the attached ContextMenu manually on left-click
        if (sender is Button b && b.ContextMenu != null)
        {
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            b.ContextMenu.IsOpen = true;
        }
    }
    private void SleepMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string tag) return;
        if (!int.TryParse(tag, out int minutes)) return;
        StartSleepTimer(minutes);
    }

    /// <summary>
    /// Apply / cancel the sleep timer. 0 = cancel, positive = pause after N minutes.
    /// Caps at 24h to avoid silly user input like "9999999".
    /// </summary>
    private void StartSleepTimer(int minutes)
    {
        minutes = Math.Min(minutes, 24 * 60);
        _sleepTimer?.Stop();
        _sleepTimer = null;
        if (minutes <= 0)
        {
            BtnSleep.SetResourceReference(ForegroundProperty, "T2");
            BtnSleep.Opacity = 0.65;
            ToastView.Show(Localization.T("ToastSleepCancelled"));
            return;
        }
        _sleepFireAt = DateTime.Now.AddMinutes(minutes);
        _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sleepTimer.Tick += (_, _) =>
        {
            if (DateTime.Now >= _sleepFireAt)
            {
                _sleepTimer?.Stop();
                _sleepTimer = null;
                _audio.Pause();
                UpdatePlayButton();
                BtnSleep.SetResourceReference(ForegroundProperty, "T2");
                BtnSleep.Opacity = 0.65;
                ToastView.Show(Localization.T("ToastSleepFired"));
            }
        };
        _sleepTimer.Start();
        BtnSleep.SetResourceReference(ForegroundProperty, "Accent");
        BtnSleep.Opacity = 1.0;
        bool _en = Localization.Current == Localization.En;
        string mUnit = _en ? "min" : "мин";
        string hUnit = _en ? "h" : "ч";
        string label = minutes < 60 ? $"{minutes} {mUnit}" : (minutes % 60 == 0 ? $"{minutes/60} {hUnit}" : $"{minutes/60} {hUnit} {minutes%60} {mUnit}");
        ToastView.Show(string.Format(Localization.T("ToastSleepSetFmt"), label));
    }

    // ─── Custom-minutes slider in sleep-menu ──────────────────────────────
    // The slider lives inside a ContextMenu item. Dragging updates the live
    // label; releasing the thumb (or just clicking on the track) commits.
    private void SldSleepCustom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSleepCustomLabel == null) return;
        int mins = (int)Math.Round(e.NewValue);
        bool en = Localization.Current == Localization.En;
        string mU = en ? "min" : "мин";
        string hU = en ? "h" : "ч";
        TxtSleepCustomLabel.Text = mins < 60
            ? $"{mins} {mU}"
            : mins % 60 == 0
                ? $"{mins / 60} {hU}"
                : $"{mins / 60} {hU} {mins % 60} {mU}";
    }

    private void SldSleepCustom_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => ApplyCustomSleep();

    private void SldSleepCustom_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // PreviewMouseLeftButtonUp fires for clicks on the track AND on the
        // thumb release. Apply in both cases — DragCompleted already covers
        // thumb-drag, but a single click that doesn't start a drag won't fire
        // DragCompleted, so we need this fallback. Idempotent re-application
        // is fine (StartSleepTimer cancels any prior timer first).
        ApplyCustomSleep();
    }

    private void ApplyCustomSleep()
    {
        int mins = (int)Math.Round(SldSleepCustom.Value);
        if (mins < 1) return;
        StartSleepTimer(mins);
        SleepMenu.IsOpen = false;
    }
}

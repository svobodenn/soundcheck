using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SoundCheck.Services;

namespace SoundCheck.Views;

public partial class ProfileView : UserControl
{
    public event Action? Closed;
    public event Action? ResetRequested;

    public ProfileView()
    {
        InitializeComponent();
    }

    public void UpdateData(
        int trackCount,
        long totalListenedSecs,
        long totalPlays,
        string sessionTime,
        string favArtist,
        List<TopTrack> topTracks,
        List<HistoryEntry> history,
        Dictionary<string, System.Windows.Media.Imaging.BitmapImage?>? coverMap = null,
        List<(string Artist, long Plays)>? topArtists = null,
        int[]? playsPerDay = null,
        int[]? playsPerHour = null)
    {
        coverMap ??= new();
        RenderDayChart(playsPerDay ?? new int[30]);
        RenderHourHeatmap(playsPerHour ?? new int[24]);
        TxtTracksCount.Text = trackCount.ToString();
        // HTML: H<span>ч</span> M<span>м</span> — number bold T1, unit small light T2
        FormatTotalListened(totalListenedSecs);
        TxtTotalPlays.Text = totalPlays.ToString();

        // Top tracks
        long maxPlays = topTracks.Count > 0 ? topTracks[0].Plays : 1;
        var topItems = topTracks.Select((t, i) => new
        {
            Rank = i + 1,
            t.Title,
            t.Artist,
            t.Plays,
            BarWidth = 80.0 * t.Plays / maxPlays,
            Cover = coverMap.TryGetValue($"{t.Title}|{t.Artist}", out var c) ? c : null,
        }).ToList();
        TopList.ItemsSource = topItems;
        TxtTopEmpty.Visibility = topItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // History
        var histItems = history.Select((h, i) => new
        {
            Rank = i + 1,
            h.Title,
            h.Artist,
            Ago = Storage.FmtAgo(h.PlayedAt),
            Cover = coverMap.TryGetValue($"{h.Title}|{h.Artist}", out var c) ? c : null,
        }).ToList();
        HistList.ItemsSource = histItems;
        TxtHistEmpty.Visibility = histItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FormatTotalListened(long secs)
    {
        // Defaults: hide secondary unit
        TxtTotalListenedVal2.Text = "";
        TxtTotalListenedUnit2.Text = "";
        bool en = SoundCheck.Services.Localization.Current == SoundCheck.Services.Localization.En;
        string secU = en ? "s" : "с";
        string minU = en ? "m" : "м";
        string hrU  = en ? "h" : "ч";
        if (secs < 60) { TxtTotalListenedVal.Text = secs.ToString(); TxtTotalListenedUnit.Text = secU; return; }
        if (secs < 3600) { TxtTotalListenedVal.Text = (secs / 60).ToString(); TxtTotalListenedUnit.Text = minU; return; }
        long h = secs / 3600;
        long m = (secs % 3600) / 60;
        TxtTotalListenedVal.Text = h.ToString();
        TxtTotalListenedUnit.Text = hrU;
        if (m > 0)
        {
            TxtTotalListenedVal2.Text = " " + m;
            TxtTotalListenedUnit2.Text = minU;
        }
    }

    // ─── Charts ───────────────────────────────────────────────────────────

    private void RenderDayChart(int[] plays)
    {
        DayChartHost.Children.Clear();
        int max = 1;
        for (int i = 0; i < plays.Length; i++) if (plays[i] > max) max = plays[i];
        // plays[0] = today, plays[N-1] = N days ago. Reverse so chart reads left-to-right (oldest → today).
        for (int i = plays.Length - 1; i >= 0; i--)
        {
            double mag = Math.Max(0.04, plays[i] / (double)max);
            var bar = new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.Resources["Accent"],
                Width = 6, Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(1.5),
                Height = mag * 56,
                Opacity = plays[i] == 0 ? 0.18 : 0.85,
                ToolTip = plays[i] > 0 ? $"{plays[i]} {Plural(plays[i])} • {(DateTime.Today.AddDays(-i)):dd MMM}" : null,
            };
            DayChartHost.Children.Add(bar);
        }
    }

    private void RenderHourHeatmap(int[] plays)
    {
        HourChartHost.Children.Clear();
        HourLabelsHost.Children.Clear();
        int max = 1;
        for (int i = 0; i < plays.Length; i++) if (plays[i] > max) max = plays[i];
        // Each cell: 18px wide + 3px total margin = 21px stride
        const double cellW = 18, cellH = 18, marginH = 1.5;
        for (int h = 0; h < 24; h++)
        {
            double intensity = plays[h] / (double)max;
            HourChartHost.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.Resources["Accent"],
                Opacity = plays[h] == 0 ? 0.06 : 0.18 + intensity * 0.72,
                Width = cellW, Height = cellH,
                Margin = new Thickness(marginH, 0, marginH, 0),
                CornerRadius = new CornerRadius(3),
                ToolTip = $"{h:D2}:00 — {plays[h]} {Plural(plays[h])}",
            });
            // Label every 3 hours (0, 3, 6, ..., 21) — readable size
            HourLabelsHost.Children.Add(new TextBlock
            {
                Text = (h % 3 == 0) ? h.ToString("D2") : "",
                Foreground = (System.Windows.Media.Brush)FindResource("T2"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Width = cellW + marginH * 2,
                TextAlignment = System.Windows.TextAlignment.Center,
            });
        }
    }

    private static string Plural(long n)
    {
        return SoundCheck.Services.Localization.T("ProfilePlayCount");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        // Just request — MainWindow handles confirmation via its custom ConfirmDialog
        ResetRequested?.Invoke();
    }

    /// <summary>Click outside the card → close.</summary>
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();
    /// <summary>Card swallows the click so it doesn't bubble to the backdrop.</summary>
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    public void AnimateIn()
    {
        var fade = new System.Windows.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        var sx = new System.Windows.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        var sy = new System.Windows.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        PfScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        PfScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
        PfTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, sy);
    }
    public void AnimateOut(Action onDone)
    {
        var fade = new System.Windows.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        var sx = new System.Windows.Media.Animation.DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(200) };
        var sy = new System.Windows.Media.Animation.DoubleAnimation { To = 26, Duration = TimeSpan.FromMilliseconds(200) };
        fade.Completed += (_, _) => onDone();
        BeginAnimation(OpacityProperty, fade);
        PfScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        PfScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
        PfTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, sy);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck.Views;

public partial class PlaylistsPageView : UserControl
{
    public event Action? Closed;
    public event Action? NewRequested;
    public event Action<long>? OpenRequested;
    // Card context menu actions ─ all just pass the playlist id up to the host
    public event Action<long>? PlayRequested;
    public event Action<long>? ShufflePlayRequested;
    public event Action<long>? RenameRequested;
    public event Action<long>? DeleteRequested;
    public event Action<long>? ExportRequested;
    public event Action<long>? AddTracksRequested;
    /// <summary>(sourceId, targetId) — the source playlist's tracks are merged into the target.</summary>
    public event Action<long, long>? MergeRequested;

    /// <summary>Card row shown in the grid.</summary>
    public class PlaylistCard
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public BitmapImage? Cover { get; set; }
        /// <summary>True for the synthetic trailing "+" tile (not a real playlist).</summary>
        public bool IsAdd { get; set; }
    }

    // List of all playlists used to populate the right-click "Merge with…" submenu.
    private List<(long Id, string Name)> _allPlaylists = new();

    public PlaylistsPageView() { InitializeComponent(); }

    public void SetItems(IEnumerable<PlaylistCard> items)
    {
        var list = items.ToList();
        int real = list.Count;                       // count BEFORE the synthetic tile
        list.Add(new PlaylistCard { IsAdd = true });  // trailing "+" tile to create a playlist
        CardsHost.ItemsSource = list;
        TxtEmpty.Visibility = real == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtSubtitle.Text = string.Format(Localization.T("PlaylistsPageCount"), real);
    }

    private void AddCard_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke();

    /// <summary>Host hands over the current set of playlists so the right-click
    /// "Merge with…" submenu can list every other playlist as a target.</summary>
    public void SetAllPlaylists(IEnumerable<(long Id, string Name)> all)
        => _allPlaylists = all.ToList();

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is PlaylistCard c) OpenRequested?.Invoke(c.Id);
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke();

    // ─── Card right-click menu ────────────────────────────────────────────
    /// <summary>The card itself doesn't know about other playlists; this builds the
    /// "Merge with…" submenu from <see cref="_allPlaylists"/> each time the menu opens.</summary>
    private void CardMenu_Opening(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not PlaylistCard self) return;
        if (fe.ContextMenu == null) return;
        // The submenu's MenuItem has Tag="merge-host" (see DataTemplate) — find it.
        MenuItem? mergeHost = null;
        foreach (var it in fe.ContextMenu.Items)
            if (it is MenuItem mi && (mi.Tag as string) == "merge-host") { mergeHost = mi; break; }
        if (mergeHost == null) return;
        mergeHost.Items.Clear();
        var others = _allPlaylists.Where(p => p.Id != self.Id).ToList();
        if (others.Count == 0)
        {
            mergeHost.Items.Add(new MenuItem { Header = Localization.T("NoPlaylists"), IsEnabled = false });
            return;
        }
        foreach (var p in others)
        {
            // "Merge with Y" on card X → X receives Y's tracks (target = self, source = p).
            var mi = new MenuItem { Header = p.Name };
            long src = p.Id;
            mi.Click += (_, _) => MergeRequested?.Invoke(src, self.Id);
            mergeHost.Items.Add(mi);
        }
    }

    // Per-item click handlers (Tag on the MenuItem identifies which action it is)
    private void CardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not PlaylistCard c) return;
        switch (mi.Tag as string)
        {
            case "play":     PlayRequested?.Invoke(c.Id); break;
            case "shuffle":  ShufflePlayRequested?.Invoke(c.Id); break;
            case "open":     OpenRequested?.Invoke(c.Id); break;
            case "add":      AddTracksRequested?.Invoke(c.Id); break;
            case "rename":   RenameRequested?.Invoke(c.Id); break;
            case "export":   ExportRequested?.Invoke(c.Id); break;
            case "delete":   DeleteRequested?.Invoke(c.Id); break;
        }
    }

    // ─── Open / close animations ──────────────────────────────────────────
    public void AnimateIn()
    {
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sc = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        StTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, ty);
    }

    public void AnimateOut(Action onDone)
    {
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160) };
        var sc = new DoubleAnimation { To = 0.99, Duration = TimeSpan.FromMilliseconds(160) };
        var ty = new DoubleAnimation { To = 14, Duration = TimeSpan.FromMilliseconds(160) };
        fade.Completed += (_, _) => onDone();
        BeginAnimation(OpacityProperty, fade);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        StTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, ty);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}

/// <summary>Picks the card template for real playlists and the "+" tile template
/// for the synthetic trailing item (<see cref="PlaylistsPageView.PlaylistCard.IsAdd"/>).</summary>
public class PlaylistCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CardTemplate { get; set; }
    public DataTemplate? AddTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => (item as PlaylistsPageView.PlaylistCard)?.IsAdd == true ? AddTemplate : CardTemplate;
}

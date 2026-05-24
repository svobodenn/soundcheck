using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SoundCheck.Views;

public partial class PlaylistAddView : UserControl
{
    public event Action? Closed;
    /// <summary>Toggle a track's membership in the active playlist: (path, add?).</summary>
    public event Action<string, bool>? ToggleTrack;

    public class PickItem : INotifyPropertyChanged
    {
        public string Path = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public BitmapImage? Cover { get; set; }
        private bool _in;
        public bool InPlaylist { get => _in; set { _in = value; OnPC(nameof(InPlaylist)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private readonly List<PickItem> _all = new();

    public PlaylistAddView() { InitializeComponent(); }

    /// <summary>Populate with every library track, flagging those already in the playlist.</summary>
    public void SetItems(string playlistName, IEnumerable<PickItem> items)
    {
        TxtPlaylistName.Text = playlistName;
        _all.Clear();
        _all.AddRange(items);
        TxtSearch.Text = "";
        ApplyFilter("");
    }

    private void ApplyFilter(string q)
    {
        IEnumerable<PickItem> src = _all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var lq = q.ToLowerInvariant();
            src = src.Where(i => i.Title.ToLowerInvariant().Contains(lq) || i.Artist.ToLowerInvariant().Contains(lq));
        }
        var list = src.ToList();
        TracksList.ItemsSource = list;
        TxtEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchPh.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(TxtSearch.Text);
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickItem it) return;
        it.InPlaylist = !it.InPlaylist;
        ToggleTrack?.Invoke(it.Path, it.InPlaylist);
    }

    // ─── Open / close animations ──────────────────────────────────────────
    public void AnimateIn()
    {
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(240), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var sc = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        StTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, ty);
    }

    public void AnimateOut(Action onDone)
    {
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180) };
        var sc = new DoubleAnimation { To = 0.96, Duration = TimeSpan.FromMilliseconds(180) };
        var ty = new DoubleAnimation { To = 22, Duration = TimeSpan.FromMilliseconds(180) };
        fade.Completed += (_, _) => onDone();
        BeginAnimation(OpacityProperty, fade);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        StScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        StTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, ty);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}

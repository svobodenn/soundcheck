using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck.Views;

public partial class QueuePanel : UserControl
{
    public event Action? Closed;
    public event Action? Cleared;
    public event Action? Shuffled;
    public event Action? PlayAllRequested;
    public event Action<int>? PlayAt;
    public event Action<int>? RemoveAt;
    /// <summary>Fires when user drags item at fromIdx and drops it at toIdx.</summary>
    public event Action<int, int>? Reordered;

    public QueuePanel() { InitializeComponent(); }

    public class QueueItemViewModel
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public BitmapImage? Cover { get; set; }
        public bool IsCurrent { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public void SetItems(List<QueueItemViewModel> items)
    {
        QueueList.ItemsSource = items;
        bool empty = items.Count == 0;
        TxtEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BtnPlayAll.IsEnabled = !empty;

        // Header summary: "N tracks · total duration" (or "empty").
        if (empty)
        {
            TxtQueueSummary.Text = Localization.T("QueueEmptyShort");
        }
        else
        {
            var total = TimeSpan.FromSeconds(items.Sum(i => i.Duration.TotalSeconds));
            TxtQueueSummary.Text = $"{items.Count}{Localization.T("TracksWord")} · {FmtDuration(total)}";
        }
    }

    private static string FmtDuration(TimeSpan d) =>
        d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
            : $"{d.Minutes}:{d.Seconds:00}";

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
    private void BtnClear_Click(object sender, RoutedEventArgs e) => Cleared?.Invoke();
    private void BtnShuffle_Click(object sender, RoutedEventArgs e) => Shuffled?.Invoke();
    private void BtnPlayAll_Click(object sender, RoutedEventArgs e) => PlayAllRequested?.Invoke();

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx) RemoveAt?.Invoke(idx);
    }

    private void QueueItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) { _dragging = false; _dragItem = null; return; }
        if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is Button) return;
        if (sender is FrameworkElement f && f.DataContext is QueueItemViewModel vm)
            PlayAt?.Invoke(vm.Index);
    }

    // ─── Drag-to-reorder ─────────────────────────────────────────────────
    private Point _dragStart;
    private QueueItemViewModel? _dragItem;
    private bool _dragging;

    private void QueueItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        if (sender is FrameworkElement f && f.DataContext is QueueItemViewModel vm)
            _dragItem = vm;
    }

    private void QueueItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null || _dragging) return;
        var diff = e.GetPosition(null) - _dragStart;
        if (Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        if (sender is FrameworkElement el)
        {
            el.Opacity = 0.4;
            try { DragDrop.DoDragDrop(el, _dragItem, DragDropEffects.Move); }
            finally { el.Opacity = 1.0; }
        }
    }

    private void QueueItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(QueueItemViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        // Visual cue: top/bottom inset shadow border
        if (sender is FrameworkElement el && el.DataContext is QueueItemViewModel target && _dragItem != null && target != _dragItem)
        {
            var pos = e.GetPosition(el);
            bool top = pos.Y < el.RenderSize.Height / 2;
            ClearDov();
            _lastDovItem = el;
            if (el is Grid g)
            {
                _dovBorder = new Border
                {
                    Height = 2,
                    Background = (Brush)FindResource("Accent"),
                    VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                g.Children.Add(_dovBorder);
            }
        }
    }

    private FrameworkElement? _lastDovItem;
    private Border? _dovBorder;
    private void ClearDov()
    {
        if (_dovBorder != null && _lastDovItem is Grid g) g.Children.Remove(_dovBorder);
        _dovBorder = null; _lastDovItem = null;
    }

    private void QueueItem_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(typeof(QueueItemViewModel)) is not QueueItemViewModel dragged) return;
            if (sender is not FrameworkElement el || el.DataContext is not QueueItemViewModel target) return;
            if (dragged == target) return;
            var pos = e.GetPosition(el);
            bool top = pos.Y < el.RenderSize.Height / 2;
            int toIdx = top ? target.Index : target.Index + 1;
            Reordered?.Invoke(dragged.Index, toIdx);
        }
        finally
        {
            ClearDov(); _dragItem = null; _dragging = false;
        }
    }
}

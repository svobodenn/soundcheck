using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Track = SoundCheck.Models.Track;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

// Track-list interaction: right-click context menu, external file drag-drop,
// row reordering. Split out of MainWindow.xaml.cs.
public partial class MainWindow
{
    // ─────────────────────────────────────────────────────────────────────
    // Context menu
    // ─────────────────────────────────────────────────────────────────────
    private Track? CtxTrack()
    {
        // ContextMenu of ListView — SelectedItem is the right-clicked track if user right-clicks a row
        // WPF auto-selects on right-click only if we handle PreviewMouseRightButtonDown to set Selected
        return TrackList.SelectedItem as Track;
    }

    private void CtxPlay_Click(object sender, RoutedEventArgs e)
    {
        if (CtxTrack() is Track t) { SetPlaybackContext(); PlayTrack(t); }
    }

    /// <summary>Enter on the track list plays the selected track.</summary>
    private void TrackList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TrackList.SelectedItem is Track t)
        {
            SetPlaybackContext();
            PlayTrack(t);
            e.Handled = true;
        }
    }

    private void CtxPlayNext_Click(object sender, RoutedEventArgs e)
    {
        if (CtxTrack() is not Track t) return;
        _queue.Remove(t);
        _queue.Insert(0, t);
        t.IsInQueue = true;
        RefreshQueueUi();
    }

    private void CtxAddQueue_Click(object sender, RoutedEventArgs e)
    {
        if (CtxTrack() is not Track t) return;
        if (_queue.Contains(t)) { _queue.Remove(t); t.IsInQueue = false; }
        else { _queue.Add(t); t.IsInQueue = true; }
        RefreshQueueUi();
    }

    private void CtxEditTags_Click(object sender, RoutedEventArgs e)
    {
        if (CtxTrack() is Track t) OpenTagEditor(t);
    }

    /// <summary>Open the tag editor for a track and persist edits to the file + library on save.</summary>
    private void OpenTagEditor(Track t)
    {
        TagEditorOverlay.Show(Path.GetFileName(t.Path), t.Title, t.Artist, t.Album, t.CoverBytes, t.IsExplicit, result =>
        {
            if (result == null) return; // cancelled
            ApplyTagEdit(t, result);
        });
    }

    /// <summary>Edit tags for an arbitrary file path coming from the File Manager.
    /// If the file is also in the library, the library entry is updated too.</summary>
    private void OnFmEditTagsRequested(string path)
    {
        var track = _allTracks.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        string title, artist, album;
        byte[]? cover;
        if (track != null)
        {
            title = track.Title; artist = track.Artist; album = track.Album; cover = track.CoverBytes;
        }
        else
        {
            var tags = Services.FileManager.ReadTags(path);
            title = tags.title; artist = tags.artist; album = tags.album; cover = tags.cover;
        }

        TagEditorOverlay.Show(Path.GetFileName(path), title, artist, album, cover, track?.IsExplicit ?? false, result =>
        {
            if (result == null) return;
            if (track != null)
            {
                ApplyTagEdit(track, result); // full path: file + library + now-playing
            }
            else
            {
                string newTitle = string.IsNullOrWhiteSpace(result.Title) ? Path.GetFileNameWithoutExtension(path) : result.Title;
                bool ok = Services.FileManager.WriteTags(path, newTitle, result.Artist, result.Album, result.CoverChanged, result.CoverBytes);
                ToastView.Show(Localization.T(ok ? "ToastTagsSaved" : "ToastTagsFailed"));
            }
            FileManagerOverlay.RefreshCurrent(); // re-scan so the new tags/names show
        });
    }

    private void ApplyTagEdit(Track t, Views.TagEditor.Result r)
    {
        string title = string.IsNullOrWhiteSpace(r.Title) ? t.Title : r.Title; // never blank the title

        // The currently-playing file is locked by NAudio — release it so TagLib can
        // write, then reload and restore the playback position afterwards.
        bool isCurrent = _current == t;
        double pos = 0;
        bool wasPlaying = false, wasRecorded = false;
        if (isCurrent)
        {
            pos = _audio.Position.TotalSeconds;
            wasPlaying = _audio.IsPlaying;
            wasRecorded = _historyRecordedForCurrent;
            _audio.Stop();
        }

        bool ok = Services.FileManager.WriteTags(t.Path, title, r.Artist, r.Album, r.CoverChanged, r.CoverBytes);
        if (ok)
        {
            t.Title = title;
            t.Artist = r.Artist;
            t.Album = r.Album;
            if (r.CoverChanged)
            {
                t.CoverBytes = r.CoverBytes;
                t.Cover = Services.Library.LoadThumb(r.CoverBytes, 80);
                _storage.UpdateTrackCover(t.Path, r.CoverBytes);
                if (_current == t) { ApplyAccentFromCover(t.CoverBytes); UpdateBlurBg(t.CoverBytes); }
            }
            t.IsExplicit = r.IsExplicit;
            _storage.UpdateTrackMeta(t.Path, title, r.Artist, r.Album);
            _storage.UpdateTrackExplicit(t.Path, r.IsExplicit);
            RefreshVisible();
            RefreshRecent();
            RefreshQueueUi();
            if (_current == t) { UpdateBarMeta(); if (NowPlayingOverlay.Visibility == Visibility.Visible) UpdateNowPlaying(); }
            Services.Log.Info($"Tags edited: {t.Path}");
            ToastView.Show(Localization.T("ToastTagsSaved"));
        }
        else
        {
            Services.Log.Warn($"Tag write failed: {t.Path}");
            ToastView.Show(Localization.T("ToastTagsFailed"));
        }

        if (isCurrent)
        {
            PlayTrack(t); // reload the (possibly rewritten) file
            _historyRecordedForCurrent = wasRecorded; // don't double-count this play
            if (pos > 1 && _audio.Duration.TotalSeconds > pos + 1)
                _audio.Seek(pos / _audio.Duration.TotalSeconds);
            if (!wasPlaying) { _audio.Pause(); UpdatePlayButton(); }
        }
    }

    private void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        // Always removes the track from the library entirely. To take a track out of a
        // playlist only, use "Remove from playlist".
        if (CtxTrack() is Track t) DeleteTrackFromLibrary(t);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Drag-drop external files from Explorer
    // ─────────────────────────────────────────────────────────────────────
    private int _dragCounter;

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        _dragCounter++;
        DropOverlay.Visibility = Visibility.Visible;
        DropOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180) });
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        if (--_dragCounter <= 0)
        {
            _dragCounter = 0;
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _dragCounter = 0;
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var files = new List<string>();
        var exts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" };
        foreach (var path in dropped)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(p => exts.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase))));
            }
            else if (File.Exists(path) && exts.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                files.Add(path);
            }
        }
        if (files.Count > 0) AddPaths(files);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Drag-drop reorder
    // ─────────────────────────────────────────────────────────────────────
    private Point _dragStartPos;
    private Track? _dragItem;

    private void TrackList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPos = e.GetPosition(null);
        if (e.OriginalSource is DependencyObject d)
            _dragItem = FindParent<ListViewItem>(d)?.DataContext as Track;
    }

    private void TrackList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var pos = e.GetPosition(null);
        var diff = pos - _dragStartPos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // Drag is allowed in any sort (so a track can be dropped onto a sidebar
        // playlist); the reorder drop itself is blocked when sorted (see TrackList_Drop).
        // HTML: tr.drag { opacity: .32 } — fade dragged row's container
        ListViewItem? container = TrackList.ItemContainerGenerator.ContainerFromItem(_dragItem) as ListViewItem;
        if (container != null) container.Opacity = 0.32;
        // Allow both Move (reorder within the list) and Copy (drop onto a sidebar playlist).
        try { DragDrop.DoDragDrop(TrackList, _dragItem, DragDropEffects.Move | DragDropEffects.Copy); }
        finally { if (container != null) container.Opacity = 1.0; _dragItem = null; }
    }

    private ListViewItem? _lastDovItem;
    private bool _dovTop;

    private void TrackList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Track)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        // Determine if drop is in top or bottom half of the row
        if (e.OriginalSource is DependencyObject d)
        {
            var item = FindParent<ListViewItem>(d);
            if (item != null && item != _lastDovItem)
            {
                ClearDov();
                _lastDovItem = item;
            }
            if (item != null)
            {
                var p = e.GetPosition(item);
                _dovTop = p.Y < item.ActualHeight / 2;
                item.BorderBrush = (Brush)FindResource("Accent");
                item.BorderThickness = _dovTop
                    ? new Thickness(0, 3, 0, 0)
                    : new Thickness(0, 0, 0, 3);
                item.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xC8, 0xA9, 0x6E));
            }
        }
    }

    private void ClearDov()
    {
        if (_lastDovItem != null)
        {
            _lastDovItem.BorderBrush = (Brush)FindResource("B1");
            _lastDovItem.BorderThickness = new Thickness(0, 0, 0, 1);
            _lastDovItem.Background = Brushes.Transparent;
            _lastDovItem = null;
        }
    }

    private void TrackList_Drop(object sender, DragEventArgs e)
    {
        ClearDov();
        if (e.Data.GetData(typeof(Track)) is not Track dragged) return;
        Track? target = null;
        if (e.OriginalSource is DependencyObject d)
            target = FindParent<ListViewItem>(d)?.DataContext as Track;
        if (target == null || target == dragged) return;
        // Reorder needs natural ("added") order + no active search, so the visible list
        // maps 1:1 to the underlying order.
        if (_sortMode != "added" || !string.IsNullOrEmpty(_searchQ))
        {
            ToastView.Show(Localization.T("ToastSortReorder"));
            return;
        }
        if (_currentPlaylistId is long pid) ReorderInPlaylist(dragged, target, _dovTop, pid);
        else ReorderTrack(dragged, target, _dovTop);
    }

    /// <summary>Reorder a track within the active playlist and persist the new order.</summary>
    private void ReorderInPlaylist(Track item, Track target, bool insertAbove, long pid)
    {
        var paths = _visible.Select(t => t.Path).ToList();
        int from = paths.IndexOf(item.Path);
        if (from < 0) return;
        paths.RemoveAt(from);
        int to = paths.IndexOf(target.Path);
        if (to < 0) return;
        if (!insertAbove) to++;
        to = Math.Clamp(to, 0, paths.Count);
        paths.Insert(to, item.Path);
        _storage.ReorderPlaylist(pid, paths);
        RefreshVisible();
    }

    private void ReorderTrack(Track item, Track target, bool insertAbove)
    {
        int from = _allTracks.IndexOf(item);
        int to = _allTracks.IndexOf(target);
        if (from < 0 || to < 0) return;
        if (!insertAbove) to++;
        // Adjust if we're moving forward (removal shifts indices)
        if (from < to) to--;
        if (from == to) return;
        _allTracks.Move(from, Math.Clamp(to, 0, _allTracks.Count - 1));
        RefreshVisible();
    }

    // Auto-select row on right-click (so ContextMenu has correct target)
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src)
        {
            var item = FindParent<ListViewItem>(src);
            if (item != null)
            {
                item.IsSelected = true; item.Focus();
                // Update "В очередь" / "Убрать из очереди" label based on state
                if (item.DataContext is Track t)
                    CtxQueueMenuItem.Header = t.IsInQueue ? Localization.T("CtxRemoveQueue") : Localization.T("CtxAddQueue");
                // Context-aware items: "Remove from playlist" only makes sense inside a playlist.
                CtxRemovePlaylistItem.Visibility = _currentPlaylistId != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        base.OnPreviewMouseRightButtonDown(e);
    }

    private void CtxRemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (CtxTrack() is not Track t || _currentPlaylistId is not long pid) return;
        _storage.RemoveFromPlaylist(pid, t.Path);
        RefreshVisible();
        RefreshPlaylistsUi();
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = VisualTreeHelper.GetParent(d);
        return d as T;
    }
}

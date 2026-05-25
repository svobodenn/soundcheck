using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SoundCheck.Services;
using SoundCheck.Views;
using Track = SoundCheck.Models.Track;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck;

// User playlists: sidebar list, create/rename/delete, add-to-playlist submenu,
// per-playlist cover + track picker, and switching the main view.
public partial class MainWindow
{
    private bool _playlistAddWired;

    /// <summary>Row shown in the sidebar playlists list.</summary>
    public class PlaylistVm
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public byte[]? CoverBytes { get; set; }
        public BitmapImage? Cover { get; set; }
    }

    /// <summary>Reload the sidebar playlist list from storage, keeping the current selection.</summary>
    private void RefreshPlaylistsUi()
    {
        var items = _storage.LoadPlaylists()
            .Select(p => new PlaylistVm
            {
                Id = p.Id, Name = p.Name, Count = p.Count,
                CoverBytes = p.Cover, Cover = Library.LoadThumb(p.Cover, 48),
            })
            .ToList();
        _suppressPlaylistSel = true;
        PlaylistList.ItemsSource = items;
        PlaylistList.SelectedItem = _currentPlaylistId is long id ? items.FirstOrDefault(i => i.Id == id) : null;
        _suppressPlaylistSel = false;
        PlaylistEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaylistSel) return;
        if (PlaylistList.SelectedItem is PlaylistVm vm) SelectPlaylist(vm);
    }

    /// <summary>Double-click a playlist → play it immediately.</summary>
    private void PlaylistList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistVm vm) PlayPlaylistById(vm.Id, false);
    }

    // ── Play a whole playlist (context menu) ──────────────────────────────────
    private void PlaylistPlay_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement fe && fe.DataContext is PlaylistVm vm) PlayPlaylistById(vm.Id, false); }

    private void PlaylistShufflePlay_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement fe && fe.DataContext is PlaylistVm vm) PlayPlaylistById(vm.Id, true); }

    /// <summary>Load a playlist into the queue (optionally shuffled) and start playing.</summary>
    private void PlayPlaylistById(long id, bool shuffle)
    {
        var byPath = _allTracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
        var tracks = _storage.LoadPlaylistPaths(id)
            .Where(p => byPath.ContainsKey(p))
            .Select(p => byPath[p])
            .ToList();
        PlayTracks(tracks, shuffle);
    }

    /// <summary>Start playing a set of tracks as the playback context (▶ = in order, ⇄ = shuffled).
    /// Playback then flows WITHIN this set (Next/auto-advance/shuffle) and loops at the end.</summary>
    private void PlayTracks(List<Track> tracks, bool shuffle)
    {
        if (tracks.Count == 0) { ToastView.Show(Localization.T("ToastPlaylistEmpty")); return; }
        _playContext.Clear();
        _playContext.AddRange(tracks);
        _shuffleHistory.Clear();
        // The ▶/⇄ buttons map directly onto the shuffle toggle.
        if (_shuffle != shuffle)
        {
            _shuffle = shuffle;
            UpdateShuffleVisual();
            _storage.SetSetting("shuffle", shuffle ? "1" : "0");
        }
        PlayTrack(shuffle ? PickShuffle(_playContext) : tracks[0]);
    }

    /// <summary>Show playlist-only actions (add tracks / cover) when a playlist is open.
    /// Play/Shuffle live in the toolbar and act on the current view either way.</summary>
    private void UpdateHeroActions()
    {
        PlaylistActions.Visibility = _currentPlaylistId != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Drag onto a playlist: a track (add it) or another playlist (merge) ────
    private void Playlist_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(typeof(Track)) || e.Data.GetDataPresent(typeof(PlaylistVm));
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        ClearDov(); // clear any leftover reorder indicator from the track list
        if (sender is not FrameworkElement fe || fe.DataContext is not PlaylistVm target) return;

        if (e.Data.GetData(typeof(Track)) is Track t)
        {
            bool added = _storage.AddToPlaylist(target.Id, t.Path);
            RefreshPlaylistsUi();
            if (_currentPlaylistId == target.Id) RefreshVisible();
            ToastView.Show(added
                ? string.Format(Localization.T("ToastAddedToPlaylist"), target.Name)
                : Localization.T("ToastAlreadyInPlaylist"));
        }
        else if (e.Data.GetData(typeof(PlaylistVm)) is PlaylistVm src && src.Id != target.Id)
        {
            MergePlaylists(src.Id, target.Id, target.Name); // target receives src's tracks
        }
        e.Handled = true;
    }

    /// <summary>Add all of <paramref name="sourceId"/>'s tracks into <paramref name="targetId"/> (deduped). Source is kept.</summary>
    private void MergePlaylists(long sourceId, long targetId, string targetName)
    {
        if (sourceId == targetId) return;
        int added = 0;
        foreach (var p in _storage.LoadPlaylistPaths(sourceId))
            if (_storage.AddToPlaylist(targetId, p)) added++;
        RefreshPlaylistsUi();
        if (_currentPlaylistId == targetId) RefreshVisible();
        ToastView.Show(string.Format(Localization.T("ToastPlaylistsMerged"), added, targetName));
    }

    // ── Drag a playlist (to merge into another) ───────────────────────────────
    private System.Windows.Point _plDragStart;
    private PlaylistVm? _plDragItem;

    private void PlaylistList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _plDragStart = e.GetPosition(null);
        _plDragItem = (e.OriginalSource is DependencyObject d ? FindParent<ListBoxItem>(d) : null)?.DataContext as PlaylistVm;
    }

    private void PlaylistList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _plDragItem == null) return;
        var diff = e.GetPosition(null) - _plDragStart;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { DragDrop.DoDragDrop(PlaylistList, new DataObject(typeof(PlaylistVm), _plDragItem), DragDropEffects.Copy); }
        finally { _plDragItem = null; }
    }

    // ── Export / import playlists as .m3u ─────────────────────────────────────
    private void PlaylistExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PlaylistVm vm) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.T("PlaylistExport"),
            FileName = FileManager.Sanitize(vm.Name) + ".m3u8",
            Filter = "Playlist (*.m3u8;*.m3u)|*.m3u8;*.m3u|All files (*.*)|*.*",
            DefaultExt = ".m3u8",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = new List<string> { "#EXTM3U" };
            lines.AddRange(_storage.LoadPlaylistPaths(vm.Id));
            File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
            ToastView.Show(string.Format(Localization.T("ToastPlaylistExported"), vm.Name));
        }
        catch (Exception ex) { Log.Error("m3u export failed", ex); ToastView.Show(Localization.T("ToastExportFailed")); }
    }

    private static readonly string[] _m3uExts = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };
    private async void BtnImportPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("PlaylistImport"),
            Filter = "Playlist (*.m3u8;*.m3u)|*.m3u8;*.m3u|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string file = dlg.FileName;
            string baseDir = Path.GetDirectoryName(file) ?? "";
            var raw = await Task.Run(() => File.ReadAllLines(file));
            var paths = new List<string>();
            foreach (var line in raw)
            {
                var s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue; // skip blanks + #EXTINF directives
                string p = Path.IsPathRooted(s) ? s : Path.GetFullPath(Path.Combine(baseDir, s));
                if (_m3uExts.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && File.Exists(p) && !paths.Contains(p))
                    paths.Add(p);
            }
            if (paths.Count == 0) { ToastView.Show(Localization.T("ToastImportEmpty")); return; }

            // Pull any tracks not yet in the library in (tags read off the UI thread).
            var existing = new HashSet<string>(_allTracks.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
            var toLoad = paths.Where(p => !existing.Contains(p)).ToList();
            var loaded = await Task.Run(() =>
            {
                var list = new List<Track>();
                foreach (var p in toLoad)
                    try { var t = Library.LoadTrack(p); if (t != null) list.Add(t); } catch { }
                return list;
            });
            foreach (var t in loaded)
            {
                _allTracks.Add(t);
                try { _storage.SaveTrack(t); } catch { }
            }

            string name = Path.GetFileNameWithoutExtension(file);
            long id = _storage.CreatePlaylist(name);
            foreach (var p in paths) _storage.AddToPlaylist(id, p);

            RefreshVisible();
            UpdateStats();
            RefreshPlaylistsUi();
            Log.Info($"Imported playlist '{name}' with {paths.Count} track(s)");
            ToastView.Show(string.Format(Localization.T("ToastPlaylistImported"), paths.Count, name));
        }
        catch (Exception ex) { Log.Error("m3u import failed", ex); ToastView.Show(Localization.T("ToastImportFailed")); }
    }

    // ── "Merge with…" submenu on a playlist ───────────────────────────────────
    private void PlaylistMerge_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem parent || parent.DataContext is not PlaylistVm self) return;
        parent.Items.Clear();
        var others = _storage.LoadPlaylists().Where(p => p.Id != self.Id).ToList();
        if (others.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = Localization.T("NoPlaylists"), IsEnabled = false });
            return;
        }
        foreach (var p in others)
        {
            var mi = new MenuItem { Header = p.Name };
            long otherId = p.Id;
            // "Merge with Y" on playlist X → X receives Y's tracks.
            mi.Click += (_, _) => MergePlaylists(otherId, self.Id, self.Name);
            parent.Items.Add(mi);
        }
    }

    /// <summary>Switch the main view to a playlist.</summary>
    private void SelectPlaylist(PlaylistVm vm)
    {
        _currentPlaylistId = vm.Id;
        TxtCollectionName.Text = vm.Name;
        PlaylistCoverHost.Visibility = Visibility.Visible;
        PlaylistCoverImg.Source = vm.CoverBytes != null ? Library.LoadFullCover(vm.CoverBytes, 160) : null;
        UpdateHeroActions();
        SetTabActive(BtnTabAll, false);
        RefreshVisible();
        UpdateStats();
    }

    private void SelectPlaylistById(long id)
    {
        if (PlaylistList.ItemsSource is IEnumerable<PlaylistVm> items)
        {
            var vm = items.FirstOrDefault(i => i.Id == id);
            if (vm != null)
            {
                _suppressPlaylistSel = true;
                PlaylistList.SelectedItem = vm;
                _suppressPlaylistSel = false;
                SelectPlaylist(vm);
            }
        }
    }

    /// <summary>Switch the main view back to the whole library.</summary>
    private void ShowAllTracks()
    {
        _currentPlaylistId = null;
        TxtCollectionName.Text = Localization.T("MyCollection");
        PlaylistCoverHost.Visibility = Visibility.Collapsed;
        PlaylistCoverImg.Source = null;
        _suppressPlaylistSel = true;
        PlaylistList.SelectedItem = null;
        _suppressPlaylistSel = false;
        UpdateHeroActions();
        SetTabActive(BtnTabAll, true);
        RefreshVisible();
        UpdateStats();
    }

    private void BtnNewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        InputOverlay.Show(Localization.T("NewPlaylistTitle"), Localization.T("PlaylistNamePh"), "", name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            long id = _storage.CreatePlaylist(name.Trim());
            RefreshPlaylistsUi();
            SelectPlaylistById(id);
            ToastView.Show(string.Format(Localization.T("ToastPlaylistCreated"), name.Trim()));
        });
    }

    private void PlaylistRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PlaylistVm vm) return;
        InputOverlay.Show(Localization.T("RenamePlaylistTitle"), Localization.T("PlaylistNamePh"), vm.Name, name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _storage.RenamePlaylist(vm.Id, name.Trim());
            if (_currentPlaylistId == vm.Id) TxtCollectionName.Text = name.Trim();
            RefreshPlaylistsUi();
        });
    }

    private void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PlaylistVm vm) return;
        ConfirmOverlay.Show(
            Localization.T("ConfirmDeletePlaylistTitle"),
            string.Format(Localization.T("ConfirmDeletePlaylistMsg"), vm.Name),
            Localization.T("PlaylistDelete"),
            ok =>
            {
                if (!ok) return;
                _storage.DeletePlaylist(vm.Id);
                if (_currentPlaylistId == vm.Id) ShowAllTracks();
                RefreshPlaylistsUi();
            });
    }

    // ── In-playlist: add tracks + change cover ────────────────────────────────
    private void BtnPlaylistAddTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlaylistId is not long pid) return;
        if (!_playlistAddWired)
        {
            PlaylistAddOverlay.Closed += () => PlaylistAddOverlay.AnimateOut(() => PlaylistAddOverlay.Visibility = Visibility.Collapsed);
            PlaylistAddOverlay.ToggleTrack += OnPlaylistToggleTrack;
            _playlistAddWired = true;
        }
        var inSet = new HashSet<string>(_storage.LoadPlaylistPaths(pid), StringComparer.OrdinalIgnoreCase);
        var items = _allTracks.Select(t => new PlaylistAddView.PickItem
        {
            Path = t.Path, Title = t.Title, Artist = t.Artist, Cover = t.Cover,
            InPlaylist = inSet.Contains(t.Path),
        });
        PlaylistAddOverlay.SetItems(TxtCollectionName.Text, items);
        PlaylistAddOverlay.Visibility = Visibility.Visible;
        PlaylistAddOverlay.AnimateIn();
    }

    private void OnPlaylistToggleTrack(string path, bool add)
    {
        if (_currentPlaylistId is not long pid) return;
        if (add) _storage.AddToPlaylist(pid, path);
        else _storage.RemoveFromPlaylist(pid, path);
        RefreshPlaylistsUi();
        RefreshVisible();
    }

    private void BtnPlaylistCover_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlaylistId is not long pid) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("PlaylistCover"),
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            _storage.UpdatePlaylistCover(pid, bytes);
            PlaylistCoverImg.Source = Library.LoadFullCover(bytes, 160);
            PlaylistCoverHost.Visibility = Visibility.Visible;
            RefreshPlaylistsUi();
        }
        catch { ToastView.Show(Localization.T("ToastPlayFailed")); }
    }

    // ── Add-to-playlist submenu on a track row ───────────────────────────────
    private void CtxAddPlaylist_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        CtxAddPlaylistMenu.Items.Clear();
        var playlists = _storage.LoadPlaylists();

        var newItem = new MenuItem { Header = Localization.T("NewPlaylistDots") };
        newItem.Click += (_, _) => AddCurrentCtxTrackToNewPlaylist();
        CtxAddPlaylistMenu.Items.Add(newItem);

        if (playlists.Count > 0) CtxAddPlaylistMenu.Items.Add(new Separator());

        foreach (var p in playlists)
        {
            var mi = new MenuItem { Header = p.Name };
            long id = p.Id;
            string nm = p.Name;
            mi.Click += (_, _) => AddCtxTrackToPlaylist(id, nm);
            CtxAddPlaylistMenu.Items.Add(mi);
        }
    }

    private void AddCtxTrackToPlaylist(long id, string name)
    {
        if (CtxTrack() is not Track t) return;
        bool added = _storage.AddToPlaylist(id, t.Path);
        RefreshPlaylistsUi();
        if (_currentPlaylistId == id) RefreshVisible();
        ToastView.Show(added
            ? string.Format(Localization.T("ToastAddedToPlaylist"), name)
            : Localization.T("ToastAlreadyInPlaylist"));
    }

    private void AddCurrentCtxTrackToNewPlaylist()
    {
        if (CtxTrack() is not Track t) return;
        InputOverlay.Show(Localization.T("NewPlaylistTitle"), Localization.T("PlaylistNamePh"), "", name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            long id = _storage.CreatePlaylist(name.Trim());
            _storage.AddToPlaylist(id, t.Path);
            RefreshPlaylistsUi();
            ToastView.Show(string.Format(Localization.T("ToastAddedToPlaylist"), name.Trim()));
        });
    }
}

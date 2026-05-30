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
        // Keep the "Playlists" tab badge count in sync with the sidebar list.
        try { TxtTabPlaylistsCount.Text = items.Count.ToString(); } catch { }
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaylistSel) return;
        if (PlaylistList.SelectedItem is PlaylistVm vm) SelectPlaylist(vm);
    }

    // ── Full "All playlists" page (cards) ─────────────────────────────────────
    private bool _playlistsPageOpen;
    private bool _playlistsPageWired;
    private void BtnPlaylistsPage_Click(object sender, RoutedEventArgs e) => TogglePlaylistsPage(true);
    private void BtnTabPlaylists_Click(object sender, RoutedEventArgs e) => TogglePlaylistsPage(true);

    /// <summary>Rebuild the cards grid from the current playlists (used after create/rename/delete).</summary>
    private void RefreshPlaylistsPageCards()
    {
        var cards = _storage.LoadPlaylists().Select(p => new Views.PlaylistsPageView.PlaylistCard
        {
            Id = p.Id, Name = p.Name, Count = p.Count, Cover = Library.LoadThumb(p.Cover, 160),
        });
        PlaylistsPageOverlay.SetItems(cards);
        try { TxtTabPlaylistsCount.Text = _storage.LoadPlaylists().Count.ToString(); } catch { }
    }

    private void TogglePlaylistsPage(bool open)
    {
        _playlistsPageOpen = open;
        if (open)
        {
            if (!_playlistsPageWired)
            {
                PlaylistsPageOverlay.Closed += () => TogglePlaylistsPage(false);
                // Create-on-page: stay on the playlists page; just refresh the cards after the new one is created.
                PlaylistsPageOverlay.NewRequested += () =>
                {
                    InputOverlay.Show(Localization.T("NewPlaylistTitle"), Localization.T("PlaylistNamePh"), "", name =>
                    {
                        if (string.IsNullOrWhiteSpace(name)) return;
                        _storage.CreatePlaylist(name.Trim());
                        RefreshPlaylistsUi();
                        RefreshPlaylistsPageCards();
                        ToastView.Show(string.Format(Localization.T("ToastPlaylistCreated"), name.Trim()));
                    });
                };
                PlaylistsPageOverlay.OpenRequested += id => { TogglePlaylistsPage(false); SelectPlaylistById(id); };
                PlaylistsPageOverlay.PlayRequested        += id => { PlayPlaylistById(id, false); };
                PlaylistsPageOverlay.ShufflePlayRequested += id => { PlayPlaylistById(id, true); };
                PlaylistsPageOverlay.RenameRequested      += id => RenamePlaylistById(id);
                PlaylistsPageOverlay.DeleteRequested      += id => DeletePlaylistById(id);
                PlaylistsPageOverlay.ExportRequested      += id => ExportPlaylistById(id);
                PlaylistsPageOverlay.MergeRequested       += (src, dst) => { MergePlaylistsById(src, dst); RefreshPlaylistsPageCards(); };
                PlaylistsPageOverlay.AddTracksRequested   += id => AddTracksToPlaylistById(id);
                _playlistsPageWired = true;
            }
            RefreshPlaylistsPageCards();
            // Always rebuild the "Merge with…" candidate list — it depends on existing playlists.
            PlaylistsPageOverlay.SetAllPlaylists(_storage.LoadPlaylists().Select(p => (p.Id, p.Name)));
            PlaylistsPageOverlay.Visibility = Visibility.Visible;
            PlaylistsPageOverlay.AnimateIn();
            // Tab state: Playlists active, All tracks inactive
            SetTabActive(BtnTabAll, false);
            SetTabActive(BtnTabPlaylists, true);
        }
        else
        {
            PlaylistsPageOverlay.AnimateOut(() => PlaylistsPageOverlay.Visibility = Visibility.Collapsed);
            SetTabActive(BtnTabPlaylists, false);
            // Restore the "All tracks" highlight if we're back to the library view
            if (_currentPlaylistId == null) SetTabActive(BtnTabAll, true);
        }
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

    // ── Action helpers used by the Playlists page cards (right-click menu) ────
    // Each helper looks up the playlist by id, performs the action, and refreshes
    // the cards grid so the user sees the change without leaving the page.
    private (long Id, string Name)? FindPlaylistById(long id)
    {
        var p = _storage.LoadPlaylists().FirstOrDefault(x => x.Id == id);
        return p == null ? null : (p.Id, p.Name);
    }

    private void RenamePlaylistById(long id)
    {
        if (FindPlaylistById(id) is not (long pid, string oldName)) return;
        InputOverlay.Show(Localization.T("RenamePlaylistTitle"), Localization.T("PlaylistNamePh"), oldName, name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _storage.RenamePlaylist(pid, name.Trim());
            if (_currentPlaylistId == pid) TxtCollectionName.Text = name.Trim();
            RefreshPlaylistsUi();
            RefreshPlaylistsPageCards();
        });
    }

    private void DeletePlaylistById(long id)
    {
        if (FindPlaylistById(id) is not (long pid, string name)) return;
        ConfirmOverlay.Show(
            Localization.T("ConfirmDeletePlaylistTitle"),
            string.Format(Localization.T("ConfirmDeletePlaylistMsg"), name),
            Localization.T("PlaylistDelete"),
            ok =>
            {
                if (!ok) return;
                _storage.DeletePlaylist(pid);
                if (_currentPlaylistId == pid) ShowAllTracks();
                RefreshPlaylistsUi();
                RefreshPlaylistsPageCards();
            });
    }

    private void ExportPlaylistById(long id)
    {
        if (FindPlaylistById(id) is not (long pid, string name)) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.T("PlaylistExport"),
            FileName = FileManager.Sanitize(name) + ".m3u8",
            Filter = "Playlist (*.m3u8;*.m3u)|*.m3u8;*.m3u|All files (*.*)|*.*",
            DefaultExt = ".m3u8",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = new List<string> { "#EXTM3U" };
            lines.AddRange(_storage.LoadPlaylistPaths(pid));
            File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
            ToastView.Show(string.Format(Localization.T("ToastPlaylistExported"), name));
        }
        catch (Exception ex) { Log.Error("m3u export failed", ex); ToastView.Show(Localization.T("ToastExportFailed")); }
    }

    private void MergePlaylistsById(long sourceId, long targetId)
    {
        if (FindPlaylistById(targetId) is not (long _, string targetName)) return;
        MergePlaylists(sourceId, targetId, targetName);
    }

    /// <summary>Open the "add tracks" picker for a playlist without navigating away
    /// from the Playlists page — temporarily sets the current playlist id, opens the
    /// existing picker, and refreshes the page when the picker closes.</summary>
    private void AddTracksToPlaylistById(long id)
    {
        if (FindPlaylistById(id) is not (long pid, string name)) return;
        // Re-use the existing in-playlist picker by temporarily pointing _currentPlaylistId at it.
        // The picker only reads _currentPlaylistId via OnPlaylistToggleTrack; no other side effects.
        var prev = _currentPlaylistId;
        _currentPlaylistId = pid;
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
        PlaylistAddOverlay.SetItems(name, items);
        PlaylistAddOverlay.Visibility = Visibility.Visible;
        PlaylistAddOverlay.AnimateIn();
        // Hook a one-shot Closed handler that restores the previous current id and refreshes cards.
        Action? onClose = null;
        onClose = () =>
        {
            _currentPlaylistId = prev;
            RefreshPlaylistsUi();
            RefreshPlaylistsPageCards();
            if (onClose != null) PlaylistAddOverlay.Closed -= onClose;
        };
        PlaylistAddOverlay.Closed += onClose;
    }

    /// <summary>Switch the main view to a playlist.</summary>
    private void SelectPlaylist(PlaylistVm vm)
    {
        if (_playlistsPageOpen) TogglePlaylistsPage(false);
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
        if (_playlistsPageOpen) TogglePlaylistsPage(false);
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

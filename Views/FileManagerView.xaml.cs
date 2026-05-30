using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SoundCheck.Services;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck.Views;

public partial class FileManagerView : UserControl
{
    public event Action? Closed;
    /// <summary>Fires after files were renamed on disk — (oldPath, newPath) pairs.</summary>
    public event Action<List<(string oldPath, string newPath)>>? FilesRenamed;
    /// <summary>User asked to edit a file's tags — host opens the shared tag editor.</summary>
    public event Action<string>? EditTagsRequested;

    private readonly List<FileVM> _allVMs = new();
    private bool _ready;
    private bool _suppressSelectAll;
    private bool _previewMode;

    public FileManagerView()
    {
        InitializeComponent();
        CmbFormat.ItemsSource = FileManager.Formats;
        CmbFormat.SelectedIndex = 0;
        _ready = true;
        UpdateSelectionUi();
        UpdatePreviewLine();
    }

    private string CurrentFormat() => CmbFormat.SelectedItem as string ?? FileManager.Formats[0];

    // ─── Row / group view-models ──────────────────────────────────────────
    public class FileVM : INotifyPropertyChanged
    {
        public FileManager.AudioItem Item;
        public FileVM(FileManager.AudioItem it) { Item = it; _proposed = it.FileName; }

        private string _proposed;
        public string Proposed { get => _proposed; set { _proposed = value; Refresh(); } }

        private bool _preview;
        public bool PreviewMode { get => _preview; set { _preview = value; Refresh(); } }

        public string Display => _preview ? _proposed : Item.FileName;
        public bool Highlight => _preview && !string.Equals(_proposed, Item.FileName, StringComparison.Ordinal);
        public string SizeText => FileManager.FormatSize(Item.SizeBytes);

        private bool _sel;
        public bool IsSelected { get => _sel; set { if (_sel != value) { _sel = value; OnPC(nameof(IsSelected)); } } }

        // Inline single-file rename: when on, the row shows an editable text box.
        private bool _editing;
        public bool IsEditing { get => _editing; set { _editing = value; OnPC(nameof(IsEditing)); OnPC(nameof(NotEditing)); } }
        public bool NotEditing => !_editing;

        private string _editName = "";
        public string EditName { get => _editName; set { _editName = value; OnPC(nameof(EditName)); } }

        private void Refresh() { OnPC(nameof(Display)); OnPC(nameof(Highlight)); }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class FileGroup : INotifyPropertyChanged
    {
        public string FolderName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public ObservableCollection<FileVM> Files { get; } = new();
        public int Count => Files.Count;
        private bool _exp;   // collapsed by default
        public bool IsExpanded { get => _exp; set { _exp = value; OnPC(nameof(IsExpanded)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ─── Folder loading ───────────────────────────────────────────────────
    /// <summary>Open the view on a folder (called by MainWindow). Pass null to keep last.</summary>
    public void OpenFolder(string? initial)
    {
        if (!string.IsNullOrWhiteSpace(initial))
        {
            TxtPath.Text = initial;
            LoadFolder(initial!);
        }
    }

    private async void LoadFolder(string path)
    {
        TxtStatus.Text = Localization.T("FmScanning");
        var items = await Task.Run(() => FileManager.Scan(path));
        BuildGroups(items);
        TxtStatus.Text = "";
    }

    private void BuildGroups(List<FileManager.AudioItem> items)
    {
        // Preserve each folder's expand/collapse state across a refresh (keyed by path).
        var prevState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (GroupsList.ItemsSource is IEnumerable<FileGroup> oldGroups)
            foreach (var g in oldGroups) prevState[g.FolderPath] = g.IsExpanded;

        _allVMs.Clear();
        var groups = items
            .GroupBy(i => i.Folder)
            .OrderBy(g => g.First().FolderName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var fg = new FileGroup
                {
                    FolderName = g.First().FolderName,
                    FolderPath = g.Key,
                    // Collapsed by default; keep the previous state on refresh.
                    IsExpanded = prevState.TryGetValue(g.Key, out var ex) && ex,
                };
                foreach (var it in g.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
                {
                    var vm = new FileVM(it);
                    vm.PropertyChanged += VM_PropertyChanged;
                    fg.Files.Add(vm);
                    _allVMs.Add(vm);
                }
                return fg;
            })
            .ToList();

        GroupsList.ItemsSource = groups;
        RunTrackCount.Text = _allVMs.Count.ToString();
        TxtEmpty.Visibility = _allVMs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _previewMode = false;
        RecomputeProposed();
        UpdatePreviewLine();
        UpdateSelectionUi();
    }

    private void VM_PropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileVM.IsSelected)) UpdateSelectionUi();
    }

    // ─── Format / preview ─────────────────────────────────────────────────
    private void RecomputeProposed()
    {
        string fmt = CurrentFormat();
        foreach (var vm in _allVMs)
        {
            vm.Proposed = FileManager.BuildName(vm.Item, fmt);
            vm.PreviewMode = _previewMode;
        }
    }

    private void UpdatePreviewLine()
    {
        string fmt = CurrentFormat();
        var sample = _allVMs.FirstOrDefault();
        if (sample != null)
        {
            RunPreview.Text = FileManager.BuildName(sample.Item, fmt);
        }
        else
        {
            // Synthetic sample so the format is illustrated even with no folder loaded.
            var demo = new FileManager.AudioItem
            {
                Title = "All The Stars", Artist = "Kendrick Lamar, SZA", Album = "Black Panther", Ext = ".flac",
                FileName = "sample.flac",
            };
            RunPreview.Text = FileManager.BuildName(demo, fmt);
        }
    }

    private void CmbFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        RecomputeProposed();
        UpdatePreviewLine();
    }

    private void BtnFormatReset_Click(object sender, RoutedEventArgs e)
    {
        CmbFormat.SelectedIndex = 0;
    }

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        _previewMode = !_previewMode;
        foreach (var vm in _allVMs) vm.PreviewMode = _previewMode;
    }

    private void SetPreview(bool on)
    {
        _previewMode = on;
        foreach (var vm in _allVMs) vm.PreviewMode = on;
    }

    // ─── Selection ────────────────────────────────────────────────────────
    private void SelectAll_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAll) return;
        bool on = ChkSelectAll.IsChecked == true;
        foreach (var vm in _allVMs) vm.IsSelected = on;
    }

    private void UpdateSelectionUi()
    {
        int total = _allVMs.Count;
        int sel = _allVMs.Count(v => v.IsSelected);
        TxtSelCount.Text = string.Format(Localization.T("FmSelectedFmt"), sel, total);
        _suppressSelectAll = true;
        ChkSelectAll.IsChecked = total > 0 && sel == total;
        _suppressSelectAll = false;
        BtnRename.IsEnabled = sel > 0;
    }

    // ─── Group expand/collapse ────────────────────────────────────────────
    private void Group_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileGroup g)
            g.IsExpanded = !g.IsExpanded;
    }

    // ─── Path / browse / refresh ──────────────────────────────────────────
    private void TxtPath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtPathPh != null)
            TxtPathPh.Visibility = string.IsNullOrEmpty(TxtPath.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(TxtPath.Text))
        {
            LoadFolder(TxtPath.Text.Trim());
            e.Handled = true;
        }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Localization.T("FmTitle"),
            InitialDirectory = Directory.Exists(TxtPath.Text) ? TxtPath.Text : "",
        };
        if (dlg.ShowDialog() == true)
        {
            TxtPath.Text = dlg.FolderName;
            LoadFolder(dlg.FolderName);
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtPath.Text)) LoadFolder(TxtPath.Text.Trim());
    }

    /// <summary>Re-scan the current folder (e.g. after tags were edited externally).</summary>
    public void RefreshCurrent()
    {
        if (!string.IsNullOrWhiteSpace(TxtPath.Text)) LoadFolder(TxtPath.Text.Trim());
    }

    // ─── Edit tags ────────────────────────────────────────────────────────
    private void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileVM vm)
            EditTagsRequested?.Invoke(vm.Item.FullPath);
        e.Handled = true;
    }

    // ─── Inline single-file rename (rename the file itself, not its tags) ──
    /// <summary>Pencil button on a row → switch that row into an editable text box.</summary>
    private void RenameFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileVM vm)
        {
            // Cancel any other in-progress edit so only one row is editable at a time.
            foreach (var other in _allVMs) if (other != vm) other.IsEditing = false;
            vm.EditName = vm.Item.FileName;
            vm.IsEditing = true;
        }
        e.Handled = true;
    }

    /// <summary>Focus the box and pre-select the stem (extension stays put) when it becomes visible.</summary>
    private void RenameBox_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox tb || !tb.IsVisible) return;
        // Defer so focus lands after the layout pass that made the box visible.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            tb.Focus();
            int dot = tb.Text.LastIndexOf('.');
            if (dot > 0) tb.Select(0, dot); else tb.SelectAll();
        }));
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not FileVM vm) return;
        if (e.Key == Key.Enter)       { CommitInlineRename(vm, tb.Text); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.IsEditing = false;            e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is FileVM vm && vm.IsEditing)
            CommitInlineRename(vm, tb.Text);
    }

    private void CommitInlineRename(FileVM vm, string typed)
    {
        vm.IsEditing = false;
        typed = (typed ?? "").Trim();
        if (string.IsNullOrEmpty(typed)) return;

        // If the user didn't include an extension, keep the original one.
        if (string.IsNullOrEmpty(Path.GetExtension(typed))) typed += vm.Item.Ext;

        // Sanitize the stem but preserve the extension verbatim.
        string ext  = Path.GetExtension(typed);
        string stem = Path.GetFileNameWithoutExtension(typed);
        string newName = FileManager.Sanitize(stem) + ext;

        if (string.Equals(newName, vm.Item.FileName, StringComparison.Ordinal)) return;

        string oldPath = vm.Item.FullPath;
        string? res = FileManager.Rename(oldPath, newName);
        if (res != null)
        {
            vm.Item.FullPath = res;
            vm.Item.FileName = Path.GetFileName(res);
            vm.Proposed = vm.Item.FileName;
            FilesRenamed?.Invoke(new List<(string, string)> { (oldPath, res) });
            TxtStatus.Text = string.Format(Localization.T("FmRenamedFmt"), 1);
        }
        else
        {
            TxtStatus.Text = string.Format(Localization.T("FmRenameFailFmt"), 1);
        }
    }

    // ─── Rename ───────────────────────────────────────────────────────────
    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        var sel = _allVMs.Where(v => v.IsSelected).ToList();
        if (sel.Count == 0) { TxtStatus.Text = Localization.T("FmNothingSelected"); return; }

        string fmt = CurrentFormat();
        var map = new List<(string, string)>();
        int ok = 0, fail = 0;

        foreach (var vm in sel)
        {
            string newName = FileManager.BuildName(vm.Item, fmt);
            if (string.Equals(newName, vm.Item.FileName, StringComparison.Ordinal)) continue; // already correct

            string? res = FileManager.Rename(vm.Item.FullPath, newName);
            if (res != null)
            {
                map.Add((vm.Item.FullPath, res));
                vm.Item.FullPath = res;
                vm.Item.FileName = Path.GetFileName(res);
                vm.Proposed = vm.Item.FileName;
                ok++;
            }
            else fail++;
        }

        if (map.Count > 0) FilesRenamed?.Invoke(map);

        SetPreview(false);
        UpdateSelectionUi();
        string msg = string.Format(Localization.T("FmRenamedFmt"), ok);
        if (fail > 0) msg += " · " + string.Format(Localization.T("FmRenameFailFmt"), fail);
        TxtStatus.Text = msg;
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

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SoundCheck.Services;
using Localization = SoundCheck.Services.Localization;

namespace SoundCheck.Views;

public partial class SettingsOverlay : UserControl
{
    public event Action? Closed;
    /// <summary>User requested deleting the whole library.db file.</summary>
    public event Action? DeleteDbRequested;
    /// <summary>User picked a backup file to restore from (path).</summary>
    public event Action<string>? ImportDbRequested;
    /// <summary>User wants to open the Logs window.</summary>
    public event Action? LogsRequested;
    /// <summary>User wants to purge tracks whose files no longer exist on disk.</summary>
    public event Action? CleanMissingRequested;
    /// <summary>Equalizer enable/gains changed — host applies it live to playback.</summary>
    public event Action<bool, float[]>? EqualizerChanged;

    private bool _loaded;
    private Slider[] _eqSliders = Array.Empty<Slider>();
    private TextBlock[] _eqValueLabels = Array.Empty<TextBlock>();

    /// <summary>A named accent preset shown in the color dropdown.</summary>
    public class AccentOption
    {
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "";
        public System.Windows.Media.Brush Brush { get; set; } = System.Windows.Media.Brushes.Gray;
        public AccentOption() { }
        public AccentOption(string name, string hex)
        {
            Name = name; Hex = hex;
            try { Brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); } catch { }
        }
    }

    private readonly List<AccentOption> _accentOptions = new()
    {
        new("Gold",     "#C8A96E"),
        new("Red",      "#E05555"),
        new("Crimson",  "#E5446D"),
        new("Rose",     "#E06AA8"),
        new("Pink",     "#EA86B6"),
        new("Magenta",  "#CF5BC0"),
        new("Violet",   "#9B6DE0"),
        new("Indigo",   "#7E5FE0"),
        new("Blue",     "#5570E8"),
        new("Sky",      "#5B8DEF"),
        new("Cyan",     "#45C2D4"),
        new("Teal",     "#4FB3B0"),
        new("Mint",     "#4FC98A"),
        new("Green",    "#5FB87A"),
        new("Lime",     "#9FCF4F"),
        new("Yellow",   "#E0C84F"),
        new("Amber",    "#E0A83C"),
        new("Orange",   "#E0883B"),
        new("Coral",    "#E07A55"),
        new("Slate",    "#8A93A0"),
    };

    public SettingsOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();

        // Live-bind every toggle/chip → AppSettings setter
        ChkRemember.Checked    += (_, _) => Push(() => AppSettings.RememberPosition = true);
        ChkRemember.Unchecked  += (_, _) => Push(() => AppSettings.RememberPosition = false);
        ChkParticles.Checked   += (_, _) => Push(() => AppSettings.ParticlesEnabled = true);
        ChkParticles.Unchecked += (_, _) => Push(() => AppSettings.ParticlesEnabled = false);
        ChkFloatBg.Checked     += (_, _) => Push(() => AppSettings.FloatingBgEnabled = true);
        ChkFloatBg.Unchecked   += (_, _) => Push(() => AppSettings.FloatingBgEnabled = false);
        ChkLogoEq.Checked      += (_, _) => Push(() => AppSettings.LogoEqualizerEnabled = true);
        ChkLogoEq.Unchecked    += (_, _) => Push(() => AppSettings.LogoEqualizerEnabled = false);
        ChkBlurBg.Checked      += (_, _) => Push(() => AppSettings.BlurBgEnabled = true);
        ChkBlurBg.Unchecked    += (_, _) => Push(() => AppSettings.BlurBgEnabled = false);
        ChkReduceMotion.Checked   += (_, _) => Push(() => AppSettings.ReduceMotion = true);
        ChkReduceMotion.Unchecked += (_, _) => Push(() => AppSettings.ReduceMotion = false);
        ChkAccentCover.Checked   += (_, _) => Push(() => { AppSettings.AccentFromCover = true;  UpdateAccentPaletteState(); });
        ChkAccentCover.Unchecked += (_, _) => Push(() => { AppSettings.AccentFromCover = false; UpdateAccentPaletteState(); });
        CmbAccent.ItemsSource = _accentOptions;
        ChkAutoStart.Checked   += (_, _) => Push(() => AppSettings.AutoStart = true);
        ChkAutoStart.Unchecked += (_, _) => Push(() => AppSettings.AutoStart = false);
        ChkCloseToTray.Checked   += (_, _) => Push(() => AppSettings.CloseToTray = true);
        ChkCloseToTray.Unchecked += (_, _) => Push(() => AppSettings.CloseToTray = false);
        ChkMinToTray.Checked   += (_, _) => Push(() => AppSettings.MinimizeToTray = true);
        ChkMinToTray.Unchecked += (_, _) => Push(() => AppSettings.MinimizeToTray = false);

        foreach (RadioButton rb in GrpCrossfade.Children)
            rb.Checked += (_, _) => Push(() => { if (rb.Tag is string t && int.TryParse(t, out var n)) AppSettings.CrossfadeSeconds = n; });

        // Visual preset chips — apply a bundle of UI toggles in one click, then refresh
        // the toggles below so the user sees what just changed.
        foreach (RadioButton rb in GrpPreset.Children)
            rb.Checked += (_, _) => Push(() =>
            {
                if (rb.Tag is not string tag) return;
                AppSettings.ApplyVisualPreset(tag);
                Reload();
            });

        BuildEqBands();
        ChkEq.Checked   += (_, _) => Push(() => { UpdateEqPanelState(); RaiseEq(); });
        ChkEq.Unchecked += (_, _) => Push(() => { UpdateEqPanelState(); RaiseEq(); });
        // Language radio chips — switch on click, then Reload to refresh code-managed strings.
        foreach (RadioButton rb in GrpLang.Children)
            rb.Checked += (_, _) => Push(() =>
            {
                if (rb.Tag is not string code) return;
                AppSettings.Language = code;
                Localization.SetLanguage(code);
                Reload();
            });

        NavData.IsChecked = true;   // default tab
    }

    // ─── Left-rail navigation ─────────────────────────────────────────────
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag) ShowPane(tag);
    }

    private void ShowPane(string tag)
    {
        if (PaneData == null) return; // template not built yet
        PaneData.Visibility      = tag == "data"      ? Visibility.Visible : Visibility.Collapsed;
        PanePlayback.Visibility  = tag == "playback"  ? Visibility.Visible : Visibility.Collapsed;
        PaneInterface.Visibility = tag == "interface" ? Visibility.Visible : Visibility.Collapsed;
        PaneSystem.Visibility    = tag == "system"    ? Visibility.Visible : Visibility.Collapsed;
        PaneLanguage.Visibility  = tag == "language"  ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Pull current AppSettings values into the UI controls. Skips Push() while doing it.</summary>
    public void Reload()
    {
        _loaded = false;
        try
        {
            ChkRemember.IsChecked    = AppSettings.RememberPosition;
            ChkParticles.IsChecked   = AppSettings.ParticlesEnabled;
            ChkFloatBg.IsChecked     = AppSettings.FloatingBgEnabled;
            ChkLogoEq.IsChecked      = AppSettings.LogoEqualizerEnabled;
            ChkBlurBg.IsChecked      = AppSettings.BlurBgEnabled;
            ChkReduceMotion.IsChecked = AppSettings.ReduceMotion;
            SelectChipString(GrpPreset, AppSettings.CurrentVisualPreset);
            ChkAccentCover.IsChecked = AppSettings.AccentFromCover;
            SelectAccent(AppSettings.AccentColor);
            UpdateAccentPaletteState();
            ChkAutoStart.IsChecked   = AppSettings.AutoStart;
            ChkCloseToTray.IsChecked = AppSettings.CloseToTray;
            ChkMinToTray.IsChecked   = AppSettings.MinimizeToTray;
            SelectChip(GrpCrossfade, AppSettings.CrossfadeSeconds);
            ChkEq.IsChecked = AppSettings.EqualizerEnabled;
            var gains = AppSettings.EqualizerBands;
            for (int i = 0; i < _eqSliders.Length && i < gains.Length; i++)
            {
                _eqSliders[i].Value = gains[i];
                UpdateEqReadout(i);
            }
            bool hasCustom = AppSettings.HasEqCustomPreset;
            BtnEqCustom.IsEnabled = hasCustom;
            BtnEqCustom.Opacity = hasCustom ? 1.0 : 0.4;
            UpdateEqPanelState();
            SelectChipString(GrpLang, Localization.Current);
            // Data section — shows the *current* dir (may be overridden via bootstrap file)
            TxtLibPath.Text = Storage.GetLibraryDir();
            try
            {
                var fi = new FileInfo(Path.Combine(TxtLibPath.Text, "library.db"));
                if (fi.Exists)
                {
                    double kb = fi.Length / 1024.0;
                    string unitKb = Localization.Current == Localization.En ? "KB" : "КБ";
                    string unitMb = Localization.Current == Localization.En ? "MB" : "МБ";
                    TxtLibStats.Text = kb < 1024 ? $"library.db · {kb:F0} {unitKb}" : $"library.db · {kb / 1024:F1} {unitMb}";
                }
                else TxtLibStats.Text = Localization.T("LibStatsNoDb");
            }
            catch { TxtLibStats.Text = "—"; }
        }
        finally { _loaded = true; }
    }

    /// <summary>Pick a new folder, optionally migrate library.db, save override, prompt restart.</summary>
    private void BtnChangeLib_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку для хранения библиотеки",
            InitialDirectory = TxtLibPath.Text,
        };
        if (dlg.ShowDialog() != true) return;
        string newDir = dlg.FolderName;
        string oldDir = Storage.GetLibraryDir();
        if (string.Equals(newDir, oldDir, StringComparison.OrdinalIgnoreCase)) return;

        // If the user has an existing library, offer to move it so they don't
        // lose their tracks/history/stats.
        string oldDb = Path.Combine(oldDir, "library.db");
        if (File.Exists(oldDb))
        {
            var res = MessageBox.Show(
                $"Скопировать существующую библиотеку в новое место?\n\n" +
                $"Откуда:\n{oldDir}\n\nКуда:\n{newDir}\n\n" +
                $"«Да» — данные сохранятся и сразу появятся после перезапуска.\n" +
                $"«Нет» — переключиться на чистую библиотеку (старая останется на прежнем месте).",
                "Перенос библиотеки",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (res == MessageBoxResult.Cancel) return;
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    Directory.CreateDirectory(newDir);
                    File.Copy(oldDb, Path.Combine(newDir, "library.db"), overwrite: true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось скопировать: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }

        Storage.SetLibraryDirOverride(newDir);
        TxtLibPath.Text = newDir;
        MessageBox.Show(
            "Папка библиотеки изменена.\nПерезапустите плеер чтобы применить.",
            "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Push(Action setter)
    {
        if (!_loaded) return;
        setter();
    }

    private static void SelectChip(StackPanel host, int value)
    {
        foreach (RadioButton rb in host.Children)
            rb.IsChecked = rb.Tag is string s && int.TryParse(s, out var n) && n == value;
    }

    private static void SelectChipString(StackPanel host, string value)
    {
        foreach (RadioButton rb in host.Children)
            rb.IsChecked = rb.Tag is string s && s == value;
    }

    /// <summary>Select the dropdown option matching the saved accent. Empty/unknown → default gold.</summary>
    private void SelectAccent(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) hex = "#C8A96E"; // default gold
        var opt = _accentOptions.FirstOrDefault(o => string.Equals(o.Hex, hex, StringComparison.OrdinalIgnoreCase))
                  ?? _accentOptions[0];
        CmbAccent.SelectedItem = opt;
    }

    private void CmbAccent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (CmbAccent.SelectedItem is AccentOption o) AppSettings.AccentColor = o.Hex;
    }

    /// <summary>Palette is only relevant when the accent is fixed (not following the cover).</summary>
    private void UpdateAccentPaletteState()
    {
        bool manual = ChkAccentCover.IsChecked != true;
        AccentPaletteRow.IsEnabled = manual;
        AccentPaletteRow.Opacity = manual ? 1.0 : 0.4;
    }

    // ─── Equalizer ────────────────────────────────────────────────────────
    private void BuildEqBands()
    {
        var centres = EqualizerSampleProvider.Centres;
        _eqSliders = new Slider[centres.Length];
        _eqValueLabels = new TextBlock[centres.Length];
        EqBandsHost.Children.Clear();
        for (int i = 0; i < centres.Length; i++)
        {
            var col = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Center };

            // dB readout above the slider
            var val = new TextBlock
            {
                Foreground = (System.Windows.Media.Brush)FindResource("T2"),
                FontSize = 9, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            _eqValueLabels[i] = val;

            var sld = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -12, Maximum = 12, Value = 0,
                Height = 120, Width = 26,
                SmallChange = 1, LargeChange = 3,
                TickFrequency = 6, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = Localization.T("EqResetHint"),
            };
            int idx = i;
            sld.ValueChanged += (_, _) => { UpdateEqReadout(idx); Push(RaiseEq); };
            sld.MouseDoubleClick += (_, e) => { sld.Value = 0; e.Handled = true; }; // reset this band
            _eqSliders[i] = sld;

            var freq = new TextBlock
            {
                Text = FreqLabel(centres[i]),
                Foreground = (System.Windows.Media.Brush)FindResource("T3"),
                FontSize = 9, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
            };
            col.Children.Add(val);
            col.Children.Add(sld);
            col.Children.Add(freq);
            EqBandsHost.Children.Add(col);
            UpdateEqReadout(i);
        }
    }

    private void UpdateEqReadout(int i)
    {
        if (i < 0 || i >= _eqValueLabels.Length) return;
        int db = (int)Math.Round(_eqSliders[i].Value);
        _eqValueLabels[i].Text = db > 0 ? $"+{db}" : db.ToString();
    }

    private static string FreqLabel(float hz) => hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}";

    private float[] CurrentEqGains()
    {
        var g = new float[_eqSliders.Length];
        for (int i = 0; i < _eqSliders.Length; i++) g[i] = (float)_eqSliders[i].Value;
        return g;
    }

    private void RaiseEq()
    {
        bool on = ChkEq.IsChecked == true;
        var gains = CurrentEqGains();
        AppSettings.PersistEqualizer(on, gains); // silent — doesn't trigger full ApplySettings
        EqualizerChanged?.Invoke(on, gains);     // live apply by the host
    }

    private void UpdateEqPanelState()
    {
        bool on = ChkEq.IsChecked == true;
        EqPanel.IsEnabled = on;
        EqPanel.Opacity = on ? 1.0 : 0.4;
    }

    private void EqPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string preset) return;
        // 10 bands: 31 62 125 250 500 1k 2k 4k 8k 16k
        float[] g = preset switch
        {
            "bass"   => new float[] {  6f,  5f,  4f,  2f,  0f,  0f,  0f,  0f,  0f,  0f },
            "vocal"  => new float[] { -2f, -1f,  0f,  2f,  4f,  4f,  3f,  1f,  0f, -1f },
            "treble" => new float[] {  0f,  0f,  0f,  0f,  0f,  1f,  3f,  5f,  6f,  7f },
            "custom" => AppSettings.EqCustomPreset,
            _         => new float[10], // flat
        };
        for (int i = 0; i < _eqSliders.Length && i < g.Length; i++) _eqSliders[i].Value = g[i];
        // ValueChanged handlers fire RaiseEq for each slider; ensure one final raise too.
        Push(RaiseEq);
    }

    private void EqSaveCustom_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.EqCustomPreset = CurrentEqGains();
        BtnEqCustom.IsEnabled = true;
        BtnEqCustom.Opacity = 1.0;
    }

    // ─── Reset handlers ───────────────────────────────────────────────────
    private void BtnResetData_Click(object sender, RoutedEventArgs e)
    {
        // Restore default library location. Doesn't move existing files — just
        // clears the override so next launch uses %AppData%\SoundCheck.
        if (Storage.GetLibraryDir() == Storage.DefaultLibraryDir) return;
        Storage.SetLibraryDirOverride(null);
        Reload();
        MessageBox.Show(
            "Папка библиотеки сброшена к стандартной.\nПерезапустите плеер чтобы применить.",
            "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void BtnResetPlay_Click(object sender, RoutedEventArgs e) { AppSettings.ResetCategory("play."); Reload(); }
    private void BtnResetUi_Click  (object sender, RoutedEventArgs e) { AppSettings.ResetCategory("ui.");   Reload(); }
    private void BtnResetSys_Click (object sender, RoutedEventArgs e) { AppSettings.ResetCategory("sys.");  Reload(); }
    private void BtnResetAll_Click (object sender, RoutedEventArgs e) { AppSettings.ResetAll(); Reload(); }

    // ─── Library actions ──────────────────────────────────────────────────
    private void BtnOpenLib_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TxtLibPath.Text}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void BtnDeleteDb_Click(object sender, RoutedEventArgs e) => DeleteDbRequested?.Invoke();
    private void BtnLogs_Click(object sender, RoutedEventArgs e) => LogsRequested?.Invoke();
    private void BtnCleanMissing_Click(object sender, RoutedEventArgs e) => CleanMissingRequested?.Invoke();

    private void BtnExportDb_Click(object sender, RoutedEventArgs e)
    {
        string src = Path.Combine(Storage.GetLibraryDir(), "library.db");
        if (!File.Exists(src))
        {
            MessageBox.Show(Localization.T("LibStatsNoDb"), Localization.T("BackupData"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.T("BackupData"),
            FileName = $"soundcheck-backup-{DateTime.Now:yyyy-MM-dd}.db",
            Filter = "SoundCheck library (*.db)|*.db|All files (*.*)|*.*",
            DefaultExt = ".db",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.Copy(src, dlg.FileName, overwrite: true);
            MessageBox.Show(string.Format(Localization.T("BackupSavedFmt"), dlg.FileName), Localization.T("BackupData"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Localization.T("BackupSaveFailFmt"), ex.Message), Localization.T("BackupData"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnImportDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("BackupData"),
            Filter = "SoundCheck library (*.db)|*.db|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) ImportDbRequested?.Invoke(dlg.FileName);
    }

    // ─── Open / close animations (matches Profile/Help style) ─────────────
    public void AnimateIn()
    {
        NavData.IsChecked = true;   // always open on the first tab
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

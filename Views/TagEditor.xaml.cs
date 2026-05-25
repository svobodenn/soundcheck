using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SoundCheck.Views;

public partial class TagEditor : UserControl
{
    /// <summary>Result of an edit. <see cref="CoverChanged"/> distinguishes "left the
    /// cover alone" from "set/removed it" so we only rewrite art when needed.</summary>
    public class Result
    {
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public bool CoverChanged;
        public byte[]? CoverBytes; // null + CoverChanged => remove cover
        public bool IsExplicit;
    }

    private Action<Result?>? _onResult;
    private bool _coverChanged;
    private byte[]? _coverBytes;

    public TagEditor() { InitializeComponent(); }

    /// <summary>Open the editor pre-filled with a track's current tags + cover.</summary>
    public void Show(string fileName, string title, string artist, string album,
                     byte[]? coverBytes, bool isExplicit, Action<Result?> onResult)
    {
        TxtFile.Text = fileName;
        TxtTitle.Text = title ?? "";
        TxtArtist.Text = artist ?? "";
        TxtAlbum.Text = album ?? "";
        ChkExplicit.IsChecked = isExplicit;
        _onResult = onResult;
        _coverChanged = false;
        _coverBytes = coverBytes;
        SetCoverPreview(coverBytes);

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        TeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        TeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
        TxtTitle.Focus();
        TxtTitle.SelectAll();
    }

    private void SetCoverPreview(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) { ImgCover.Source = null; return; }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 144;
            bmp.EndInit();
            bmp.Freeze();
            ImgCover.Source = bmp;
        }
        catch { ImgCover.Source = null; }
    }

    private void ChangeCover_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Cover image",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            _coverBytes = bytes;
            _coverChanged = true;
            SetCoverPreview(bytes);
        }
        catch { /* unreadable image — ignore */ }
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        _coverBytes = null;
        _coverChanged = true;
        SetCoverPreview(null);
    }

    private void Close(Result? result)
    {
        var cb = _onResult;
        _onResult = null;
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160) };
        fade.Completed += (_, _) => { Visibility = Visibility.Collapsed; cb?.Invoke(result); };
        BeginAnimation(OpacityProperty, fade);
        var sx = new DoubleAnimation { To = 0.94, Duration = TimeSpan.FromMilliseconds(160) };
        TeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        TeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sx);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
        => Close(new Result
        {
            Title = TxtTitle.Text.Trim(),
            Artist = TxtArtist.Text.Trim(),
            Album = TxtAlbum.Text.Trim(),
            CoverChanged = _coverChanged,
            CoverBytes = _coverBytes,
            IsExplicit = ChkExplicit.IsChecked == true,
        });

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(null);

    // Click on the dark backdrop (outside the card) cancels; clicks on the card don't bubble.
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => Close(null);
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        else if (e.Key == Key.Enter) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

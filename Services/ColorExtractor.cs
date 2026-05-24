using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoundCheck.Services;

/// <summary>
/// Extracts dominant saturated accent color from cover bytes.
/// Mirrors the JS algorithm: bin by RGB high-bits, weigh by saturation × count,
/// brighten if too dim.
/// </summary>
public static class ColorExtractor
{
    public static Color? Extract(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            // Decode to small bitmap
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.DecodePixelWidth = 48;
            src.StreamSource = new MemoryStream(bytes);
            src.EndInit();
            src.Freeze();

            int w = src.PixelWidth;
            int h = src.PixelHeight;
            if (w == 0 || h == 0) return null;

            var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int stride = w * 4;
            byte[] px = new byte[h * stride];
            converted.CopyPixels(px, stride, 0);

            // Bin by (r>>5, g>>5, b>>5)
            var bins = new Dictionary<int, (long r, long g, long b, long n, int sat)>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                    if (a < 200) continue;
                    int mx = Math.Max(r, Math.Max(g, b));
                    int mn = Math.Min(r, Math.Min(g, b));
                    // Allow darker pixels (was 45), keep upper guard a bit lower
                    if (mx < 25 || mn > 235) continue;
                    int sat = mx - mn;
                    // Accept lower saturation (was 25) so we get a color from muted covers too
                    if (sat < 12) continue;
                    int key = ((r >> 5) << 10) | ((g >> 5) << 5) | (b >> 5);
                    if (bins.TryGetValue(key, out var v))
                        bins[key] = (v.r + r, v.g + g, v.b + b, v.n + 1, Math.Max(v.sat, sat));
                    else
                        bins[key] = (r, g, b, 1, sat);
                }
            }

            (long r, long g, long b, long n, int sat) best = default;
            long bestScore = 0;
            foreach (var v in bins.Values)
            {
                long score = v.n * Math.Min(v.sat, 90);
                if (score > bestScore) { bestScore = score; best = v; }
            }
            if (best.n == 0)
            {
                // Fallback: just average all opaque pixels and brighten
                long tr = 0, tg = 0, tb = 0, tn = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * stride + x * 4;
                        if (px[i + 3] < 200) continue;
                        tb += px[i]; tg += px[i + 1]; tr += px[i + 2]; tn++;
                    }
                if (tn == 0) return null;
                best = (tr, tg, tb, tn, 0);
            }

            byte br = (byte)(best.r / best.n);
            byte bg = (byte)(best.g / best.n);
            byte bb = (byte)(best.b / best.n);
            int max = Math.Max(br, Math.Max(bg, bb));
            if (max < 150)
            {
                float boost = 150f / max;
                br = (byte)Math.Min(255, br * boost);
                bg = (byte)Math.Min(255, bg * boost);
                bb = (byte)Math.Min(255, bb * boost);
            }
            return Color.FromRgb(br, bg, bb);
        }
        catch { return null; }
    }

    public static Color Dim(Color c, float factor = 0.55f) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
}

namespace PickMatch.Core;

/// <summary>Decoded picture: premultiplied BGRA, 4 bytes per pixel, rows without padding.</summary>
public sealed class Bitmap32(int width, int height, byte[] bgra)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = bgra;
}

/// <summary>
/// Decodes photos upright and scaled down. On Windows this uses Windows Imaging (so HEIC, AVIF and the Raw Image
/// Extension work); elsewhere SkiaSharp. RAW files are never developed: their embedded JPEG preview is used.
/// </summary>
public static partial class Imaging
{
    /// <summary>Decode a photo with its long side at most <paramref name="longSide"/>, upright.</summary>
    public static async Task<Bitmap32?> DecodeAsync(string path, PhotoInfo info, int longSide, bool best = false, bool allowUpscale = false)
    {
        try
        {
            var blob = best ? info.BestPreview : info.MatchPreview;
            if (blob is { } j && !info.WholeFileIsImage)
            {
                // Container orientation wins; without it, use the preview's own EXIF.
                return await DecodeBytesAsync(ReadBlob(path, j), longSide, info.Orientation, allowUpscale);
            }
            return await DecodeFileAsync(path, longSide, info.WholeFileIsImage ? null : info.Orientation, !info.WholeFileIsImage, allowUpscale);
        }
        catch { return null; }
    }

    static byte[] ReadBlob(string path, JpegBlob j)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long len = Math.Min(j.Length, fs.Length - j.Offset);
        var b = new byte[len];
        fs.Position = j.Offset;
        fs.ReadExactly(b);
        return b;
    }

    public static Gray ToGray(Bitmap32 bmp, int longSide)
    {
        int w = bmp.Width, h = bmp.Height;
        var buf = bmp.Bgra;
        var p = new float[w * h];
        for (int i = 0, j = 0; i < p.Length; i++, j += 4)
            p[i] = 0.114f * buf[j] + 0.587f * buf[j + 1] + 0.299f * buf[j + 2];
        var g = new Gray(w, h, p);
        double k = (double)longSide / Math.Max(w, h);
        return k >= 1 ? g : g.Resize(Math.Max(1, (int)Math.Round(w * k)), Math.Max(1, (int)Math.Round(h * k)));
    }

    static (int w, int h) Target(int w, int h, int longSide, bool allowUpscale)
    {
        double k = Math.Min(allowUpscale ? 3.0 : 1.0, (double)longSide / Math.Max(w, h));
        return (Math.Max(1, (int)Math.Round(w * k)), Math.Max(1, (int)Math.Round(h * k)));
    }
}

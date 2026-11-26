using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TahaCull.Core;

/// <summary>Decoding through Windows Imaging Component: JPEG, PNG, TIFF, HEIC/AVIF/WebP (with the Store extensions) and RAW (Raw Image Extension).</summary>
public static class Imaging
{
    /// <summary>Decode a photo to BGRA with its long side at most <paramref name="longSide"/>, upright.</summary>
    public static async Task<SoftwareBitmap?> DecodeAsync(string path, PhotoInfo info, int longSide, bool best = false, bool allowUpscale = false)
    {
        try
        {
            var blob = best ? info.BestPreview : info.MatchPreview;
            if (blob is { } j && !info.WholeFileIsImage)
            {
                var bytes = ReadBlob(path, j);
                using var ms = new MemoryStream(bytes).AsRandomAccessStream();
                // Container orientation wins; without it, use the preview's own EXIF.
                return await DecodeStreamAsync(ms, longSide, info.Orientation, false, allowUpscale);
            }
            using var fs = File.OpenRead(path).AsRandomAccessStream();
            return await DecodeStreamAsync(fs, longSide, info.WholeFileIsImage ? null : info.Orientation, !info.WholeFileIsImage, allowUpscale);
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

    public static async Task<SoftwareBitmap?> DecodeStreamAsync(IRandomAccessStream stream, int longSide, int? orientation, bool allowPreview = false, bool allowUpscale = false)
    {
        var dec = await BitmapDecoder.CreateAsync(stream);

        // RAW through the Raw Image Extension: its embedded preview is much faster than developing the RAW.
        if (allowPreview)
        {
            try
            {
                using var preview = await dec.GetPreviewAsync();
                if (preview != null && preview.Size > 0)
                {
                    var pdec = await BitmapDecoder.CreateAsync(preview);
                    if (Math.Max(pdec.PixelWidth, pdec.PixelHeight) >= Math.Min(longSide, 1000))
                        return await Render(pdec, longSide, orientation, allowUpscale);
                }
            }
            catch { /* no preview: decode the image itself */ }
        }
        return await Render(dec, longSide, orientation, allowUpscale);
    }

    static async Task<SoftwareBitmap> Render(BitmapDecoder dec, int longSide, int? orientation, bool allowUpscale)
    {
        uint w = dec.PixelWidth, h = dec.PixelHeight;
        double k = Math.Min(allowUpscale ? 3.0 : 1.0, (double)longSide / Math.Max(w, h));
        var tf = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(w * k)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(h * k)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        var mode = ExifOrientationMode.RespectExifOrientation;
        if (orientation is int o)
        {
            mode = ExifOrientationMode.IgnoreExifOrientation;
            // EXIF orientation -> mirror first, then rotate clockwise (same order as BitmapTransform).
            (tf.Flip, tf.Rotation) = o switch
            {
                2 => (BitmapFlip.Horizontal, BitmapRotation.None),
                3 => (BitmapFlip.None, BitmapRotation.Clockwise180Degrees),
                4 => (BitmapFlip.Vertical, BitmapRotation.None),
                5 => (BitmapFlip.Horizontal, BitmapRotation.Clockwise270Degrees),
                6 => (BitmapFlip.None, BitmapRotation.Clockwise90Degrees),
                7 => (BitmapFlip.Horizontal, BitmapRotation.Clockwise90Degrees),
                8 => (BitmapFlip.None, BitmapRotation.Clockwise270Degrees),
                _ => (BitmapFlip.None, BitmapRotation.None),
            };
        }
        return await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tf, mode, ColorManagementMode.DoNotColorManage);
    }

    public static Gray ToGray(SoftwareBitmap bmp, int longSide)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var buf = new byte[w * h * 4];
        bmp.CopyToBuffer(buf.AsBuffer());
        var p = new float[w * h];
        for (int i = 0, j = 0; i < p.Length; i++, j += 4)
            p[i] = 0.114f * buf[j] + 0.587f * buf[j + 1] + 0.299f * buf[j + 2];
        var g = new Gray(w, h, p);
        double k = (double)longSide / Math.Max(w, h);
        return k >= 1 ? g : g.Resize(Math.Max(1, (int)Math.Round(w * k)), Math.Max(1, (int)Math.Round(h * k)));
    }
}

#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PickMatch.Core;

public static partial class Imaging
{
    static async Task<Bitmap32?> DecodeBytesAsync(byte[] data, int longSide, int? orientation, bool allowUpscale)
    {
        using var ms = new MemoryStream(data).AsRandomAccessStream();
        using var bmp = await DecodeStreamAsync(ms, longSide, orientation, false, allowUpscale);
        return ToBitmap32(bmp);
    }

    static async Task<Bitmap32?> DecodeFileAsync(string path, int longSide, int? orientation, bool allowPreview, bool allowUpscale)
    {
        using var bmp = await DecodeSoftwareBitmapAsync(path, longSide, orientation, allowPreview, allowUpscale);
        return ToBitmap32(bmp);
    }

    internal static async Task<SoftwareBitmap?> DecodeSoftwareBitmapAsync(string path, int longSide, int? orientation, bool allowPreview, bool allowUpscale)
    {
        using var fs = File.OpenRead(path).AsRandomAccessStream();
        return await DecodeStreamAsync(fs, longSide, orientation, allowPreview, allowUpscale);
    }

    static Bitmap32? ToBitmap32(SoftwareBitmap? bmp)
    {
        if (bmp == null) return null;
        var buf = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyToBuffer(buf.AsBuffer());
        return new Bitmap32(bmp.PixelWidth, bmp.PixelHeight, buf);
    }

    static async Task<SoftwareBitmap?> DecodeStreamAsync(IRandomAccessStream stream, int longSide, int? orientation, bool allowPreview, bool allowUpscale)
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
        var (tw, th) = Target((int)dec.PixelWidth, (int)dec.PixelHeight, longSide, allowUpscale);
        var tf = new BitmapTransform
        {
            ScaledWidth = (uint)tw,
            ScaledHeight = (uint)th,
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
}
#endif

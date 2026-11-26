#if !WINDOWS
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PickMatch.Core;

public static partial class Imaging
{
    static Task<Bitmap32?> DecodeBytesAsync(byte[] data, int longSide, int? orientation, bool allowUpscale) =>
        Task.Run(() =>
        {
            using var ms = new MemoryStream(data, writable: false);
            return Decode(ms, longSide, orientation, allowUpscale);
        });

    static Task<Bitmap32?> DecodeFileAsync(string path, int longSide, int? orientation, bool allowPreview, bool allowUpscale) =>
        Task.Run(() =>
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Decode(fs, longSide, orientation, allowUpscale);
        });

    static Bitmap32? Decode(Stream stream, int longSide, int? orientation, bool allowUpscale)
    {
        using var codec = SKCodec.Create(stream);
        if (codec == null) return null;
        int w = codec.Info.Width, h = codec.Info.Height;
        int o = orientation ?? (int)codec.EncodedOrigin;
        if (o is < 1 or > 8) o = 1;

        // Let the JPEG decoder skip detail we don't need (it can decode at 1/2, 1/4, 1/8 size directly).
        var (tw, th) = Target(w, h, longSide, allowUpscale);
        var scaled = codec.GetScaledDimensions((float)Math.Min(1.0, (double)tw / w));
        if (scaled.Width < tw || scaled.Height < th) scaled = new SKSizeI(w, h);

        using var src = new SKBitmap(new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var result = codec.GetPixels(src.Info, src.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;

        // Final size, then orientation (mirror/rotate), in one draw.
        bool swap = o >= 5;
        int dw = swap ? th : tw, dh = swap ? tw : th;
        var info = new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var dst = new SKBitmap(info);
        using (var canvas = new SKCanvas(dst))
        using (var image = SKImage.FromBitmap(src))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(OrientationMatrix(o, tw, th));
            using var paint = new SKPaint();
            canvas.DrawImage(image, new SKRect(0, 0, tw, th), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        var bytes = new byte[dw * dh * 4];
        if (dst.RowBytes == dw * 4) Marshal.Copy(dst.GetPixels(), bytes, 0, bytes.Length);
        else
            for (int y = 0; y < dh; y++)
                Marshal.Copy(dst.GetPixels() + y * dst.RowBytes, bytes, y * dw * 4, dw * 4);
        return new Bitmap32(dw, dh, bytes);
    }

    /// <summary>Save as PNG, enlarged <paramref name="zoom"/> times (for text recognition of small labels).</summary>
    public static void SavePng(Bitmap32 bmp, string path, int zoom = 1)
    {
        var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var src = new SKBitmap(info);
        Marshal.Copy(bmp.Bgra, 0, src.GetPixels(), bmp.Bgra.Length);
        using var dst = new SKBitmap(new SKImageInfo(bmp.Width * zoom, bmp.Height * zoom, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(dst))
        using (var image = SKImage.FromBitmap(src))
        using (var paint = new SKPaint())
            canvas.DrawImage(image, new SKRect(0, 0, dst.Width, dst.Height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        using var data = dst.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>Maps a w×h image to its upright position for EXIF orientation <paramref name="o"/>.</summary>
    static SKMatrix OrientationMatrix(int o, int w, int h) => o switch
    {
        2 => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),   // mirror horizontally
        3 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),  // rotate 180
        4 => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // mirror vertically
        5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),    // transpose
        6 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),   // rotate 90 clockwise
        7 => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),  // transverse
        8 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),   // rotate 90 counter-clockwise
        _ => SKMatrix.Identity,
    };
}
#endif

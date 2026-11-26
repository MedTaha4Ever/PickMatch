using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TahaCull.Core;

/// <summary>Reads images: CR3 embedded previews (no RAW decoding needed) and normal JPG/PNG files.</summary>
public static class ImageIO
{
    public static readonly string[] ClientExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic"];

    /// <summary>
    /// Canon CR3 files carry a 1620x1080 JPEG in a "PRVW" box near the start of the file,
    /// and the orientation in the "CMT1" TIFF block. Reading only those bytes keeps this fast.
    /// </summary>
    public static byte[]? ReadCr3Preview(string path, out int orientation)
    {
        orientation = 1;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        int n = (int)Math.Min(fs.Length, 512 * 1024);
        var head = new byte[n];
        fs.ReadExactly(head);
        orientation = Cr3Orientation(head);

        int i = head.AsSpan().IndexOf("PRVW"u8);
        if (i < 0 || i + 20 > n) return FallbackSiblingJpeg(path, ref orientation);
        int size = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(i + 16));
        int start = i + 20;
        if (size <= 0 || start + (long)size > fs.Length) return FallbackSiblingJpeg(path, ref orientation);

        var jpeg = new byte[size];
        int have = Math.Max(0, Math.Min(size, n - start));
        head.AsSpan(start, have).CopyTo(jpeg);
        if (have < size)
        {
            fs.Position = start + have;
            fs.ReadExactly(jpeg.AsSpan(have));
        }
        if (jpeg[0] != 0xFF || jpeg[1] != 0xD8) return FallbackSiblingJpeg(path, ref orientation);
        return jpeg;
    }

    static byte[]? FallbackSiblingJpeg(string path, ref int orientation)
    {
        // Some folders keep a JPG next to the RAW (same name). Use it if the CR3 preview is missing.
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var d in new[] { dir, Path.GetDirectoryName(dir) })
        {
            if (d == null) continue;
            foreach (var ext in new[] { ".JPG", ".jpg", ".jpeg" })
            {
                var p = Path.Combine(d, name + ext);
                if (File.Exists(p))
                {
                    var bytes = File.ReadAllBytes(p);
                    orientation = JpegOrientation(bytes);
                    return bytes;
                }
            }
        }
        return null;
    }

    static int Cr3Orientation(byte[] b)
    {
        try
        {
            int k = b.AsSpan().IndexOf("CMT1"u8);
            if (k < 0) return 1;
            int t = k + 4; // TIFF header
            bool le = b[t] == 'I';
            uint U32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(t + o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(t + o));
            ushort U16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(t + o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(t + o));
            int ifd = (int)U32(4);
            int count = U16(ifd);
            for (int e = 0; e < count; e++)
            {
                int o = ifd + 2 + e * 12;
                if (U16(o) == 0x0112) return Math.Clamp((int)U16(o + 8), 1, 8);
            }
        }
        catch { /* malformed: assume upright */ }
        return 1;
    }

    public static int JpegOrientation(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (frame.Metadata is BitmapMetadata md && md.GetQuery("/app1/ifd/{ushort=274}") is ushort v) return v;
        }
        catch { }
        return 1;
    }

    /// <summary>Decode to a frozen bitmap with the long side close to <paramref name="longSide"/> and rotation applied.</summary>
    public static BitmapSource Decode(byte[] data, int longSide, int orientation)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.StreamSource = new MemoryStream(data);
        if (longSide > 0) bi.DecodePixelWidth = longSide; // width before rotation; previews are landscape
        bi.EndInit();
        bi.Freeze();

        // DecodePixelWidth on a tall image makes it too big; shrink again if needed.
        BitmapSource r = bi;
        if (longSide > 0 && Math.Max(r.PixelWidth, r.PixelHeight) > longSide * 1.05)
        {
            double k = (double)longSide / Math.Max(r.PixelWidth, r.PixelHeight);
            r = new TransformedBitmap(r, new ScaleTransform(k, k));
            r.Freeze();
        }

        int angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (angle != 0)
        {
            r = new TransformedBitmap(r, new RotateTransform(angle));
            r.Freeze();
        }
        return r;
    }

    public static (byte[] data, int orientation)? ReadAny(string path)
    {
        if (Path.GetExtension(path).Equals(".cr3", StringComparison.OrdinalIgnoreCase))
        {
            var d = ReadCr3Preview(path, out int o);
            return d == null ? null : (d, o);
        }
        var bytes = File.ReadAllBytes(path);
        return (bytes, JpegOrientation(bytes));
    }
}

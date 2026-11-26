using System.Buffers.Binary;

namespace PickMatch.Core;

/// <summary>An embedded JPEG inside a file (RAW preview), or the whole file when it is a JPEG.</summary>
public readonly record struct JpegBlob(long Offset, long Length, int Width, int Height)
{
    public int LongSide => Math.Max(Width, Height);
}

public sealed class PhotoInfo
{
    /// <summary>Orientation from the RAW container (1–8). Null: let the decoder read it from the image itself.</summary>
    public int? Orientation { get; set; }
    /// <summary>"yyyy:MM:dd HH:mm:ss[.sub]" when known. Used to tell same-named shots from different cameras apart.</summary>
    public string? DateTaken { get; set; }
    public List<JpegBlob> Jpegs { get; } = [];
    public bool WholeFileIsImage { get; set; }

    /// <summary>Smallest preview that is still big enough to match and show; falls back to the largest.</summary>
    public JpegBlob? MatchPreview => Jpegs.Where(j => j.LongSide >= 1000).OrderBy(j => j.Length).Cast<JpegBlob?>().FirstOrDefault()
                                     ?? Jpegs.OrderByDescending(j => j.Length).Cast<JpegBlob?>().FirstOrDefault();
    public JpegBlob? BestPreview => Jpegs.OrderByDescending(j => j.Length).Cast<JpegBlob?>().FirstOrDefault();
}

/// <summary>
/// Reads the date, orientation and embedded previews of photos without decoding RAW data.
/// Unknown formats return an empty <see cref="PhotoInfo"/>; the decoder then falls back to Windows' own codecs
/// (for RAW files: the Raw Image Extension from the Microsoft Store).
/// </summary>
public static class PhotoReader
{
    public static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".ari", ".arw", ".bay", ".cap", ".cr2", ".cr3", ".crw", ".dcr", ".dcs", ".dng", ".drf", ".eip",
        ".erf", ".fff", ".gpr", ".iiq", ".k25", ".kdc", ".mdc", ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf",
        ".ori", ".pef", ".ptx", ".pxn", ".raf", ".raw", ".rw2", ".rwl", ".rwz", ".sr2", ".srf", ".srw", ".x3f"
    };

    public static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jpe", ".jfif" };

    public static readonly HashSet<string> OtherImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tif", ".tiff", ".heic", ".heif", ".hif", ".avif", ".webp", ".jxl", ".bmp"
    };

    public static bool IsRaw(string path) => RawExtensions.Contains(Path.GetExtension(path));
    public static bool IsJpeg(string path) => JpegExtensions.Contains(Path.GetExtension(path));
    public static bool IsPhoto(string path)
    {
        var e = Path.GetExtension(path);
        return RawExtensions.Contains(e) || JpegExtensions.Contains(e) || OtherImageExtensions.Contains(e);
    }

    public static PhotoInfo Read(string path)
    {
        var info = new PhotoInfo();
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 14, FileOptions.RandomAccess);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (JpegExtensions.Contains(ext))
            {
                info.WholeFileIsImage = true;
                var (w, h) = JpegSize(s, 0);
                info.Jpegs.Add(new JpegBlob(0, s.Length, w, h));
                ReadJpegExif(s, 0, info, takeOrientation: false);
            }
            else if (OtherImageExtensions.Contains(ext))
            {
                info.WholeFileIsImage = true;
                if (ext is ".tif" or ".tiff") ReadTiff(s, 0, info, collectPreviews: false);
            }
            else if (ext == ".cr3") ReadCr3(s, info);
            else if (ext == ".raf") ReadRaf(s, info);
            else if (TiffReader.Open(s, 0) != null) ReadTiff(s, 0, info, collectPreviews: true);
            else ScanForJpegs(s, info);

            // Date/orientation may also live in the embedded JPEG (e.g. Fujifilm).
            if (info.DateTaken == null && !info.WholeFileIsImage && info.BestPreview is { } best)
                ReadJpegExif(s, best.Offset, info, takeOrientation: false);
        }
        catch { /* unreadable: decoder fallback will try */ }
        return info;
    }

    // ---------- TIFF-based RAW ----------

    static void ReadTiff(Stream s, long baseOff, PhotoInfo info, bool collectPreviews)
    {
        var t = TiffReader.Open(s, baseOff);
        if (t == null) return;
        var seen = new HashSet<uint>();
        Walk(t.FirstIfd, 0, isIfd0: true);

        void Walk(uint off, int depth, bool isIfd0)
        {
            while (off != 0 && depth < 6 && seen.Add(off))
            {
                var (entries, next) = t.ReadIfd(off);
                if (entries.Count == 0) return;
                uint jOff = 0, jLen = 0, compression = 0;
                uint[] strips = [], stripLens = [];
                foreach (var e in entries)
                {
                    switch (e.Tag)
                    {
                        case 0x0112 when isIfd0: var o = (int)t.UInt(e); if (o is >= 1 and <= 8) info.Orientation = o; break;
                        case 0x0103: compression = t.UInt(e); break;
                        case 0x0111: strips = t.UInts(e); break;
                        case 0x0117: stripLens = t.UInts(e); break;
                        case 0x0201: jOff = t.UInt(e); break;
                        case 0x0202: jLen = t.UInt(e); break;
                        case 0x002E when collectPreviews: AddJpeg(s, t.Abs(t.Pointer(e)), e.Count, info); break; // Panasonic JpgFromRaw
                        case 0x014A: foreach (var sub in t.UInts(e)) Walk(sub, depth + 1, false); break;
                        case 0x8769: ReadExifIfd(t, t.UInt(e), info); break;
                        case 0x0132 when info.DateTaken == null: info.DateTaken = t.Ascii(e); break;
                    }
                }
                if (collectPreviews)
                {
                    if (jOff > 0 && jLen > 0) AddJpeg(s, t.Abs(jOff), jLen, info);
                    if (compression is 6 or 7 && strips.Length == 1 && stripLens.Length == 1) AddJpeg(s, t.Abs(strips[0]), stripLens[0], info);
                }
                off = next;
                depth++;
                isIfd0 = false;
            }
        }
    }

    static void ReadExifIfd(TiffReader t, uint off, PhotoInfo info)
    {
        var (entries, _) = t.ReadIfd(off);
        string? date = null, sub = null;
        foreach (var e in entries)
        {
            if (e.Tag == 0x9003) date = t.Ascii(e);
            else if (e.Tag == 0x9291) sub = t.Ascii(e);
        }
        if (!string.IsNullOrWhiteSpace(date)) info.DateTaken = string.IsNullOrWhiteSpace(sub) ? date : $"{date}.{sub}";
    }

    // ---------- Canon CR3 (ISO media file) ----------

    static void ReadCr3(Stream s, PhotoInfo info)
    {
        var head = TiffReader.ReadAt(s, 0, 2 << 20);
        var span = head.AsSpan();
        int k1 = span.IndexOf("CMT1"u8);
        if (k1 >= 0)
        {
            using var ms = new MemoryStream(head);
            var t = TiffReader.Open(ms, k1 + 4);
            if (t != null)
                foreach (var e in t.ReadIfd(t.FirstIfd).entries)
                    if (e.Tag == 0x0112 && t.UInt(e) is var o and >= 1 and <= 8) info.Orientation = (int)o;
        }
        int k2 = span.IndexOf("CMT2"u8);
        if (k2 >= 0)
        {
            using var ms = new MemoryStream(head);
            var t = TiffReader.Open(ms, k2 + 4);
            if (t != null)
            {
                string? date = null, sub = null;
                foreach (var e in t.ReadIfd(t.FirstIfd).entries)
                {
                    if (e.Tag == 0x9003) date = t.Ascii(e);
                    else if (e.Tag == 0x9291) sub = t.Ascii(e);
                }
                if (!string.IsNullOrWhiteSpace(date)) info.DateTaken = string.IsNullOrWhiteSpace(sub) ? date : $"{date}.{sub}";
            }
        }
        // PRVW: 4 zero bytes, u16 1, u16 width, u16 height, u16 1, u32 jpeg size, jpeg data.
        int p = span.IndexOf("PRVW"u8);
        if (p >= 0 && p + 20 <= head.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(span[(p + 16)..]);
            AddJpeg(s, p + 20, len, info);
        }
        // THMB: small 160x120 thumbnail, used only if nothing else.
        int th = span.IndexOf("THMB"u8);
        if (info.Jpegs.Count == 0 && th >= 0 && th + 20 <= head.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(span[(th + 12)..]);
            AddJpeg(s, th + 20, len, info);
            if (info.Jpegs.Count == 0) AddJpeg(s, th + 16, len, info);
        }
    }

    // ---------- Fujifilm RAF ----------

    static void ReadRaf(Stream s, PhotoInfo info)
    {
        var h = TiffReader.ReadAt(s, 0, 100);
        if (h.Length < 92 || !h.AsSpan().StartsWith("FUJIFILM"u8)) { ScanForJpegs(s, info); return; }
        uint off = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(84));
        uint len = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(88));
        AddJpeg(s, off, len, info);
        ReadJpegExif(s, off, info, takeOrientation: false);
    }

    // ---------- other formats: look for JPEG previews in the first part of the file ----------

    static void ScanForJpegs(Stream s, PhotoInfo info)
    {
        var head = TiffReader.ReadAt(s, 0, 4 << 20);
        ReadOnlySpan<byte> soi = [0xFF, 0xD8, 0xFF];
        int i = 0;
        while (i < head.Length)
        {
            int j = head.AsSpan(i).IndexOf(soi);
            if (j < 0) break;
            i += j;
            var (w, h) = JpegSize(s, i);
            if (w >= 320 || w < 0) { info.Jpegs.Add(new JpegBlob(i, s.Length - i, Math.Max(w, 0), Math.Max(h, 0))); break; }
            i += 3;
        }
    }

    // ---------- JPEG helpers ----------

    static void AddJpeg(Stream s, long off, long len, PhotoInfo info)
    {
        if (off <= 0 || len < 1000 || off + len > s.Length) return;
        var (w, h) = JpegSize(s, off);
        if (w != 0) info.Jpegs.Add(new JpegBlob(off, len, Math.Max(w, 0), Math.Max(h, 0))); // -1: size not found but looks valid
    }

    /// <summary>
    /// Size of a baseline/progressive JPEG at <paramref name="off"/>; (0,0) if not a JPEG that Windows can decode
    /// (e.g. lossless RAW data), (-1,-1) if it looks valid but the size is further than we read.
    /// </summary>
    public static (int w, int h) JpegSize(Stream s, long off)
    {
        var b = TiffReader.ReadAt(s, off, 256 * 1024);
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return (0, 0);
        int i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            byte m = b[i + 1];
            if (m == 0xFF) { i++; continue; }
            if (m is 0xD8 or 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            int segLen = (b[i + 2] << 8) | b[i + 3];
            if (m is 0xC0 or 0xC1 or 0xC2)
                return (((b[i + 7] << 8) | b[i + 8]), ((b[i + 5] << 8) | b[i + 6]));
            if (m is 0xC3 or (>= 0xC5 and <= 0xCF) && m is not (0xC4 or 0xC8 or 0xCC)) return (0, 0); // lossless etc.
            if (m is 0xDA or 0xD9) return (0, 0);
            i += 2 + segLen;
        }
        return (-1, -1);
    }

    static void ReadJpegExif(Stream s, long off, PhotoInfo info, bool takeOrientation)
    {
        var b = TiffReader.ReadAt(s, off, 128 * 1024);
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return;
        int i = 2;
        while (i + 4 < b.Length && b[i] == 0xFF)
        {
            byte m = b[i + 1];
            int segLen = (b[i + 2] << 8) | b[i + 3];
            if (m == 0xE1 && i + 10 < b.Length && b.AsSpan(i + 4).StartsWith("Exif\0\0"u8))
            {
                var tmp = new PhotoInfo();
                ReadTiff(s, off + i + 10, tmp, collectPreviews: false);
                info.DateTaken ??= tmp.DateTaken;
                if (takeOrientation) info.Orientation ??= tmp.Orientation;
                return;
            }
            if (m is 0xDA or 0xD9) return;
            i += 2 + segLen;
        }
    }
}

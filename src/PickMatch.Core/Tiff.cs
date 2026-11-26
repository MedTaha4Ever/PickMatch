using System.Buffers.Binary;
using System.Text;

namespace PickMatch.Core;

/// <summary>
/// Minimal TIFF/EXIF reader. Most RAW formats (CR2, NEF, ARW, DNG, RW2, PEF, SRW, ORF…) are TIFF files,
/// and JPEG/CR3 store their EXIF as an embedded TIFF block, so one reader covers them all.
/// </summary>
public sealed class TiffReader
{
    public readonly record struct Entry(ushort Tag, ushort Type, uint Count, byte[] Field, long FieldPos);

    readonly Stream _s;
    readonly long _base;
    readonly bool _le;
    public uint FirstIfd { get; }

    TiffReader(Stream s, long baseOffset, bool le, uint first) { _s = s; _base = baseOffset; _le = le; FirstIfd = first; }

    public static TiffReader? Open(Stream s, long baseOffset)
    {
        try
        {
            var h = ReadAt(s, baseOffset, 8);
            if (h.Length < 8) return null;
            bool le;
            if (h[0] == 'I' && h[1] == 'I') le = true;
            else if (h[0] == 'M' && h[1] == 'M') le = false;
            else return null;
            uint first = le ? BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(4));
            return new TiffReader(s, baseOffset, le, first);
        }
        catch { return null; }
    }

    public static byte[] ReadAt(Stream s, long pos, int len)
    {
        if (pos < 0 || pos >= s.Length) return [];
        len = (int)Math.Min(len, s.Length - pos);
        var b = new byte[len];
        s.Position = pos;
        s.ReadExactly(b);
        return b;
    }

    ushort U16(ReadOnlySpan<byte> b) => _le ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);
    uint U32(ReadOnlySpan<byte> b) => _le ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);

    /// <summary>Absolute position in the stream of an offset stored in the TIFF.</summary>
    public long Abs(uint offset) => _base + offset;

    public (List<Entry> entries, uint next) ReadIfd(uint offset)
    {
        var list = new List<Entry>();
        var cb = ReadAt(_s, _base + offset, 2);
        if (cb.Length < 2) return (list, 0);
        int count = U16(cb);
        if (count is 0 or > 1000) return (list, 0);
        var b = ReadAt(_s, _base + offset + 2, count * 12 + 4);
        for (int i = 0; i < count && (i + 1) * 12 <= b.Length; i++)
        {
            var e = b.AsSpan(i * 12);
            list.Add(new Entry(U16(e), U16(e[2..]), U32(e[4..]), e.Slice(8, 4).ToArray(), _base + offset + 2 + i * 12 + 8));
        }
        uint next = b.Length >= count * 12 + 4 ? U32(b.AsSpan(count * 12)) : 0;
        return (list, next);
    }

    static int TypeSize(ushort type) => type switch { 3 or 8 => 2, 4 or 9 or 11 or 13 => 4, 5 or 10 or 12 => 8, _ => 1 };

    /// <summary>First integer value of a SHORT/LONG entry.</summary>
    public uint UInt(Entry e) => TypeSize(e.Type) == 2 ? U16(e.Field) : TypeSize(e.Type) == 1 ? e.Field[0] : U32(e.Field);

    public uint[] UInts(Entry e)
    {
        int size = TypeSize(e.Type);
        if (e.Count == 0 || e.Count > 4096) return [];
        long total = size * e.Count;
        var data = total <= 4 ? e.Field : ReadAt(_s, _base + U32(e.Field), (int)total);
        var r = new uint[Math.Min(e.Count, (uint)(data.Length / Math.Max(1, size)))];
        for (int i = 0; i < r.Length; i++)
            r[i] = size == 2 ? U16(data.AsSpan(i * 2)) : size == 1 ? data[i] : U32(data.AsSpan(i * 4));
        return r;
    }

    public string Ascii(Entry e)
    {
        if (e.Count == 0 || e.Count > 256) return "";
        var data = e.Count <= 4 ? e.Field : ReadAt(_s, _base + U32(e.Field), (int)e.Count);
        return Encoding.ASCII.GetString(data).TrimEnd('\0', ' ');
    }

    /// <summary>Offset field of an entry whose data lives elsewhere (e.g. RW2 JpgFromRaw).</summary>
    public uint Pointer(Entry e) => U32(e.Field);
}

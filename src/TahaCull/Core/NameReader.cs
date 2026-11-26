using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace TahaCull.Core;

/// <summary>
/// Finds a source file name inside a client image: either in the client file's own name
/// (e.g. "9A2A3314 (1).jpg") or written on the picture (gallery/screenshot labels), using Windows' built-in OCR.
/// </summary>
public sealed partial class NameReader
{
    readonly Dictionary<string, string> _byKey = new(); // confusable-normalized name -> real name
    readonly OcrEngine? _ocr;

    public bool OcrAvailable => _ocr != null;

    public NameReader(IEnumerable<string> sourceNames)
    {
        foreach (var n in sourceNames) _byKey.TryAdd(Key(n), n);
        _ocr = OcrEngine.TryCreateFromUserProfileLanguages()
               ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
    }

    /// <summary>Letters OCR often confuses with digits are folded together, so "9A2A33I4" still finds 9A2A3314.</summary>
    static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToUpperInvariant())
        {
            sb.Append(ch switch
            {
                'O' or 'Q' or 'D' => '0',
                'I' or 'L' or '|' or '!' => '1',
                'S' => '5',
                'B' => '8',
                'Z' => '2',
                'G' => '6',
                _ => ch
            });
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[A-Za-z0-9_|!\-]{5,}")]
    private static partial Regex TokenRx();

    /// <summary>All source names found in the text, in reading order.</summary>
    public List<string> FindIn(string text)
    {
        var hits = new List<string>();
        foreach (Match m in TokenRx().Matches(text.Replace(" ", "")))
        {
            var k = Key(m.Value.Replace("-", "_"));
            // Exact token, or a token that contains a name (e.g. "9A2A3314JPG").
            if (_byKey.TryGetValue(k, out var hit)) { if (!hits.Contains(hit)) hits.Add(hit); continue; }
            foreach (var (key, name) in _byKey)
                if (key.Length >= 5 && k.Contains(key, StringComparison.Ordinal) && !hits.Contains(name)) hits.Add(name);
        }
        return hits;
    }

    public List<string> FromFileName(string path) => FindIn(Path.GetFileNameWithoutExtension(path));

    public async Task<List<string>> FromPictureAsync(string path)
    {
        if (_ocr == null) return [];
        try
        {
            using var stream = File.OpenRead(path).AsRandomAccessStream();
            var dec = await WinBitmapDecoder.CreateAsync(stream);
            uint max = OcrEngine.MaxImageDimension;

            // Pass 1: as is. Pass 2: enlarged, which helps with small labels on screenshots.
            foreach (double zoom in new[] { 1.0, 2.0 })
            {
                double k = Math.Min(zoom, (double)max / Math.Max(dec.PixelWidth, dec.PixelHeight));
                if (zoom > 1 && k <= 1.05) break;
                var tf = new BitmapTransform
                {
                    ScaledWidth = (uint)(dec.PixelWidth * k),
                    ScaledHeight = (uint)(dec.PixelHeight * k),
                    InterpolationMode = BitmapInterpolationMode.Fant
                };
                using var bmp = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tf,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
                var res = await _ocr.RecognizeAsync(bmp);
                var hits = FindIn(res.Text);
                if (hits.Count > 0) return hits;
            }
        }
        catch { /* unreadable format for OCR: fall back to image matching */ }
        return [];
    }
}

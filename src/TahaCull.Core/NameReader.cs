using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TahaCull.Core;

/// <summary>
/// Finds a photo's file name in a client picture: in the client file's own name (e.g. "IMG_1234 (1).jpg"),
/// or written on the picture (gallery and screenshot labels) using the OCR built into Windows.
/// </summary>
public sealed partial class NameReader
{
    readonly Dictionary<string, string> _byKey = new(); // confusable-folded name -> real name
    readonly OcrEngine? _ocr;

    public bool OcrAvailable => _ocr != null;

    public NameReader(IEnumerable<string> names)
    {
        foreach (var n in names) _byKey.TryAdd(Key(n), n);
        _ocr = OcrEngine.TryCreateFromUserProfileLanguages()
               ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
    }

    /// <summary>Characters OCR often confuses are folded together, so "9A2A33I4" still finds 9A2A3314.</summary>
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
                '-' => '_',
                _ => ch
            });
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[A-Za-z0-9_|!\-]{4,}")]
    private static partial Regex TokenRx();

    /// <summary>All known names found in the text, in reading order.</summary>
    public List<string> FindIn(string text)
    {
        var hits = new List<string>();
        foreach (Match m in TokenRx().Matches(text.Replace(" ", "")))
        {
            var k = Key(m.Value);
            if (_byKey.TryGetValue(k, out var hit)) { if (!hits.Contains(hit)) hits.Add(hit); continue; }
            foreach (var (key, name) in _byKey)
                if (key.Length >= 5 && k.Contains(key, StringComparison.Ordinal) && !hits.Contains(name)) hits.Add(name);
        }
        return hits;
    }

    public List<string> FromFileName(string path) => FindIn(Path.GetFileNameWithoutExtension(path));

    /// <summary>OCR the picture as is, then enlarged (helps with small labels on screenshots).</summary>
    public async Task<List<string>> FromPictureAsync(string path)
    {
        if (_ocr == null) return [];
        try
        {
            var info = new PhotoInfo { WholeFileIsImage = true };
            uint max = OcrEngine.MaxImageDimension;
            using (var bmp = await Imaging.DecodeAsync(path, info, (int)max))
            {
                if (bmp == null) return [];
                var hits = FindIn((await _ocr.RecognizeAsync(bmp)).Text);
                if (hits.Count > 0) return hits;
                if (Math.Max(bmp.PixelWidth, bmp.PixelHeight) * 2 > max) return [];
                using var big = await Imaging.DecodeAsync(path, info, Math.Max(bmp.PixelWidth, bmp.PixelHeight) * 2, allowUpscale: true);
                if (big == null) return [];
                return FindIn((await _ocr.RecognizeAsync(big)).Text);
            }
        }
        catch { return []; }
    }
}

using System.Diagnostics;

namespace PickMatch.Core;

public interface ITextRecognizer
{
    /// <summary>Text on the picture, read at <paramref name="zoom"/>× its size. Null when that zoom isn't possible.</summary>
    Task<string?> ReadAsync(string path, int zoom);
}

public static class TextRecognizer
{
    /// <summary>The text recognition available on this computer, or null.</summary>
    public static ITextRecognizer? Create()
    {
#if WINDOWS
        return WindowsTextRecognizer.TryCreate();
#else
        return TesseractTextRecognizer.TryCreate();
#endif
    }
}

#if WINDOWS
sealed class WindowsTextRecognizer : ITextRecognizer
{
    readonly Windows.Media.Ocr.OcrEngine _engine;
    WindowsTextRecognizer(Windows.Media.Ocr.OcrEngine engine) => _engine = engine;

    public static ITextRecognizer? TryCreate()
    {
        var e = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        return e == null ? null : new WindowsTextRecognizer(e);
    }

    public async Task<string?> ReadAsync(string path, int zoom)
    {
        int max = (int)Windows.Media.Ocr.OcrEngine.MaxImageDimension;
        using var bmp = await Imaging.DecodeSoftwareBitmapAsync(path, max, null, false, false);
        if (bmp == null) return null;
        if (zoom <= 1) return (await _engine.RecognizeAsync(bmp)).Text;

        int big = Math.Max(bmp.PixelWidth, bmp.PixelHeight) * zoom;
        if (big > max) return null;
        using var large = await Imaging.DecodeSoftwareBitmapAsync(path, big, null, false, true);
        return large == null ? null : (await _engine.RecognizeAsync(large)).Text;
    }
}
#else
/// <summary>
/// Uses the Tesseract command-line tool when installed (Linux: "sudo apt install tesseract-ocr",
/// macOS: "brew install tesseract"). Without it, matching relies on file names and image similarity.
/// </summary>
sealed class TesseractTextRecognizer : ITextRecognizer
{
    readonly string _exe;
    TesseractTextRecognizer(string exe) => _exe = exe;

    public static ITextRecognizer? TryCreate()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", @"C:\Program Files\Tesseract-OCR"]);
        foreach (var d in dirs)
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            foreach (var name in new[] { "tesseract", "tesseract.exe" })
            {
                var p = Path.Combine(d, name);
                if (File.Exists(p)) return new TesseractTextRecognizer(p);
            }
        }
        return null;
    }

    public async Task<string?> ReadAsync(string path, int zoom)
    {
        string input = path, temp = "";
        try
        {
            if (zoom > 1)
            {
                var bmp = await Imaging.DecodeAsync(path, new PhotoInfo { WholeFileIsImage = true }, 1 << 20);
                if (bmp == null) return null;
                temp = Path.Combine(Path.GetTempPath(), $"pickmatch-ocr-{Guid.NewGuid():N}.png");
                Imaging.SavePng(bmp, temp, zoom);
                input = temp;
            }
            var psi = new ProcessStartInfo(_exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add("stdout");
            psi.ArgumentList.Add("--psm");
            psi.ArgumentList.Add("11"); // sparse text: labels anywhere on the picture
            using var p = Process.Start(psi);
            if (p == null) return null;
            var text = await p.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await p.WaitForExitAsync(cts.Token);
            return text;
        }
        catch { return null; }
        finally
        {
            if (temp.Length > 0) try { File.Delete(temp); } catch { }
        }
    }
}
#endif

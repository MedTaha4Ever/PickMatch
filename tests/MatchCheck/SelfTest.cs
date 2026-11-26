using PickMatch.Core;

/// <summary>
/// Platform check without real photos: draws made-up "originals", makes cropped, smaller "client" copies
/// with generic names, and checks every copy is matched to its original. Runs on Windows, Linux and macOS.
/// </summary>
static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pickmatch-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        var photos = Path.Combine(root, "photos");
        var client = Path.Combine(root, "client");
        Directory.CreateDirectory(photos);
        Directory.CreateDirectory(client);
        var rnd = new Random(7);
        var expected = new Dictionary<string, string>();
        for (int i = 0; i < 40; i++)
        {
            bool portrait = i % 5 == 0;
            var img = Draw(rnd, portrait ? 400 : 600, portrait ? 600 : 400);
            string name = $"IMG_{1000 + i}";
            WriteBmp(Path.Combine(photos, name + ".bmp"), img);
            if (i % 4 == 0)
            {
                // Client copy: 10% cropped on each side, half size, generic name.
                var crop = Crop(img, 0.1);
                var small = Half(crop);
                string cn = $"picked_{expected.Count + 1:00}.bmp";
                WriteBmp(Path.Combine(client, cn), small);
                expected[cn] = name;
            }
        }

        var lib = await Library.LoadAsync(photos, [client], null, default);
        Console.WriteLine($"Self-test: {lib.Photos.Count} originals, {expected.Count} client pictures, OCR {(TextRecognizer.Create() != null ? "available" : "not available")}");
        var engine = new MatchEngine(lib);
        int wrong = 0;
        foreach (var (cn, exp) in expected)
        {
            var r = await engine.MatchAsync(Path.Combine(client, cn));
            bool ok = r.Chosen?.Name == exp;
            if (!ok) wrong++;
            Console.WriteLine($"  {cn} -> {r.Chosen?.Name} ({r.Kind}, {(r.Candidates.Count > 0 ? r.Candidates[0].score : 0):0.00}) {(ok ? "ok" : "WRONG, expected " + exp)}");
        }

        // Copy and rename.
        var outDir = Path.Combine(client, "Originals");
        var picks = expected.Select(e => (Path.Combine(client, e.Key), lib.ByName[e.Value][0])).ToList();
        var rep = FileCopier.Copy(picks, outDir, new CopyOptions(true, true, true, true));
        bool copyOk = rep.Copied == expected.Count && rep.Renamed == expected.Count && rep.Problems.Count == 0
                      && expected.Values.All(n => File.Exists(Path.Combine(outDir, n + ".bmp")) && File.Exists(Path.Combine(client, n + ".bmp")));
        Console.WriteLine($"  copy: {rep.Copied} copied, {rep.Renamed} renamed {(copyOk ? "ok" : "WRONG")}");

        try { Directory.Delete(root, true); } catch { }
        bool pass = wrong == 0 && copyOk;
        Console.WriteLine(pass ? "Self-test passed" : "Self-test FAILED");
        return pass ? 0 : 1;
    }

    sealed record Img(int W, int H, byte[] Rgb);

    static Img Draw(Random rnd, int w, int h)
    {
        var px = new byte[w * h * 3];
        var bg = new[] { rnd.Next(256), rnd.Next(256), rnd.Next(256) };
        var bg2 = new[] { rnd.Next(256), rnd.Next(256), rnd.Next(256) };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 3; c++)
                    px[(y * w + x) * 3 + c] = (byte)(bg[c] + (bg2[c] - bg[c]) * (x + y) / (w + h));
        for (int s = 0; s < 14; s++)
        {
            int cx = rnd.Next(w), cy = rnd.Next(h), r = rnd.Next(15, 90);
            var col = new[] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256) };
            bool circle = rnd.Next(2) == 0;
            for (int y = Math.Max(0, cy - r); y < Math.Min(h, cy + r); y++)
                for (int x = Math.Max(0, cx - r); x < Math.Min(w, cx + r); x++)
                    if (!circle || (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r)
                        for (int c = 0; c < 3; c++) px[(y * w + x) * 3 + c] = col[c];
        }
        return new Img(w, h, px);
    }

    static Img Crop(Img a, double f)
    {
        int x0 = (int)(a.W * f), y0 = (int)(a.H * f), w = a.W - 2 * x0, h = a.H - 2 * y0;
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++) Array.Copy(a.Rgb, ((y + y0) * a.W + x0) * 3, px, y * w * 3, w * 3);
        return new Img(w, h, px);
    }

    static Img Half(Img a)
    {
        int w = a.W / 2, h = a.H / 2;
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 3; c++)
                {
                    int s = a.Rgb[((2 * y) * a.W + 2 * x) * 3 + c] + a.Rgb[((2 * y) * a.W + 2 * x + 1) * 3 + c]
                          + a.Rgb[((2 * y + 1) * a.W + 2 * x) * 3 + c] + a.Rgb[((2 * y + 1) * a.W + 2 * x + 1) * 3 + c];
                    px[(y * w + x) * 3 + c] = (byte)(s / 4);
                }
        return new Img(w, h, px);
    }

    /// <summary>24-bit BMP: readable everywhere without an encoder library.</summary>
    static void WriteBmp(string path, Img img)
    {
        int stride = (img.W * 3 + 3) & ~3;
        int size = 54 + stride * img.H;
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write((byte)'B'); bw.Write((byte)'M'); bw.Write(size); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(img.W); bw.Write(img.H); bw.Write((short)1); bw.Write((short)24);
        bw.Write(0); bw.Write(stride * img.H); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        var row = new byte[stride];
        for (int y = img.H - 1; y >= 0; y--)
        {
            for (int x = 0; x < img.W; x++)
            {
                int i = (y * img.W + x) * 3;
                row[x * 3] = img.Rgb[i + 2]; row[x * 3 + 1] = img.Rgb[i + 1]; row[x * 3 + 2] = img.Rgb[i];
            }
            bw.Write(row);
        }
    }
}

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TahaCull.Core;

/// <summary>Small grayscale image used for matching.</summary>
public sealed class Gray
{
    public readonly int W, H;
    public readonly float[] P;
    double[]? _ii, _ii2;

    public Gray(int w, int h, float[] p) { W = w; H = h; P = p; }

    public static Gray From(BitmapSource src, int longSide)
    {
        var g = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
        int w = g.PixelWidth, h = g.PixelHeight;
        var buf = new byte[w * h];
        g.CopyPixels(buf, w, 0);
        var f = new float[buf.Length];
        for (int i = 0; i < buf.Length; i++) f[i] = buf[i];
        var full = new Gray(w, h, f);
        double k = (double)longSide / Math.Max(w, h);
        return k >= 1 ? full : full.Resize(Math.Max(1, (int)Math.Round(w * k)), Math.Max(1, (int)Math.Round(h * k)));
    }

    /// <summary>Area-average downscale (nearest when upscaling).</summary>
    public Gray Resize(int tw, int th)
    {
        var sum = new float[tw * th];
        var cnt = new int[tw * th];
        if (tw <= W && th <= H)
        {
            for (int y = 0; y < H; y++)
            {
                int ty = y * th / H;
                for (int x = 0; x < W; x++)
                {
                    int idx = ty * tw + x * tw / W;
                    sum[idx] += P[y * W + x];
                    cnt[idx]++;
                }
            }
            for (int i = 0; i < sum.Length; i++) sum[i] = cnt[i] > 0 ? sum[i] / cnt[i] : 0;
        }
        else
        {
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    sum[y * tw + x] = P[(y * H / th) * W + x * W / tw];
        }
        return new Gray(tw, th, sum);
    }

    void EnsureIntegrals()
    {
        if (_ii != null) return;
        int s = W + 1;
        var ii = new double[s * (H + 1)];
        var ii2 = new double[s * (H + 1)];
        for (int y = 0; y < H; y++)
        {
            double row = 0, row2 = 0;
            for (int x = 0; x < W; x++)
            {
                double v = P[y * W + x];
                row += v; row2 += v * v;
                ii[(y + 1) * s + x + 1] = ii[y * s + x + 1] + row;
                ii2[(y + 1) * s + x + 1] = ii2[y * s + x + 1] + row2;
            }
        }
        _ii2 = ii2;
        _ii = ii;
    }

    /// <summary>Best normalized cross-correlation of template <paramref name="t"/> anywhere inside this image.</summary>
    public double BestNcc(Gray t)
    {
        int tw = t.W, th = t.H;
        if (tw > W || th > H || tw < 6 || th < 6) return -1;
        EnsureIntegrals();
        int n = tw * th;
        double mean = 0;
        foreach (var v in t.P) mean += v;
        mean /= n;
        var tz = new float[n];
        double tn = 0;
        for (int i = 0; i < n; i++) { tz[i] = (float)(t.P[i] - mean); tn += tz[i] * (double)tz[i]; }
        if (tn < 1e-3) return -1;
        tn = Math.Sqrt(tn);

        int s = W + 1;
        var ii = _ii!; var ii2 = _ii2!;
        double best = -1;
        for (int y = 0; y + th <= H; y++)
        {
            for (int x = 0; x + tw <= W; x++)
            {
                int a = y * s + x, b = y * s + x + tw, c = (y + th) * s + x, d = (y + th) * s + x + tw;
                double sum = ii[d] - ii[b] - ii[c] + ii[a];
                double sq = ii2[d] - ii2[b] - ii2[c] + ii2[a];
                double var = sq - sum * sum / n;
                if (var < 1e-3) continue;
                double dot = 0;
                for (int j = 0; j < th; j++)
                {
                    var rowS = P.AsSpan((y + j) * W + x, tw);
                    var rowT = tz.AsSpan(j * tw, tw);
                    for (int i = 0; i < tw; i++) dot += rowT[i] * rowS[i];
                }
                double v = dot / (tn * Math.Sqrt(var));
                if (v > best) best = v;
            }
        }
        return best;
    }
}

/// <summary>Source photo (CR3) prepared for matching.</summary>
public sealed class SourcePhoto
{
    public required string Name { get; init; }   // file name without extension, e.g. 9A2A3314
    public required string Path { get; init; }
    public required int Orientation { get; init; }
    public required Gray Coarse { get; init; }
    public required Gray Fine { get; init; }
}

/// <summary>
/// Finds which source photo a client image comes from. The client image may be resized, compressed,
/// slightly cropped or a screenshot, so the client picture is slid over each source at several scales
/// (normalized cross-correlation). A quick coarse pass keeps the best few, a finer pass ranks them.
/// </summary>
public static class Matcher
{
    public const int CoarseSize = 48, FineSize = 128, ClientSize = 256;
    static readonly double[] CoarseScales = [1.0, 0.85, 0.7];
    static readonly double[] FineScales = [1.0, 0.95, 0.9, 0.85, 0.8, 0.75, 0.7, 0.65, 0.6, 0.55];

    public static List<(SourcePhoto src, double score)> Rank(Gray client, IReadOnlyList<SourcePhoto> sources, int keep = 5)
    {
        var templates = new Dictionary<(int, int), Gray>();
        Gray Template(int w, int h)
        {
            lock (templates)
            {
                if (!templates.TryGetValue((w, h), out var g)) templates[(w, h)] = g = client.Resize(w, h);
                return g;
            }
        }

        double Score(Gray s, double[] scales)
        {
            double km = Math.Min((double)s.W / client.W, (double)s.H / client.H);
            double best = -1;
            foreach (var f in scales)
            {
                int w = (int)Math.Round(client.W * km * f), h = (int)Math.Round(client.H * km * f);
                if (w < 8 || h < 8) continue;
                best = Math.Max(best, s.BestNcc(Template(w, h)));
            }
            return best;
        }

        var coarse = new (SourcePhoto, double)[sources.Count];
        Parallel.For(0, sources.Count, i => coarse[i] = (sources[i], Score(sources[i].Coarse, CoarseScales)));
        var shortlist = coarse.OrderByDescending(c => c.Item2).Take(Math.Max(keep * 2, 8)).ToArray();

        var fine = new (SourcePhoto, double)[shortlist.Length];
        Parallel.For(0, shortlist.Length, i => fine[i] = (shortlist[i].Item1, Score(shortlist[i].Item1.Fine, FineScales)));
        return fine.OrderByDescending(f => f.Item2).Take(keep).ToList();
    }

    public static double ScoreOne(Gray client, SourcePhoto s)
    {
        return Rank(client, [s], 1)[0].score;
    }
}

// Checks matching from the command line: MatchCheck <cr3Folder> <clientFolder> [expected.tsv]
// expected.tsv lines: clientFileName<TAB>cr3Name
using System.Diagnostics;
using System.IO;
using TahaCull.Core;

var sw = Stopwatch.StartNew();
var cr3 = Directory.EnumerateFiles(args[0], "*.cr3", SearchOption.AllDirectories).ToList();
var sources = new System.Collections.Concurrent.ConcurrentBag<SourcePhoto>();
Parallel.ForEach(cr3, f =>
{
    var j = ImageIO.ReadCr3Preview(f, out int o);
    if (j == null) return;
    var fine = Gray.From(ImageIO.Decode(j, 256, o), Matcher.FineSize);
    sources.Add(new SourcePhoto { Name = Path.GetFileNameWithoutExtension(f), Path = f, Orientation = o, Fine = fine,
        Coarse = fine.Resize(fine.W * Matcher.CoarseSize / Math.Max(fine.W, fine.H), fine.H * Matcher.CoarseSize / Math.Max(fine.W, fine.H)) });
});
Console.WriteLine($"{sources.Count} CR3 read in {sw.Elapsed.TotalSeconds:0.0}s");
var all = sources.ToList();
var names = new NameReader(all.Select(s => s.Name));
var expected = args.Length > 2 ? File.ReadAllLines(args[2]).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1]) : new();
int okOcr = 0, okImg = 0, n = 0;
foreach (var c in Directory.EnumerateFiles(args[1]).Where(f => f.EndsWith(".jpg") || f.EndsWith(".jpeg")).OrderBy(f => f))
{
    var t = Stopwatch.StartNew();
    var bytes = File.ReadAllBytes(c);
    var g = Gray.From(ImageIO.Decode(bytes, 900, ImageIO.JpegOrientation(bytes)), Matcher.ClientSize);
    var ocr = await names.FromPictureAsync(c);
    var rank = Matcher.Rank(g, all, 5);
    var fn = Path.GetFileName(c);
    expected.TryGetValue(fn, out var exp);
    n++;
    if (ocr.Count > 0 && ocr[0] == exp) okOcr++;
    if (rank[0].src.Name == exp) okImg++;
    Console.WriteLine($"{fn,-14} exp={exp} ocr=[{string.Join(",", ocr)}] img={rank[0].src.Name}:{rank[0].score:0.00} 2nd={rank[1].src.Name}:{rank[1].score:0.00} {t.ElapsedMilliseconds}ms");
}
Console.WriteLine($"OCR correct {okOcr}/{n}, image top-1 correct {okImg}/{n}, total {sw.Elapsed.TotalSeconds:0.0}s");

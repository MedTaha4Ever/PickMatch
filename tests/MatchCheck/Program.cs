// Command-line check of the matching, without the app:
//   MatchCheck <photosFolder> <clientFolder> [expected.tsv] [excludeFolder]...   expected.tsv: clientFileName<TAB>photoName
//   MatchCheck --info <file>...                                    show what is read from photo files
using System.Diagnostics;
using TahaCull.Core;

if (args.Length > 0 && args[0] == "--info")
{
    foreach (var f in args.Skip(1))
    {
        var sw0 = Stopwatch.StartNew();
        var i = PhotoReader.Read(f);
        using var bmp = await Imaging.DecodeAsync(f, i, 320);
        Console.WriteLine($"{Path.GetFileName(f)}: date={i.DateTaken} orient={i.Orientation} jpegs=[{string.Join(", ", i.Jpegs.Select(j => $"{j.Width}x{j.Height}@{j.Offset}"))}] decoded={bmp?.PixelWidth}x{bmp?.PixelHeight} {sw0.ElapsedMilliseconds}ms");
    }
    return;
}

var sw = Stopwatch.StartNew();
var lib = await Library.LoadAsync(args[0], args.Skip(3).Prepend(args[1]), null, default);
Console.WriteLine($"{lib.FileCount} files -> {lib.Photos.Count} shots ({lib.Photos.Count(p => p.Fine == null)} without preview) in {sw.Elapsed.TotalSeconds:0.0}s");
foreach (var g in lib.Photos.GroupBy(p => p.Kinds).OrderByDescending(g => g.Count())) Console.WriteLine($"  {g.Count(),5} x {g.Key}");

var engine = new MatchEngine(lib);
var expected = args.Length > 2 ? File.ReadAllLines(args[2]).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1]) : new();
int ok = 0, okAuto = 0, wrongAuto = 0, n = 0;
foreach (var c in MatchEngine.ClientPictures(args[1]))
{
    var t = Stopwatch.StartNew();
    var r = await engine.MatchAsync(c);
    var fn = Path.GetFileName(c);
    expected.TryGetValue(fn, out var exp);
    n++;
    bool right = r.Chosen?.Name == exp;
    if (right) ok++;
    if (r.AutoConfirm) { if (right) okAuto++; else wrongAuto++; }
    var alt = r.Candidates.Skip(1).FirstOrDefault();
    Console.WriteLine($"{fn,-14} exp={exp} got={r.Chosen?.Name}({r.Kind},{(r.Candidates.Count > 0 ? r.Candidates[0].score : 0):0.00}) auto={r.AutoConfirm} next={alt.src?.Name}:{alt.score:0.00} {(right ? "" : "  <-- WRONG")} {t.ElapsedMilliseconds}ms");
}
Console.WriteLine($"Correct {ok}/{n}; auto-confirmed right {okAuto}, auto-confirmed WRONG {wrongAuto}; total {sw.Elapsed.TotalSeconds:0.0}s");

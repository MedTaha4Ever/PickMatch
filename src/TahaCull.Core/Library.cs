using System.Collections.Concurrent;

namespace TahaCull.Core;

public sealed class PhotoFile
{
    public required string Path { get; init; }
    public required long Length { get; init; }
    public required PhotoInfo Info { get; init; }
    public bool IsRaw => PhotoReader.IsRaw(Path);
    public bool IsJpeg => PhotoReader.IsJpeg(Path);
    public string Extension => System.IO.Path.GetExtension(Path);
}

/// <summary>
/// One shot. RAW + JPG copies of the same shot (same name and same capture time, possibly in different
/// subfolders like "cr3" and "Compressed") are grouped so they show up once.
/// </summary>
public sealed class SourcePhoto
{
    public required string Name { get; init; }
    public required List<PhotoFile> Files { get; init; }
    public Gray? Coarse { get; set; }
    public Gray? Fine { get; set; }

    /// <summary>File used to show and match the shot: a RAW with an embedded preview, else the lightest JPG, else anything.</summary>
    public PhotoFile PreviewFile =>
        Files.Where(f => f.IsRaw && f.Info.Jpegs.Count > 0).OrderBy(f => f.Length).FirstOrDefault()
        ?? Files.Where(f => f.IsJpeg).OrderBy(f => f.Length).FirstOrDefault()
        ?? Files.OrderBy(f => f.IsRaw ? 1 : 0).First();

    public IEnumerable<PhotoFile> RawFiles => Files.Where(f => f.IsRaw);

    /// <summary>The full-quality JPG (largest) when several exist.</summary>
    public PhotoFile? MainJpeg => Files.Where(f => !f.IsRaw).OrderByDescending(f => f.Length).FirstOrDefault();

    public string Kinds => string.Join(" + ", Files.Select(f => f.Extension.TrimStart('.').ToUpperInvariant()).Distinct());
    public string Folder => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(PreviewFile.Path)) ?? "";
}

public sealed class Library
{
    public List<SourcePhoto> Photos { get; } = [];
    public Dictionary<string, List<SourcePhoto>> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int FileCount { get; private set; }

    /// <summary>Scan <paramref name="root"/> (with subfolders) for RAW and image files, skipping <paramref name="exclude"/> folders.</summary>
    public static async Task<Library> LoadAsync(string root, IEnumerable<string> exclude, IProgress<(int done, int total, string stage)>? progress, CancellationToken ct)
    {
        var excl = exclude.Where(e => !string.IsNullOrWhiteSpace(e))
                          .Select(e => Path.TrimEndingDirectorySeparator(Path.GetFullPath(e)) + Path.DirectorySeparatorChar)
                          .ToList();
        var paths = await Task.Run(() =>
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            return Directory.EnumerateFiles(root, "*", opts)
                .Where(PhotoReader.IsPhoto)
                .Where(p => !excl.Any(e => Path.GetFullPath(p).StartsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }, ct);

        var lib = new Library { FileCount = paths.Count };

        // 1. Metadata (fast: a few KB per file).
        var files = new ConcurrentBag<PhotoFile>();
        int done = 0;
        await Parallel.ForEachAsync(paths, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 8 }, (p, _) =>
        {
            try { files.Add(new PhotoFile { Path = p, Length = new FileInfo(p).Length, Info = PhotoReader.Read(p) }); } catch { }
            int d = Interlocked.Increment(ref done);
            if (d % 25 == 0) progress?.Report((d, paths.Count, "Reading photo details"));
            return ValueTask.CompletedTask;
        });

        // 2. Group copies of the same shot.
        foreach (var g in files.GroupBy(f => Path.GetFileNameWithoutExtension(f.Path), StringComparer.OrdinalIgnoreCase))
        {
            // Same name: same shot if the capture times agree (or are unknown and the files sit in related folders).
            var groups = new List<List<PhotoFile>>();
            foreach (var f in g.OrderBy(f => f.IsRaw ? 0 : 1))
            {
                var home = groups.FirstOrDefault(grp => SameShot(grp[0], f));
                if (home != null) home.Add(f); else groups.Add([f]);
            }
            foreach (var grp in groups)
            {
                var sp = new SourcePhoto { Name = g.Key, Files = grp };
                lib.Photos.Add(sp);
                if (!lib.ByName.TryGetValue(g.Key, out var list)) lib.ByName[g.Key] = list = [];
                list.Add(sp);
            }
        }

        // 3. Small grayscale versions for matching.
        done = 0;
        await Parallel.ForEachAsync(lib.Photos, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, async (sp, _) =>
        {
            var pf = sp.PreviewFile;
            using var bmp = await Imaging.DecodeAsync(pf.Path, pf.Info, 320);
            if (bmp != null)
            {
                sp.Fine = Imaging.ToGray(bmp, Matcher.FineSize);
                sp.Coarse = sp.Fine.ResizeLong(Matcher.CoarseSize);
            }
            int d = Interlocked.Increment(ref done);
            if (d % 10 == 0 || d == lib.Photos.Count) progress?.Report((d, lib.Photos.Count, "Reading previews"));
        });
        return lib;
    }

    static bool SameShot(PhotoFile a, PhotoFile b)
    {
        string? da = a.Info.DateTaken, db = b.Info.DateTaken;
        if (da != null && db != null)
        {
            // Sub-seconds are not always written in every copy: compare to the second when one lacks them.
            if (da == db) return true;
            return da.Length >= 19 && db.Length >= 19 && da[..19] == db[..19] && (da.Length == 19 || db.Length == 19);
        }
        // Unknown date: same folder, or one folder inside the other's parent (e.g. "Mariage" and "Mariage\cr3").
        string fa = Path.GetDirectoryName(a.Path)!, fb = Path.GetDirectoryName(b.Path)!;
        string pa = Path.GetDirectoryName(fa) ?? fa, pb = Path.GetDirectoryName(fb) ?? fb;
        return fa.Equals(fb, StringComparison.OrdinalIgnoreCase) || pa.Equals(pb, StringComparison.OrdinalIgnoreCase)
            || pa.Equals(fb, StringComparison.OrdinalIgnoreCase) || pb.Equals(fa, StringComparison.OrdinalIgnoreCase);
    }
}

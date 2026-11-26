namespace TahaCull.Core;

public enum MatchKind { None, FileName, Ocr, Image, Manual }

public sealed class MatchResult
{
    public required string ClientPath { get; init; }
    public Gray? ClientGray { get; init; }
    /// <summary>Best candidates first; the chosen one is first when there is one.</summary>
    public List<(SourcePhoto src, double score)> Candidates { get; init; } = [];
    public SourcePhoto? Chosen { get; set; }
    public MatchKind Kind { get; set; }
    public string? Warning { get; set; }
    public bool AutoConfirm { get; set; }
}

/// <summary>Matches one client picture: name in the file name, then name written on it (OCR), then image similarity.</summary>
public sealed class MatchEngine
{
    readonly Library _lib;
    public NameReader Names { get; }

    public MatchEngine(Library lib)
    {
        _lib = lib;
        Names = new NameReader(lib.ByName.Keys);
    }

    public static IEnumerable<string> ClientPictures(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(f => PhotoReader.IsPhoto(f) && !PhotoReader.IsRaw(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    public async Task<MatchResult> MatchAsync(string clientPath, CancellationToken ct = default)
    {
        var info = new PhotoInfo { WholeFileIsImage = true };
        using var bmp = await Imaging.DecodeAsync(clientPath, info, 640);
        if (bmp == null) return new MatchResult { ClientPath = clientPath, Warning = "Windows can't open this picture" };
        var gray = Imaging.ToGray(bmp, Matcher.ClientSize);

        var kind = MatchKind.FileName;
        var named = Names.FromFileName(clientPath);
        if (named.Count == 0) { named = await Names.FromPictureAsync(clientPath); kind = MatchKind.Ocr; }

        // Image similarity always runs: it gives the alternatives and double-checks a name.
        var ranked = await Task.Run(() => Matcher.Rank(gray, _lib.Photos, 6, ct), ct);
        var result = new MatchResult { ClientPath = clientPath, ClientGray = gray, Candidates = ranked };

        (SourcePhoto src, double score)? best = null;
        foreach (var n in named)
        {
            if (!_lib.ByName.TryGetValue(n, out var sameName)) continue;
            foreach (var src in sameName) // several cameras can use the same file name
            {
                var hit = ranked.FirstOrDefault(r => r.src == src);
                double score = hit.src != null ? hit.score : Matcher.ScoreOne(gray, src);
                if (best == null || score > best.Value.score) best = (src, score);
            }
        }

        if (best is { } b)
        {
            result.Kind = kind;
            result.Chosen = b.src;
            if (b.score < 0.35) result.Warning = "The picture doesn't look like this photo";
            result.AutoConfirm = result.Warning == null;
            ranked.RemoveAll(r => r.src == b.src);
            ranked.Insert(0, b);
        }
        else if (ranked.Count > 0)
        {
            result.Kind = MatchKind.Image;
            result.Chosen = ranked[0].src;
            double margin = ranked.Count > 1 ? ranked[0].score - ranked[1].score : 1;
            result.AutoConfirm = ranked[0].score >= 0.9 && margin >= 0.08;
            if (ranked[0].score < 0.5) result.Warning = "Not sure: pick the right photo";
        }
        return result;
    }
}

public sealed record CopyOptions(bool Raw, bool Jpeg, bool Sidecars, bool RenameClientPictures);

public sealed record CopyReport(int Copied, int AlreadyThere, int Renamed, List<string> Problems);

public static class FileCopier
{
    static readonly string[] SidecarExtensions = [".xmp", ".pp3", ".dop", ".on1", ".cos"];

    /// <summary>Copy the chosen photos' files into <paramref name="outDir"/>. Nothing is ever overwritten.</summary>
    public static CopyReport Copy(IEnumerable<(string clientPath, SourcePhoto photo)> picks, string outDir, CopyOptions o,
                                  Action<string, string>? onClientRenamed = null)
    {
        Directory.CreateDirectory(outDir);
        int copied = 0, there = 0, renamed = 0;
        var problems = new List<string>();
        var done = new HashSet<SourcePhoto>();

        foreach (var (clientPath, photo) in picks)
        {
            try
            {
                if (done.Add(photo))
                {
                    var files = new List<string>();
                    if (o.Raw) files.AddRange(photo.RawFiles.Select(f => f.Path));
                    if (o.Jpeg && photo.MainJpeg is { } j) files.Add(j.Path);
                    if (files.Count == 0) // nothing of the chosen kind: take what exists
                        files.Add((photo.RawFiles.FirstOrDefault() ?? photo.MainJpeg ?? photo.Files[0]).Path);
                    if (o.Sidecars)
                        foreach (var f in files.ToList())
                            foreach (var ext in SidecarExtensions)
                                foreach (var side in new[] { Path.ChangeExtension(f, ext), f + ext })
                                    if (File.Exists(side) && !files.Contains(side)) files.Add(side);

                    foreach (var f in files)
                    {
                        var dest = Path.Combine(outDir, Path.GetFileName(f));
                        if (File.Exists(dest))
                        {
                            if (new FileInfo(dest).Length == new FileInfo(f).Length) { there++; continue; }
                            dest = Unique(dest);
                        }
                        File.Copy(f, dest);
                        copied++;
                    }
                }

                if (o.RenameClientPictures)
                {
                    var target = Path.Combine(Path.GetDirectoryName(clientPath)!, photo.Name + Path.GetExtension(clientPath).ToLowerInvariant());
                    if (!target.Equals(clientPath, StringComparison.OrdinalIgnoreCase))
                    {
                        target = Unique(target);
                        File.Move(clientPath, target);
                        onClientRenamed?.Invoke(clientPath, target);
                        renamed++;
                    }
                }
            }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(clientPath)}: {ex.Message}"); }
        }
        return new CopyReport(copied, there, renamed, problems);
    }

    static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}

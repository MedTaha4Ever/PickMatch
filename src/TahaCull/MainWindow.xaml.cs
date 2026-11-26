using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using TahaCull.Core;

namespace TahaCull;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty ThumbHeightProperty =
        DependencyProperty.Register(nameof(ThumbHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(280.0));
    public double ThumbHeight { get => (double)GetValue(ThumbHeightProperty); set => SetValue(ThumbHeightProperty, value); }

    const int ThumbSize = 900;     // pixels kept for the big previews
    const int SmallThumbSize = 240;

    readonly ObservableCollection<PairItem> _items = [];
    readonly ICollectionView _view;
    Dictionary<string, SourcePhoto> _sources = new(StringComparer.OrdinalIgnoreCase);

    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TahaCull", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        PairList.ItemsSource = _items;
        _view = CollectionViewSource.GetDefaultView(_items);
        LoadSettings();
        ClientBox.TextChanged += (_, _) => { if (string.IsNullOrWhiteSpace(OutBox.Text) || OutBox.Tag as string == "auto") SetAutoOut(); };
    }

    void SetAutoOut()
    {
        if (string.IsNullOrWhiteSpace(ClientBox.Text)) return;
        OutBox.Text = Path.Combine(ClientBox.Text.Trim(), "CR3");
        OutBox.Tag = "auto";
    }

    // ---------- folders & settings ----------

    static string? PickFolder(string title, string start)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (Directory.Exists(start)) dlg.InitialDirectory = start;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    void BrowseCr3_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Folder with the CR3 files", Cr3Box.Text) is { } f) Cr3Box.Text = f;
    }

    void BrowseClient_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Folder with the pictures the client sent", ClientBox.Text) is { } f) { ClientBox.Text = f; SetAutoOut(); }
    }

    void BrowseOut_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Where to copy the chosen CR3 files", OutBox.Text) is { } f) { OutBox.Text = f; OutBox.Tag = null; }
    }

    record Settings(string? Cr3, string? Client, double Size);

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            if (s == null) return;
            Cr3Box.Text = s.Cr3 ?? "";
            if (s.Size > 0) SizeSlider.Value = s.Size;
        }
        catch { }
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(Cr3Box.Text, ClientBox.Text, SizeSlider.Value)));
        }
        catch { }
    }

    protected override void OnClosing(CancelEventArgs e) { SaveSettings(); base.OnClosing(e); }

    void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ThumbHeight = e.NewValue;

    void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _view.Filter = OnlyCheckBox.IsChecked == true ? o => o is PairItem p && (p.NeedsCheck || !p.Confirmed) : null;
    }

    void SetBusy(bool busy, string text)
    {
        MatchButton.IsEnabled = !busy;
        CopyButton.IsEnabled = !busy && _items.Count > 0;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = text;
    }

    void Report(double done, double total, string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Progress.Maximum = Math.Max(1, total);
            Progress.Value = done;
            StatusText.Text = text;
        });
    }

    // ---------- matching ----------

    async void Match_Click(object sender, RoutedEventArgs e)
    {
        string cr3Dir = Cr3Box.Text.Trim(), clientDir = ClientBox.Text.Trim();
        string outDir = OutBox.Text.Trim();
        if (!Directory.Exists(cr3Dir) || !Directory.Exists(clientDir))
        {
            MessageBox.Show(this, "Choose both folders first.", "TahaCull");
            return;
        }
        SaveSettings();
        _items.Clear();
        SetBusy(true, "Looking for files…");
        var sw = Stopwatch.StartNew();

        try
        {
            var cr3Files = await Task.Run(() => Directory
                .EnumerateFiles(cr3Dir, "*.cr3", SearchOption.AllDirectories)
                .Where(f => string.IsNullOrEmpty(outDir) || !Path.GetFullPath(f).StartsWith(Path.GetFullPath(outDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList());
            var clientFiles = Directory.EnumerateFiles(clientDir)
                .Where(f => ImageIO.ClientExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cr3Files.Count == 0) { SetBusy(false, "No CR3 files found in that folder."); return; }
            if (clientFiles.Count == 0) { SetBusy(false, "No pictures found in the client folder."); return; }

            // 1. Read the small previews embedded in every CR3.
            var sources = await Task.Run(() => LoadSources(cr3Files));
            _sources = sources.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var all = _sources.Values.ToList();
            var names = new NameReader(_sources.Keys);

            // 2. Match each client picture.
            for (int i = 0; i < clientFiles.Count; i++)
            {
                string path = clientFiles[i];
                Report(i, clientFiles.Count, $"Matching {i + 1} / {clientFiles.Count}: {Path.GetFileName(path)}");
                var item = await MatchOne(path, all, names);
                _items.Add(item);
            }

            FlagDuplicates();
            int check = _items.Count(p => p.NeedsCheck);
            SetBusy(false, $"{_items.Count} pictures matched against {all.Count} CR3 in {sw.Elapsed.TotalSeconds:0}s. " +
                           (check == 0 ? "All found by name." : $"{check} need a look (orange/red).") +
                           (names.OcrAvailable ? "" : " (Windows OCR not available: matched by image only.)"));
        }
        catch (Exception ex)
        {
            SetBusy(false, "Error: " + ex.Message);
        }
    }

    List<SourcePhoto> LoadSources(List<string> files)
    {
        var bag = new System.Collections.Concurrent.ConcurrentBag<SourcePhoto>();
        int done = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, f =>
        {
            try
            {
                var jpeg = ImageIO.ReadCr3Preview(f, out int orientation);
                if (jpeg != null)
                {
                    var bmp = ImageIO.Decode(jpeg, 256, orientation);
                    var fine = Gray.From(bmp, Matcher.FineSize);
                    bag.Add(new SourcePhoto
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Path = f,
                        Orientation = orientation,
                        Fine = fine,
                        Coarse = fine.Resize(
                            Math.Max(1, fine.W * Matcher.CoarseSize / Math.Max(fine.W, fine.H)),
                            Math.Max(1, fine.H * Matcher.CoarseSize / Math.Max(fine.W, fine.H))),
                    });
                }
            }
            catch { /* skip unreadable file */ }
            int d = Interlocked.Increment(ref done);
            if (d % 10 == 0 || d == files.Count) Report(d, files.Count, $"Reading CR3 previews {d} / {files.Count}");
        });
        return bag.ToList();
    }

    async Task<PairItem> MatchOne(string path, List<SourcePhoto> all, NameReader names)
    {
        var item = new PairItem { ClientPath = path };
        var (clientBmp, clientGray) = await Task.Run(() =>
        {
            var bytes = File.ReadAllBytes(path);
            var o = ImageIO.JpegOrientation(bytes);
            var bmp = ImageIO.Decode(bytes, ThumbSize, o);
            return (bmp, Gray.From(bmp, Matcher.ClientSize));
        });
        item.ClientThumb = clientBmp;
        item.ClientGray = clientGray;

        // Name first: in the file name, then written on the picture.
        var kind = MatchKind.FileName;
        var named = names.FromFileName(path);
        if (named.Count == 0) { named = await names.FromPictureAsync(path); kind = MatchKind.Ocr; }

        // Image similarity always runs: it gives the alternatives and double-checks the name.
        var ranked = await Task.Run(() => Matcher.Rank(clientGray, all, 5));
        var cands = ranked.Select(r => new Candidate { Owner = item, Source = r.src, Score = r.score }).ToList();

        Candidate? chosen = null;
        foreach (var n in named)
        {
            if (!_sources.TryGetValue(n, out var src)) continue;
            var c = cands.FirstOrDefault(x => x.Source == src);
            if (c == null)
            {
                double s = await Task.Run(() => Matcher.ScoreOne(clientGray, src));
                c = new Candidate { Owner = item, Source = src, Score = s };
                cands.Insert(0, c);
            }
            // With several names on one picture (gallery screenshot), keep the one that looks most alike.
            if (chosen == null || c.Score > chosen.Score) chosen = c;
        }

        if (chosen != null)
        {
            item.Kind = kind;
            if (chosen.Score < 0.35) item.Note = "the picture does not look like this CR3";
            item.Confirmed = string.IsNullOrEmpty(item.Note);
            cands.Remove(chosen);
            cands.Insert(0, chosen);
        }
        else if (cands.Count > 0)
        {
            chosen = cands[0];
            item.Kind = MatchKind.Image;
            double margin = cands.Count > 1 ? chosen.Score - cands[1].Score : 1;
            item.Confirmed = chosen.Score >= 0.9 && margin >= 0.08;
        }

        foreach (var c in cands) item.Candidates.Add(c);
        item.Selected = chosen;

        // Previews for the original and the alternatives.
        await Task.Run(() =>
        {
            foreach (var c in cands)
            {
                var data = ImageIO.ReadCr3Preview(c.Source.Path, out int o);
                if (data == null) continue;
                c.Thumb = ImageIO.Decode(data, c == chosen ? ThumbSize : SmallThumbSize, o);
            }
        });
        return item;
    }

    void FlagDuplicates()
    {
        var counts = _items.Where(p => p.Selected != null).GroupBy(p => p.Selected!.Name).ToDictionary(g => g.Key, g => g.Count());
        foreach (var p in _items) p.Duplicate = p.Selected != null && counts[p.Selected.Name] > 1;
    }

    // ---------- manual choice ----------

    async void Candidate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Candidate c) return;
        await Choose(c.Owner, c);
    }

    async Task Choose(PairItem item, Candidate c)
    {
        if (c.Thumb == null || Math.Max(c.Thumb.PixelWidth, c.Thumb.PixelHeight) < ThumbSize * 0.8)
        {
            var big = await Task.Run(() =>
            {
                var data = ImageIO.ReadCr3Preview(c.Source.Path, out int o);
                return data == null ? null : ImageIO.Decode(data, ThumbSize, o);
            });
            if (big != null) c.Thumb = big;
        }
        item.Selected = c;
        if (item.Kind != MatchKind.Manual && item.Candidates.IndexOf(c) != 0) item.Kind = MatchKind.Manual;
        item.Note = "";
        item.Confirmed = true;
        FlagDuplicates();
    }

    void ManualName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ManualName_Click(sender, e);
    }

    async void ManualName_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PairItem item) return;
        var typed = item.ManualName.Trim();
        var key = Path.GetFileNameWithoutExtension(typed);
        if (!_sources.TryGetValue(key, out var src))
        {
            // Allow typing just the number, e.g. "3314".
            var hits = _sources.Keys.Where(k => k.EndsWith(key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count != 1)
            {
                MessageBox.Show(this, hits.Count == 0 ? $"No CR3 named \"{typed}\"." : $"\"{typed}\" fits {hits.Count} files, type more of the name.", "TahaCull");
                return;
            }
            src = _sources[hits[0]];
        }
        var c = item.Candidates.FirstOrDefault(x => x.Source == src);
        if (c == null)
        {
            double s = item.ClientGray == null ? -1 : await Task.Run(() => Matcher.ScoreOne(item.ClientGray, src));
            c = new Candidate { Owner = item, Source = src, Score = s };
            item.Candidates.Insert(0, c);
        }
        await Choose(item, c);
        item.Kind = MatchKind.Manual;
        item.ManualName = "";
    }

    void ConfirmAll_Click(object sender, RoutedEventArgs e) { foreach (var p in _items) p.Confirmed = p.Selected != null; }
    void UnconfirmAll_Click(object sender, RoutedEventArgs e) { foreach (var p in _items) p.Confirmed = false; }

    // ---------- output ----------

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _items.Where(p => p.Confirmed && p.Selected != null).ToList();
        if (chosen.Count == 0) { MessageBox.Show(this, "Nothing is confirmed yet.", "TahaCull"); return; }
        string outDir = OutBox.Text.Trim();
        if (string.IsNullOrEmpty(outDir)) { MessageBox.Show(this, "Choose where to save.", "TahaCull"); return; }

        int unconfirmed = _items.Count - chosen.Count;
        int dups = chosen.Count(p => p.Duplicate);
        var msg = $"Copy {chosen.Select(p => p.Selected!.Name).Distinct().Count()} CR3 files to\n{outDir}?";
        if (unconfirmed > 0) msg += $"\n\n{unconfirmed} picture(s) are not confirmed and will be skipped.";
        if (dups > 0) msg += $"\n{dups} picture(s) point to the same CR3 as another one.";
        if (MessageBox.Show(this, msg, "TahaCull", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        Directory.CreateDirectory(outDir);
        int copied = 0, existed = 0, renamed = 0;
        var errors = new List<string>();
        foreach (var p in chosen)
        {
            var src = p.Selected!.Source;
            try
            {
                var dest = Path.Combine(outDir, Path.GetFileName(src.Path));
                if (File.Exists(dest)) existed++;
                else { File.Copy(src.Path, dest); copied++; }

                if (XmpBox.IsChecked == true)
                {
                    foreach (var xmp in new[] { Path.ChangeExtension(src.Path, ".xmp"), src.Path + ".xmp" })
                    {
                        var xdest = Path.Combine(outDir, Path.GetFileName(xmp));
                        if (File.Exists(xmp) && !File.Exists(xdest)) File.Copy(xmp, xdest);
                    }
                }

                if (RenameBox.IsChecked == true && !p.Duplicate)
                {
                    var newPath = Path.Combine(Path.GetDirectoryName(p.ClientPath)!,
                        src.Name + Path.GetExtension(p.ClientPath).ToLowerInvariant());
                    if (!newPath.Equals(p.ClientPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(newPath))
                    {
                        File.Move(p.ClientPath, newPath);
                        p.ClientPath = newPath;
                        renamed++;
                    }
                }
            }
            catch (Exception ex) { errors.Add($"{p.ClientName}: {ex.Message}"); }
        }

        var done = $"Copied {copied} CR3" + (existed > 0 ? $" ({existed} were already there)" : "") +
                   (renamed > 0 ? $", renamed {renamed} client pictures" : "") + ".";
        if (errors.Count > 0) done += "\n\nProblems:\n" + string.Join("\n", errors.Take(10));
        StatusText.Text = done.Split('\n')[0];
        if (MessageBox.Show(this, done + "\n\nOpen the folder?", "TahaCull", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outDir}\"") { UseShellExecute = true });
    }
}

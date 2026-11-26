using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TahaCull.Core;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;

namespace TahaCull;

public sealed partial class MainWindow : Window
{
    const int ThumbSize = 640, SmallThumbSize = 200, LargeSize = 2048;

    readonly List<PairItem> _all = [];
    readonly ObservableCollection<PairItem> _visible = [];
    Library? _lib;
    MatchEngine? _engine;
    List<string> _names = [];
    CancellationTokenSource? _cts;
    string? _photosDir, _clientDir, _outDir;
    bool _busy;
    PairItem? _compareItem;

    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TahaCull", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TahaCull.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(980 * scale);
            p.PreferredMinimumHeight = (int)(640 * scale);
        }
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(1440 * scale), area.Width), h = Math.Min((int)(960 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));

        PairList.ItemsSource = _visible;
        LoadSettings();

        // TahaCull.exe <photos folder> <client folder> starts matching right away.
        var args = Environment.GetCommandLineArgs().Skip(1).Where(Directory.Exists).ToList();
        if (args.Count >= 2)
        {
            SetPhotos(args[0]);
            SetClient(args[1]);
            DispatcherQueue.TryEnqueue(() => Match_Click(this, new RoutedEventArgs()));
        }
        Closed += (_, _) => { _cts?.Cancel(); SaveSettings(); };
    }

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    // ---------- settings ----------

    sealed record Settings(string? PhotosDir, double Size, bool Raw, bool Jpg, bool Sidecars, bool Rename);

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            if (s == null) return;
            if (Directory.Exists(s.PhotosDir)) SetPhotos(s.PhotosDir!);
            if (s.Size >= SizeSlider.Minimum) SizeSlider.Value = s.Size;
            RawOption.IsChecked = s.Raw; JpgOption.IsChecked = s.Jpg; SidecarOption.IsChecked = s.Sidecars; RenameOption.IsChecked = s.Rename;
        }
        catch { }
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(_photosDir, SizeSlider.Value,
                RawOption.IsChecked, JpgOption.IsChecked, SidecarOption.IsChecked, RenameOption.IsChecked)));
        }
        catch { }
    }

    // ---------- folders ----------

    async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    async void PickPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } f) SetPhotos(f);
    }

    async void PickClient_Click(object sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } f) SetClient(f);
    }

    void SetClient(string f)
    {
        _clientDir = f;
        int n = MatchEngine.ClientPictures(f).Count();
        ClientCard.Description = $"{f}  ·  {n} picture{(n == 1 ? "" : "s")}";
        SetOut(Path.Combine(f, "Originals"));
        UpdateButtons();
    }

    async void PickOut_Click(object sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } f) SetOut(f);
    }

    void SetPhotos(string f)
    {
        _photosDir = f;
        PhotosCard.Description = f;
        UpdateButtons();
    }

    void SetOut(string f)
    {
        _outDir = f;
        OutText.Text = f;
        ToolTipService.SetToolTip(OutText, f);
    }

    void UpdateButtons()
    {
        MatchButton.IsEnabled = !_busy && _photosDir != null && _clientDir != null;
        int n = _all.Count(p => p.Confirmed && p.Selected != null);
        CopyText.Text = n == 1 ? "Copy 1 photo" : $"Copy {n} photos";
        CopyButton.IsEnabled = !_busy && n > 0;
    }

    void ShowInfo(InfoBarSeverity severity, string title, string message, ButtonBase? action = null)
    {
        Info.Severity = severity;
        Info.Title = title;
        Info.Message = message;
        Info.ActionButton = action;
        Info.IsOpen = true;
    }

    // ---------- matching ----------

    async void Match_Click(object sender, RoutedEventArgs e)
    {
        if (_photosDir == null || _clientDir == null) return;
        SaveSettings();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        Info.IsOpen = false;
        CloseCompare();
        _all.Clear();
        _visible.Clear();
        var sw = Stopwatch.StartNew();

        var progress = new Progress<(int done, int total, string stage)>(p =>
        {
            Progress.IsIndeterminate = false;
            Progress.Maximum = Math.Max(1, p.total);
            Progress.Value = p.done;
            ProgressText.Text = $"{p.stage} · {p.done} of {p.total}";
        });

        try
        {
            ProgressText.Text = "Looking for photos…";
            Progress.IsIndeterminate = true;
            string photos = _photosDir, client = _clientDir, output = _outDir ?? "";
            _lib = await Task.Run(() => Library.LoadAsync(photos, [client, output], progress, ct), ct);
            if (_lib.Photos.Count == 0)
            {
                ShowInfo(InfoBarSeverity.Warning, "No photos found", "There are no RAW or JPG files in the photos folder.");
                return;
            }
            _engine = new MatchEngine(_lib);
            _names = _lib.ByName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            var clients = MatchEngine.ClientPictures(client).ToList();
            if (clients.Count == 0)
            {
                ShowInfo(InfoBarSeverity.Warning, "No client pictures", "The client folder has no pictures.");
                return;
            }
            ShowList(true);
            for (int i = 0; i < clients.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                ((IProgress<(int, int, string)>)progress).Report((i, clients.Count, "Matching client pictures"));
                var engine = _engine;
                string path = clients[i];
                var r = await Task.Run(() => engine.MatchAsync(path, ct), ct);
                var item = await BuildItemAsync(r);
                _all.Add(item);
                if (PassesFilter(item)) _visible.Add(item);
                FlagDuplicates();
                UpdateSummary();
            }

            int toCheck = _all.Count(p => !p.Confirmed);
            ShowInfo(toCheck == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational,
                $"Matched {_all.Count} picture{(_all.Count == 1 ? "" : "s")} in {sw.Elapsed.TotalSeconds:0} s",
                toCheck == 0 ? "All were found by name and look right. Review them, then copy."
                             : $"{toCheck} need a look. Use the To check filter, or double-click a pair to compare it large."
                             + (_engine.Names.OcrAvailable ? "" : " (Windows text recognition isn't available, so names on pictures can't be read.)"));
        }
        catch (OperationCanceledException)
        {
            ShowInfo(InfoBarSeverity.Warning, "Stopped", $"{_all.Count} pictures were matched before stopping.");
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "Something went wrong", ex.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateSummary();
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    void SetBusy(bool busy)
    {
        _busy = busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy) ProgressText.Text = "";
        UpdateButtons();
    }

    void ShowList(bool show)
    {
        EmptyState.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PairList.Visibility = ListToolbar.Visibility = Footer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task<PairItem> BuildItemAsync(MatchResult r)
    {
        var item = new PairItem(r.ClientPath)
        {
            ClientGray = r.ClientGray,
            Kind = r.Kind,
            Warning = r.Warning,
            Confirmed = r.AutoConfirm,
            ThumbHeight = SizeSlider.Value,
        };
        foreach (var (src, score) in r.Candidates) item.Candidates.Add(new Candidate(item, src, score));
        item.Selected = item.Candidates.FirstOrDefault(c => c.Source == r.Chosen);
        item.PropertyChanged += Item_PropertyChanged;

        string path = r.ClientPath;
        item.ClientThumb = await ToImageAsync(await Task.Run(() => Imaging.DecodeAsync(path, new PhotoInfo { WholeFileIsImage = true }, ThumbSize)));
        foreach (var c in item.Candidates) await LoadThumbAsync(c, c == item.Selected ? ThumbSize : SmallThumbSize);
        return item;
    }

    static async Task LoadThumbAsync(Candidate c, int size)
    {
        if (c.ThumbSize >= size) return;
        var pf = c.Source.PreviewFile;
        var bmp = await Task.Run(() => Imaging.DecodeAsync(pf.Path, pf.Info, size));
        if (bmp == null) return;
        c.ThumbSize = size;
        c.Thumb = await ToImageAsync(bmp);
    }

    static async Task<ImageSource?> ToImageAsync(SoftwareBitmap? bmp)
    {
        if (bmp == null) return null;
        var src = new SoftwareBitmapSource();
        await src.SetBitmapAsync(bmp);
        return src;
    }

    void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PairItem.Confirmed) or nameof(PairItem.Selected)) UpdateSummary();
    }

    void FlagDuplicates()
    {
        var counts = _all.Where(p => p.Selected != null).GroupBy(p => p.Selected!.Source).ToDictionary(g => g.Key, g => g.Count());
        foreach (var p in _all) p.Duplicate = p.Selected != null && counts[p.Selected.Source] > 1;
    }

    void UpdateSummary()
    {
        int confirmed = _all.Count(p => p.Confirmed);
        SummaryText.Text = _all.Count == 0 ? "" : $"{_all.Count} pictures · {confirmed} confirmed · {_all.Count - confirmed} to check";
        UpdateButtons();
    }

    // ---------- filter & size ----------

    bool PassesFilter(PairItem p) => Filter.SelectedIndex switch
    {
        1 => !p.Confirmed || p.NeedsCheck,
        2 => p.Confirmed,
        _ => true
    };

    void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PairList == null) return;
        _visible.Clear();
        foreach (var p in _all.Where(PassesFilter)) _visible.Add(p);
    }

    void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        foreach (var p in _all) p.ThumbHeight = e.NewValue;
    }

    // ---------- choosing ----------

    async void Candidate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Candidate c) await ChooseAsync(c.Owner, c);
    }

    async Task ChooseAsync(PairItem item, Candidate c)
    {
        item.Selected = c;
        if (item.Candidates.IndexOf(c) != 0 || item.Kind == MatchKind.Image) item.Kind = MatchKind.Manual;
        item.Warning = null;
        item.Confirmed = true;
        FlagDuplicates();
        await LoadThumbAsync(c, ThumbSize);
    }

    void Name_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var t = sender.Text.Trim();
        sender.ItemsSource = t.Length == 0 ? null
            : _names.Where(n => n.Contains(t, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
    }

    async void Name_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is not PairItem item || _lib == null) return;
        var typed = (args.ChosenSuggestion as string ?? args.QueryText).Trim();
        if (typed.Length == 0) return;
        var key = Path.GetFileNameWithoutExtension(typed);
        if (!_lib.ByName.ContainsKey(key))
        {
            var hits = _names.Where(n => n.EndsWith(key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count != 1)
            {
                ShowInfo(InfoBarSeverity.Warning, "Name not found",
                    hits.Count == 0 ? $"No photo is called \"{typed}\"." : $"\"{typed}\" fits {hits.Count} photos. Type more of the name.");
                return;
            }
            key = hits[0];
        }

        Candidate? best = null;
        foreach (var src in _lib.ByName[key])
        {
            var c = item.Candidates.FirstOrDefault(x => x.Source == src);
            if (c == null)
            {
                var gray = item.ClientGray;
                double s = gray == null ? -1 : await Task.Run(() => Matcher.ScoreOne(gray, src));
                c = new Candidate(item, src, s);
                item.Candidates.Insert(0, c);
            }
            if (best == null || c.Score > best.Score) best = c;
        }
        if (best == null) return;
        await ChooseAsync(item, best);
        item.Kind = MatchKind.Manual;
        sender.Text = "";
    }

    void ConfirmAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _all) p.Confirmed = p.Selected != null;
    }

    void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _all) p.Confirmed = false;
    }

    void Option_Click(object sender, RoutedEventArgs e) => SaveSettings();

    // ---------- copying ----------

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var picks = _all.Where(p => p.Confirmed && p.Selected != null).ToList();
        if (picks.Count == 0 || _outDir == null) return;
        if (RawOption.IsChecked == false && JpgOption.IsChecked == false) RawOption.IsChecked = true;

        int photos = picks.Select(p => p.Selected!.Source).Distinct().Count();
        int skipped = _all.Count - picks.Count;
        var kinds = new List<string>();
        if (RawOption.IsChecked) kinds.Add("RAW");
        if (JpgOption.IsChecked) kinds.Add("JPG");
        if (SidecarOption.IsChecked) kinds.Add("edits");
        var text = $"The {string.Join(", ", kinds)} files of {photos} photo{(photos == 1 ? "" : "s")} will be copied to:\n{_outDir}";
        if (skipped > 0) text += $"\n\n{skipped} picture{(skipped == 1 ? " isn't" : "s aren't")} confirmed and will be skipped.";
        if (RenameOption.IsChecked) text += "\n\nThe client's pictures will be renamed to the original names.";
        text += "\n\nExisting files are never overwritten.";

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Copy the confirmed photos?",
            Content = text,
            PrimaryButtonText = "Copy",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        SaveSettings();
        SetBusy(true);
        ProgressText.Text = "Copying…";
        Progress.IsIndeterminate = true;
        try
        {
            var options = new CopyOptions(RawOption.IsChecked, JpgOption.IsChecked, SidecarOption.IsChecked, RenameOption.IsChecked);
            var list = picks.Select(p => (p.ClientPath, p.Selected!.Source)).ToList();
            var outDir = _outDir;
            var renames = new List<(string from, string to)>();
            var report = await Task.Run(() => FileCopier.Copy(list, outDir, options, (a, b) => { lock (renames) renames.Add((a, b)); }));
            foreach (var (from, to) in renames)
                foreach (var p in _all.Where(p => p.ClientPath == from)) p.ClientPath = to;

            var msg = $"{report.Copied} file{(report.Copied == 1 ? "" : "s")} copied";
            if (report.AlreadyThere > 0) msg += $", {report.AlreadyThere} already there";
            if (report.Renamed > 0) msg += $", {report.Renamed} client picture{(report.Renamed == 1 ? "" : "s")} renamed";
            msg += ".";
            if (report.Problems.Count > 0) msg += " Problems: " + string.Join("; ", report.Problems.Take(5));

            var open = new Button { Content = "Open folder" };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outDir}\"") { UseShellExecute = true });
            ShowInfo(report.Problems.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning, "Done", msg, open);
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "Copy failed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- large compare ----------

    void Compare_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PairItem p) _ = OpenCompareAsync(p);
    }

    void Thumb_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PairItem p) _ = OpenCompareAsync(p);
    }

    async Task OpenCompareAsync(PairItem p)
    {
        _compareItem = p;
        CompareOverlay.Visibility = Visibility.Visible;
        CompareTitle.Text = p.SelectedName;
        CompareStatus.Text = p.StatusText;
        CompareClientName.Text = "Client · " + p.ClientName;
        CompareOriginalName.Text = p.Selected == null ? "" : $"Original · {p.Selected.Name} · {p.Selected.Source.Kinds}";
        CompareConfirm.IsChecked = p.Confirmed;
        CompareClient.Source = p.ClientThumb;
        CompareOriginal.Source = p.SelectedThumb;
        CompareRing.IsActive = true;
        Root.Focus(FocusState.Programmatic);

        try
        {
            string client = p.ClientPath;
            var big1 = await ToImageAsync(await Task.Run(() => Imaging.DecodeAsync(client, new PhotoInfo { WholeFileIsImage = true }, LargeSize)));
            if (_compareItem != p) return;
            if (big1 != null) CompareClient.Source = big1;
            if (p.Selected?.Source.PreviewFile is { } pf)
            {
                var big2 = await ToImageAsync(await Task.Run(() => Imaging.DecodeAsync(pf.Path, pf.Info, LargeSize, best: true)));
                if (_compareItem != p) return;
                if (big2 != null) CompareOriginal.Source = big2;
            }
        }
        finally
        {
            if (_compareItem == p) CompareRing.IsActive = false;
        }
    }

    void CloseCompare()
    {
        _compareItem = null;
        CompareOverlay.Visibility = Visibility.Collapsed;
        CompareClient.Source = CompareOriginal.Source = null;
    }

    void CompareClose_Click(object sender, RoutedEventArgs e) => CloseCompare();
    void ComparePrev_Click(object sender, RoutedEventArgs e) => StepCompare(-1);
    void CompareNext_Click(object sender, RoutedEventArgs e) => StepCompare(1);

    void StepCompare(int delta)
    {
        if (_compareItem == null) return;
        int i = _visible.IndexOf(_compareItem) + delta;
        if (i < 0 || i >= _visible.Count) return;
        PairList.ScrollIntoView(_visible[i]);
        _ = OpenCompareAsync(_visible[i]);
    }

    void CompareConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_compareItem != null) _compareItem.Confirmed = CompareConfirm.IsChecked == true;
    }

    void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CompareOverlay.Visibility != Visibility.Visible) return;
        switch (e.Key)
        {
            case VirtualKey.Escape: CloseCompare(); e.Handled = true; break;
            case VirtualKey.Left: StepCompare(-1); e.Handled = true; break;
            case VirtualKey.Right: StepCompare(1); e.Handled = true; break;
            case VirtualKey.Space:
                if (_compareItem != null)
                {
                    _compareItem.Confirmed = !_compareItem.Confirmed;
                    CompareConfirm.IsChecked = _compareItem.Confirmed;
                }
                e.Handled = true;
                break;
        }
    }
}

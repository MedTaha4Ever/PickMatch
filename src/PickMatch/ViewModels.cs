using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PickMatch.Core;

namespace PickMatch;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];
}

public sealed class Candidate : Observable
{
    public Candidate(PairItem owner, SourcePhoto source, double score) { Owner = owner; Source = source; Score = score; }

    public PairItem Owner { get; }
    public SourcePhoto Source { get; }
    public double Score { get; set; }
    public string Name => Source.Name;
    public string ScoreText => Score <= -1 ? "" : $"{Score * 100:0}% alike";
    public string Tooltip => $"{Source.Name} · {Source.Kinds} · {Source.Folder}";

    /// <summary>Long side of the loaded thumbnail, to know when a bigger one is needed.</summary>
    public int ThumbSize { get; set; }

    ImageSource? _thumb;
    public ImageSource? Thumb
    {
        get => _thumb;
        set { if (Set(ref _thumb, value)) Owner.OnCandidateThumbChanged(this); }
    }

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) Raise(nameof(Border)); }
    }

    public Brush Border => IsSelected ? ThemeBrush("AccentFillColorDefaultBrush") : ThemeBrush("ControlStrokeColorDefaultBrush");
}

public sealed class PairItem : Observable
{
    public PairItem(string clientPath) { _clientPath = clientPath; }

    string _clientPath;
    public string ClientPath { get => _clientPath; set { if (Set(ref _clientPath, value)) Raise(nameof(ClientName)); } }
    public string ClientName => Path.GetFileName(ClientPath);
    public Gray? ClientGray { get; set; }

    ImageSource? _clientThumb;
    public ImageSource? ClientThumb { get => _clientThumb; set => Set(ref _clientThumb, value); }

    double _thumbHeight = 280;
    public double ThumbHeight { get => _thumbHeight; set => Set(ref _thumbHeight, value); }

    public ObservableCollection<Candidate> Candidates { get; } = [];

    Candidate? _selected;
    public Candidate? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected != null) _selected.IsSelected = false;
            _selected = value;
            if (value != null) value.IsSelected = true;
            Raise();
            Raise(nameof(SelectedThumb)); Raise(nameof(SelectedName)); Raise(nameof(SelectedDetails));
            RaiseStatus();
        }
    }

    public ImageSource? SelectedThumb => Selected?.Thumb;
    public string SelectedName => Selected?.Name ?? "No match";
    public string SelectedDetails => Selected == null ? "" : $"{Selected.Source.Kinds} · {Selected.Source.Folder}";

    internal void OnCandidateThumbChanged(Candidate c)
    {
        if (c == Selected) Raise(nameof(SelectedThumb));
    }

    MatchKind _kind;
    public MatchKind Kind { get => _kind; set { if (Set(ref _kind, value)) RaiseStatus(); } }

    string? _warning;
    public string? Warning { get => _warning; set { if (Set(ref _warning, value)) RaiseStatus(); } }

    bool _duplicate;
    public bool Duplicate { get => _duplicate; set { if (Set(ref _duplicate, value)) RaiseStatus(); } }

    bool _confirmed;
    public bool Confirmed
    {
        get => _confirmed;
        set { if (Set(ref _confirmed, value)) Raise(nameof(ConfirmedBox)); }
    }

    /// <summary>CheckBox.IsChecked is bool?.</summary>
    public bool? ConfirmedBox { get => Confirmed; set => Confirmed = value == true; }

    public bool NeedsCheck => Selected == null || Duplicate || Warning != null || Kind == MatchKind.Image;

    void RaiseStatus()
    {
        Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); Raise(nameof(StatusGlyph)); Raise(nameof(NeedsCheck));
    }

    public string StatusText
    {
        get
        {
            if (Selected == null) return Warning ?? "No match found. Type the file name below.";
            var s = Kind switch
            {
                MatchKind.FileName => "Found by file name",
                MatchKind.Ocr => "Found by the name written on the picture",
                MatchKind.Image => $"Found by image similarity ({Selected.Score * 100:0}%). Please check.",
                MatchKind.Manual => "Chosen by you",
                _ => ""
            };
            if (Warning != null) s += ". " + Warning;
            if (Duplicate) s += ". Another pick uses the same photo.";
            return s;
        }
    }

    enum Level { Ok, Check, Problem }
    Level StatusLevel => Selected == null || Duplicate || Warning != null ? Level.Problem
                       : Kind == MatchKind.Image ? Level.Check : Level.Ok;

    public Brush StatusBrush => ThemeBrush(StatusLevel switch
    {
        Level.Ok => "SystemFillColorSuccessBrush",
        Level.Check => "SystemFillColorCautionBrush",
        _ => "SystemFillColorCriticalBrush"
    });

    public string StatusGlyph => StatusLevel switch
    {
        Level.Ok => "",      // Completed
        Level.Check => "",   // Warning
        _ => ""              // ErrorBadge
    };
}

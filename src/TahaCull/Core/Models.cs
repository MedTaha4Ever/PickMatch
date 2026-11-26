using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TahaCull.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(p); return true;
    }
}

public sealed class Candidate : Observable
{
    public required PairItem Owner { get; init; }
    public required SourcePhoto Source { get; init; }
    public string Name => Source.Name;
    public double Score { get; set; }
    public string ScoreText => Score <= -1 ? "" : $"{Score:0.00}";

    BitmapSource? _thumb;
    public BitmapSource? Thumb { get => _thumb; set => Set(ref _thumb, value); }

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public enum MatchKind { None, FileName, Ocr, Image, Manual }

public sealed class PairItem : Observable
{
    string _clientPath = "";
    public string ClientPath { get => _clientPath; set { if (Set(ref _clientPath, value)) Raise(nameof(ClientName)); } }
    public string ClientName => Path.GetFileName(ClientPath);

    BitmapSource? _clientThumb;
    public BitmapSource? ClientThumb { get => _clientThumb; set => Set(ref _clientThumb, value); }

    public Gray? ClientGray { get; set; }

    public ObservableCollection<Candidate> Candidates { get; } = [];

    Candidate? _selected;
    public Candidate? Selected
    {
        get => _selected;
        set
        {
            if (_selected != null) _selected.IsSelected = false;
            if (Set(ref _selected, value) && value != null) value.IsSelected = true;
            Raise(nameof(StatusText)); Raise(nameof(StatusBrush));
        }
    }

    MatchKind _kind;
    public MatchKind Kind { get => _kind; set { Set(ref _kind, value); Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); } }

    bool _confirmed;
    public bool Confirmed { get => _confirmed; set => Set(ref _confirmed, value); }

    bool _duplicate;
    public bool Duplicate { get => _duplicate; set { Set(ref _duplicate, value); Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); } }

    string _note = "";
    public string Note { get => _note; set { Set(ref _note, value); Raise(nameof(StatusText)); } }

    string _manualName = "";
    public string ManualName { get => _manualName; set => Set(ref _manualName, value); }

    public bool NeedsCheck =>
        Selected == null || Duplicate || Kind == MatchKind.Image || !string.IsNullOrEmpty(Note);

    public string StatusText
    {
        get
        {
            if (Selected == null) return "No match found: type the name below";
            var s = Kind switch
            {
                MatchKind.FileName => "Matched by file name",
                MatchKind.Ocr => "Matched by name written on the picture",
                MatchKind.Image => $"Matched by image similarity ({Selected.Score:0.00}): please check",
                MatchKind.Manual => "Chosen by you",
                _ => ""
            };
            if (Duplicate) s += " · SAME CR3 AS ANOTHER PICK";
            if (!string.IsNullOrEmpty(Note)) s += " · " + Note;
            return s;
        }
    }

    public Brush StatusBrush => Selected == null || Duplicate || !string.IsNullOrEmpty(Note)
        ? Brushes.IndianRed
        : Kind == MatchKind.Image ? Brushes.Orange : Brushes.MediumSeaGreen;
}

using System.ComponentModel;

namespace Flamoris.StudioClient.App;

// WPF selection stores items in hash-based ItemInfo collections. Presentation
// state and event subscriptions must never change item equality/hash identity.
public sealed class LyricRow(Guid id, int number, string text, string start, string end) : INotifyPropertyChanged
{
    public Guid Id { get; } = id;
    public int Number { get; } = number;
    public string Text { get; } = text;
    public string Start { get; } = start;
    public string End { get; } = end;
    private bool isCurrent;
    public bool IsCurrent
    {
        get => isCurrent;
        set { if (isCurrent == value) return; isCurrent = value; PropertyChanged?.Invoke(this, new(nameof(IsCurrent))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

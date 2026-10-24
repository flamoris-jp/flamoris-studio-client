using System.ComponentModel;

namespace Flamoris.StudioClient.App;

public sealed record LyricRow(Guid Id, int Number, string Text, string Start, string End) : INotifyPropertyChanged
{
    private bool isCurrent;
    public bool IsCurrent
    {
        get => isCurrent;
        set { if (isCurrent == value) return; isCurrent = value; PropertyChanged?.Invoke(this, new(nameof(IsCurrent))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

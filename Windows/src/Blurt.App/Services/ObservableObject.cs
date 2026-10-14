using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Blurt.App.Services;

/// <summary>A view-model base that raises <see cref="PropertyChanged"/> when a backing field actually changes.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stores <paramref name="value"/> in <paramref name="field"/> and notifies; returns false when the value was already equal.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for each of <paramref name="names"/>.</summary>
    protected void Raise(params string?[] names)
    {
        foreach (var name in names)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

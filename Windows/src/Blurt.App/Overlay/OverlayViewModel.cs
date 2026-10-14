using System.ComponentModel;
using System.Runtime.CompilerServices;
using Blurt.Engine.Pipeline;

namespace Blurt.App.Overlay;

/// <summary>The overlay pill's bindable state: what it shows for the current <see cref="OverlayUIState"/>, and the live mic level.</summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private OverlayUIState _state = OverlayUIState.Idle;
    private OverlayPillLayout _layout = OverlayPillLayout.For(OverlayUIState.Idle);
    private float _level;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The displayed state. Assigning an equal value raises nothing.</summary>
    public OverlayUIState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            _layout = OverlayPillLayout.For(value);
            Raise();
            Raise(nameof(ShowsOrb));
            Raise(nameof(ShowsMeter));
            Raise(nameof(ShowsStatusWord));
            Raise(nameof(StatusWord));
            Raise(nameof(IsError));
            Raise(nameof(AccessibilityLabel));
        }
    }

    /// <summary>Whether the brand orb is drawn.</summary>
    public bool ShowsOrb => _layout.ShowsOrb;

    /// <summary>Whether the live meter is drawn.</summary>
    public bool ShowsMeter => _layout.ShowsMeter;

    /// <summary>Whether a status word is drawn.</summary>
    public bool ShowsStatusWord => _layout.StatusWord is not null;

    /// <summary>The status word in the pill's tracked uppercase, or empty.</summary>
    public string StatusWord => _layout.StatusWord?.ToUpperInvariant() ?? string.Empty;

    /// <summary>Whether the status word takes the error color.</summary>
    public bool IsError => _layout.IsError;

    /// <summary>The spoken label, also the pill's tooltip.</summary>
    public string AccessibilityLabel => _state.AccessibilityLabel;

    /// <summary>The current mic level, clamped to 0…1. Assigning an equal value raises nothing.</summary>
    public float Level
    {
        get => _level;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (clamped == _level)
            {
                return;
            }

            _level = clamped;
            Raise();
        }
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The modal style editor. The name is capped at <see cref="StyleProfileStore.NameLimit"/>
/// characters and the instructions at the store's byte limit as the user types, so text past a cap
/// is never storable; Save needs both fields filled. Delete appears only for a stored profile.
/// </summary>
public partial class StyleProfileEditor : Window
{
    private readonly StyleProfile _profile;
    private readonly int _characterLimit;
    private bool _clamping;

    /// <summary>Creates the editor for <paramref name="profile"/>, with instructions capped at <paramref name="characterLimit"/> UTF-8 bytes.</summary>
    public StyleProfileEditor(StyleProfile profile, bool isExisting, int characterLimit)
    {
        InitializeComponent();
        _profile = profile;
        _characterLimit = characterLimit;
        DeleteButton.Visibility = isExisting ? Visibility.Visible : Visibility.Collapsed;
        NameField.MaxLength = StyleProfileStore.NameLimit;
        NameField.Text = profile.Name;
        InstructionsField.Text = profile.Instructions;
        Refresh();
        Loaded += (_, _) => NameField.Focus();
    }

    /// <summary>The edited profile after Save, with the original id; null otherwise.</summary>
    public StyleProfile? Result { get; private set; }

    /// <summary>Whether the user chose Delete.</summary>
    public bool Deleted { get; private set; }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => Clamp(NameField, StyleProfileDraft.ClampName);

    private void OnInstructionsChanged(object sender, TextChangedEventArgs e) =>
        Clamp(InstructionsField, text => StyleProfileDraft.ClampInstructions(text, _characterLimit));

    private void Clamp(TextBox field, Func<string, string> clamp)
    {
        if (_clamping)
        {
            return;
        }

        var clamped = clamp(field.Text);
        if (clamped != field.Text)
        {
            _clamping = true;
            var caret = Math.Min(field.CaretIndex, clamped.Length);
            field.Text = clamped;
            field.CaretIndex = caret;
            _clamping = false;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (Counter is null || SaveButton is null)
        {
            return;
        }

        Counter.Text = StyleProfileDraft.Counter(InstructionsField.Text, _characterLimit);
        SaveButton.IsEnabled = StyleProfileDraft.CanSave(NameField.Text, InstructionsField.Text);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!StyleProfileDraft.CanSave(NameField.Text, InstructionsField.Text))
        {
            return;
        }

        Result = _profile with { Name = NameField.Text, Instructions = InstructionsField.Text };
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Deleted = true;
        DialogResult = true;
    }
}

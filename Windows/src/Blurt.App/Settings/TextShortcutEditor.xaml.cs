using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The modal text shortcut editor. The phrase is capped at <see cref="TextShortcutStore.TriggerLimit"/>
/// and the replacement at <see cref="TextShortcutStore.ExpansionLimit"/> characters as the user
/// types; Save needs a matchable phrase, a replacement, and no other shortcut on the same phrase.
/// Delete appears only for a stored shortcut.
/// </summary>
public partial class TextShortcutEditor : Window
{
    private readonly TextShortcut _shortcut;
    private readonly IReadOnlySet<string> _otherKeys;
    private bool _clamping;

    /// <summary>Creates the editor for <paramref name="shortcut"/>; <paramref name="otherKeys"/> are the other shortcuts' match keys.</summary>
    public TextShortcutEditor(TextShortcut shortcut, bool isExisting, IReadOnlySet<string> otherKeys)
    {
        InitializeComponent();
        _shortcut = shortcut;
        _otherKeys = otherKeys;
        DeleteButton.Visibility = isExisting ? Visibility.Visible : Visibility.Collapsed;
        PhraseField.MaxLength = TextShortcutStore.TriggerLimit;
        PhraseField.Text = shortcut.Trigger;
        ReplacementField.Text = shortcut.Expansion;
        Refresh();
        Loaded += (_, _) => PhraseField.Focus();
    }

    /// <summary>The edited shortcut after Save, with the original id; null otherwise.</summary>
    public TextShortcut? Result { get; private set; }

    /// <summary>Whether the user chose Delete.</summary>
    public bool Deleted { get; private set; }

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnReplacementChanged(object sender, TextChangedEventArgs e)
    {
        if (_clamping)
        {
            return;
        }

        var clamped = TextShortcutDraft.ClampExpansion(ReplacementField.Text);
        if (clamped != ReplacementField.Text)
        {
            _clamping = true;
            var caret = Math.Min(ReplacementField.CaretIndex, clamped.Length);
            ReplacementField.Text = clamped;
            ReplacementField.CaretIndex = caret;
            _clamping = false;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (SaveButton is null || DuplicateWarning is null)
        {
            return;
        }

        DuplicateWarning.Visibility = TextShortcutDraft.DuplicatesAnother(PhraseField.Text, _otherKeys) ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = TextShortcutDraft.CanSave(PhraseField.Text, ReplacementField.Text, _otherKeys);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TextShortcutDraft.CanSave(PhraseField.Text, ReplacementField.Text, _otherKeys))
        {
            return;
        }

        Result = _shortcut with { Trigger = PhraseField.Text, Expansion = ReplacementField.Text };
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Deleted = true;
        DialogResult = true;
    }
}

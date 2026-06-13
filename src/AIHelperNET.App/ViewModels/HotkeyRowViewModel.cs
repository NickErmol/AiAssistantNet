using AIHelperNET.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

/// <summary>One editable row in the Settings → Shortcuts tab: an action and its current chord.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    /// <summary>The action this row binds.</summary>
    public HotkeyId Id { get; }

    /// <summary>Human-readable action name (read-only).</summary>
    public string Description { get; }

    /// <summary>All bindable keys for the dropdown.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPF {Binding} requires an instance property.")]
    public IReadOnlyList<KeyChoice> KeyChoices => HotkeyKeys.Selectable;

    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _alt;
    [ObservableProperty] private bool _win;
    [ObservableProperty] private VirtualKey _selectedKey;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isRecording;

    private HotkeyRowViewModel(HotkeyId id, string description)
    {
        Id = id;
        Description = description;
    }

    /// <summary>Builds a row from a binding (used for both defaults and resolved/overridden chords).</summary>
    /// <param name="b">The binding to seed the row from.</param>
    /// <returns>A populated row.</returns>
    public static HotkeyRowViewModel FromBinding(HotkeyBinding b)
    {
        var row = new HotkeyRowViewModel(b.Id, b.Description);
        row.SetChord(b.Modifiers, b.Key);
        return row;
    }

    /// <summary>The combined modifier flags from the four checkboxes.</summary>
    /// <returns>The current modifier flags.</returns>
    public ModifierKeys ToModifiers()
    {
        var m = ModifierKeys.None;
        if (Ctrl)  m |= ModifierKeys.Ctrl;
        if (Shift) m |= ModifierKeys.Shift;
        if (Alt)   m |= ModifierKeys.Alt;
        if (Win)   m |= ModifierKeys.Win;
        return m;
    }

    /// <summary>Replaces the whole chord at once and clears any error (used by reset + recorder).</summary>
    /// <param name="mods">The new modifier flags.</param>
    /// <param name="key">The new key.</param>
    public void SetChord(ModifierKeys mods, VirtualKey key)
    {
        Ctrl  = mods.HasFlag(ModifierKeys.Ctrl);
        Shift = mods.HasFlag(ModifierKeys.Shift);
        Alt   = mods.HasFlag(ModifierKeys.Alt);
        Win   = mods.HasFlag(ModifierKeys.Win);
        SelectedKey = key;
        ErrorMessage = null;
    }

    /// <summary>The current chord as a binding (description carried from the default).</summary>
    /// <returns>The binding represented by this row.</returns>
    public HotkeyBinding ToBinding() => new(Id, ToModifiers(), SelectedKey, Description);

    /// <summary>The display gesture, e.g. <c>"Ctrl+Shift+Q"</c>.</summary>
    public string Gesture => ToBinding().Gesture;

    partial void OnCtrlChanged(bool value)  => OnPropertyChanged(nameof(Gesture));
    partial void OnShiftChanged(bool value) => OnPropertyChanged(nameof(Gesture));
    partial void OnAltChanged(bool value)   => OnPropertyChanged(nameof(Gesture));
    partial void OnWinChanged(bool value)   => OnPropertyChanged(nameof(Gesture));
    partial void OnSelectedKeyChanged(VirtualKey value) => OnPropertyChanged(nameof(Gesture));
}

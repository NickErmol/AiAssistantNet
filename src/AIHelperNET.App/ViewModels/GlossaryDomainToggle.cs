using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

/// <summary>A single glossary-domain checkbox row in Settings.</summary>
public sealed partial class GlossaryDomainToggle : ObservableObject
{
    /// <summary>Creates a toggle row.</summary>
    /// <param name="key">Stable glossary domain key persisted in settings.</param>
    /// <param name="displayName">Human-readable domain name.</param>
    /// <param name="isEnabled">Whether this domain biases transcription.</param>
    public GlossaryDomainToggle(string key, string displayName, bool isEnabled)
    {
        Key = key;
        DisplayName = displayName;
        _isEnabled = isEnabled;
    }

    /// <summary>Stable domain key persisted in settings.</summary>
    public string Key { get; }

    /// <summary>Human-readable domain name.</summary>
    public string DisplayName { get; }

    /// <summary>Whether this domain biases transcription.</summary>
    [ObservableProperty] private bool _isEnabled;
}

using System.Collections.ObjectModel;
using AIHelperNET.Domain.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

/// <summary>Displays the live transcript of the current session.</summary>
public sealed partial class TranscriptViewModel : ObservableObject
{
    /// <summary>Gets the collection of transcript items for the current session.</summary>
    [ObservableProperty]
    private ObservableCollection<TranscriptItemVm> _items = [];

    /// <summary>Adds a new transcript item to the display.</summary>
    /// <param name="item">The domain transcript item to display.</param>
    public void AddItem(TranscriptItem item)
        => Items.Add(new TranscriptItemVm(item.Speaker, item.Text, item.Timestamp));

    /// <summary>Clears all transcript items.</summary>
    public void Clear() => Items.Clear();
}

/// <summary>View model for a single transcript item.</summary>
public sealed class TranscriptItemVm(Speaker speaker, string text, DateTimeOffset timestamp)
{
    /// <summary>Gets the speaker label for display.</summary>
    public string SpeakerLabel   => speaker == Speaker.Me ? "Me" : "Other";

    /// <summary>Gets the speaker color for display.</summary>
    public string SpeakerColor   => speaker == Speaker.Me ? "#FFAA44" : "#44AAFF";

    /// <summary>Gets the transcript text.</summary>
    public string Text           => text;

    /// <summary>Gets the formatted timestamp label.</summary>
    public string TimestampLabel => timestamp.ToLocalTime().ToString("HH:mm:ss");
}

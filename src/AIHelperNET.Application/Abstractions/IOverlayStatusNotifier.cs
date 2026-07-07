namespace AIHelperNET.Application.Abstractions;

/// <summary>Pushes a transient status message to the overlay header (empty string clears it).</summary>
public interface IOverlayStatusNotifier
{
    /// <summary>Pushes a transient status message to the overlay header.</summary>
    /// <param name="message">The message to display; an empty string clears it.</param>
    void Notify(string message);
}

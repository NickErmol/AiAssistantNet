namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Projection of an audio input/output device.</summary>
/// <param name="Id">Device identifier.</param>
/// <param name="Name">Human-readable device name.</param>
/// <param name="IsDefault">Whether this is the system default device.</param>
public sealed record AudioDeviceDto(string Id, string Name, bool IsDefault);

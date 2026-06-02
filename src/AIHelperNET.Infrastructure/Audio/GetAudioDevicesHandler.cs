using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using FluentResults;
using Mediator;
using NAudio.CoreAudioApi;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class GetAudioDevicesHandler
    : IRequestHandler<GetAudioDevicesQuery, Result<IReadOnlyList<AudioDeviceDto>>>
{
    public ValueTask<Result<IReadOnlyList<AudioDeviceDto>>> Handle(
        GetAudioDevicesQuery request, CancellationToken cancellationToken)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
            .Select(d => new AudioDeviceDto(d.ID, d.FriendlyName, false))
            .ToList();

        return ValueTask.FromResult(Result.Ok<IReadOnlyList<AudioDeviceDto>>(devices));
    }
}

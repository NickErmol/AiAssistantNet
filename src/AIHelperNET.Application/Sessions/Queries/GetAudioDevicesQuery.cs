using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

/// <summary>
/// Query to enumerate available audio input/output devices.
/// Handler lives in Infrastructure (requires NAudio); only the query is declared here.
/// </summary>
public sealed record GetAudioDevicesQuery : IRequest<Result<IReadOnlyList<AudioDeviceDto>>>;

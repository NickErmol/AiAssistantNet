using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>A named snapshot of CodeProfile + AnswerSettings for quick switching between interview types.</summary>
public sealed record ProfilePreset(
    string Name,
    CodeProfile CodeProfile,
    AnswerSettings AnswerSettings);

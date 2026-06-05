// src/AIHelperNET.App/Windows/EnumValues.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.App.Windows;

/// <summary>A display/value pair for enum ComboBox bindings.</summary>
public sealed record EnumOption<T>(string Display, T Value);

/// <summary>Static lists of enum options for XAML ComboBox bindings.</summary>
public static class EnumValues
{
    public static IReadOnlyList<EnumOption<AnswerLength>> AnswerLengths { get; } =
    [
        new("Very Short", AnswerLength.VeryShort),
        new("Short",      AnswerLength.ShortLength),
        new("Medium",     AnswerLength.Medium),
        new("Detailed",   AnswerLength.Detailed),
        new("Deep Dive",  AnswerLength.DeepDive),
    ];

    public static IReadOnlyList<EnumOption<AnswerComplexity>> AnswerComplexities { get; } =
    [
        new("Simple",   AnswerComplexity.Simple),
        new("Balanced", AnswerComplexity.Balanced),
        new("Advanced", AnswerComplexity.Advanced),
        new("Senior",   AnswerComplexity.Senior),
    ];

    public static IReadOnlyList<EnumOption<AnswerStyle>> AnswerStyles { get; } =
    [
        new("Natural",      AnswerStyle.Natural),
        new("Interview",    AnswerStyle.Interview),
        new("Technical",    AnswerStyle.Technical),
        new("Step-by-Step", AnswerStyle.StepByStep),
        new("Code First",   AnswerStyle.CodeFirst),
        new("Architecture", AnswerStyle.Architecture),
        new("Debugging",    AnswerStyle.Debugging),
    ];

    public static IReadOnlyList<EnumOption<WhisperModelSize>> WhisperModelSizes { get; } =
    [
        new("Tiny",        WhisperModelSize.Tiny),
        new("Base",        WhisperModelSize.Base),
        new("Small",       WhisperModelSize.Small),
        new("Medium",      WhisperModelSize.Medium),
        new("Large Turbo", WhisperModelSize.LargeTurbo),
        new("Large",       WhisperModelSize.Large),
    ];
}

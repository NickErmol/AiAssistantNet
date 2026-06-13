using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
        => HotkeyValidator.Validate(HotkeyDefaults.All).Should().BeEmpty();

    [Fact]
    public void BareKey_IsFlagged()
    {
        var bindings = new[]
        {
            new HotkeyBinding(HotkeyId.GenerateAnswer, ModifierKeys.None, VirtualKey.G, "Generate")
        };

        var errors = HotkeyValidator.Validate(bindings);

        errors.Should().ContainKey(HotkeyId.GenerateAnswer);
        errors[HotkeyId.GenerateAnswer].Should().Contain("modifier");
    }

    [Fact]
    public void DuplicateChord_FlagsBothActions()
    {
        var bindings = new[]
        {
            new HotkeyBinding(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.G, "Generate"),
            new HotkeyBinding(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.G, "Copy")
        };

        var errors = HotkeyValidator.Validate(bindings);

        errors.Should().ContainKeys(HotkeyId.GenerateAnswer, HotkeyId.CopyAnswer);
        errors[HotkeyId.GenerateAnswer].Should().Contain("Copy");
        errors[HotkeyId.CopyAnswer].Should().Contain("Generate");
    }
}

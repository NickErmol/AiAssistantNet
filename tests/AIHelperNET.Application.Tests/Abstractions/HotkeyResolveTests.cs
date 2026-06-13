using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyResolveTests
{
    [Fact]
    public void Resolve_NoOverrides_EqualsDefaults()
        => HotkeyDefaults.Resolve([]).Should().BeEquivalentTo(HotkeyDefaults.All);

    [Fact]
    public void Resolve_ReplacesOnlyMatchingId_AndKeepsDescription()
    {
        var overrides = new[] { new HotkeyOverride(HotkeyId.GenerateAnswer,
            ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G) };

        var resolved = HotkeyDefaults.Resolve(overrides);

        var changed = resolved.Single(b => b.Id == HotkeyId.GenerateAnswer);
        changed.Modifiers.Should().Be(ModifierKeys.Ctrl | ModifierKeys.Alt);
        changed.Key.Should().Be(VirtualKey.G);
        changed.Description.Should().Be(
            HotkeyDefaults.All.Single(b => b.Id == HotkeyId.GenerateAnswer).Description);

        resolved.Where(b => b.Id != HotkeyId.GenerateAnswer)
            .Should().BeEquivalentTo(HotkeyDefaults.All.Where(b => b.Id != HotkeyId.GenerateAnswer));
    }
}

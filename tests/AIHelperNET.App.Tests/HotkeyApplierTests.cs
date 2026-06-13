using AIHelperNET.App.Hotkeys;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using FluentResults;
using Xunit;

namespace AIHelperNET.App.Tests;

public class HotkeyApplierTests
{
    /// <summary>Fake hotkey service: records registrations and fails the chords named in <see cref="FailIds"/>.</summary>
    private sealed class FakeHotkeyService : IGlobalHotkeyService
    {
        public int UnregisterCalls;
        public readonly List<HotkeyId> Registered = [];
        public HashSet<HotkeyId> FailIds = [];

#pragma warning disable CS0067
        public event EventHandler<HotkeyId>? HotkeyPressed;
#pragma warning restore CS0067

        public Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key)
        {
            if (FailIds.Contains(id)) return Result.Fail("in use");
            Registered.Add(id);
            return Result.Ok();
        }

        public void UnregisterAll() { UnregisterCalls++; Registered.Clear(); }
    }

    [Fact]
    public void Apply_UnregistersThenRegistersAll_ReturnsNoFailures()
    {
        var svc = new FakeHotkeyService();
        var applier = new HotkeyApplier(svc);

        var failures = applier.Apply(HotkeyDefaults.All);

        failures.Should().BeEmpty();
        svc.UnregisterCalls.Should().Be(1);
        svc.Registered.Should().BeEquivalentTo(HotkeyDefaults.All.Select(b => b.Id));
    }

    [Fact]
    public void Apply_ReturnsIds_TheOsRejected()
    {
        var svc = new FakeHotkeyService { FailIds = [HotkeyId.CaptureScreen] };
        var applier = new HotkeyApplier(svc);

        var failures = applier.Apply(HotkeyDefaults.All);

        failures.Should().ContainSingle().Which.Should().Be(HotkeyId.CaptureScreen);
    }
}

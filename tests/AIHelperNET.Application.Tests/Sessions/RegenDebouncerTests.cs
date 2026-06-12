using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class RegenDebouncerTests
{
    private static readonly ConversationTurnId Turn = new(Guid.NewGuid());

    [Fact]
    public void BurstOfTouchesWithinWindow_FiresExactlyOnce()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(300));
        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(300));
        sut.Touch(Turn, () => fired++);

        fired.Should().Be(0, "still within the debounce window after each reset");

        time.Advance(TimeSpan.FromMilliseconds(1000));
        fired.Should().Be(1, "a burst collapses into a single regeneration");
    }

    [Fact]
    public void TouchesSpacedBeyondWindow_FireForEachQuietGap()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        fired.Should().Be(1);

        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        fired.Should().Be(2);
    }

    [Fact]
    public void Cancel_BeforeWindowElapses_NeverFires()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        sut.Cancel(Turn);
        time.Advance(TimeSpan.FromMilliseconds(2000));

        fired.Should().Be(0);
    }

    [Fact]
    public void Dispose_CancelsPendingTimers()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        sut.Dispose();
        time.Advance(TimeSpan.FromMilliseconds(2000));

        fired.Should().Be(0);
    }
}

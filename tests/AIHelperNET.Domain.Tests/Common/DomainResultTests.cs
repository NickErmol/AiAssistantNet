using AIHelperNET.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Common;

public class DomainResultTests
{
    [Fact]
    public void Ok_IsSuccess_True()
    {
        var r = DomainResult.Ok();
        r.IsSuccess.Should().BeTrue();
        r.IsFailed.Should().BeFalse();
        r.Error.Should().BeEmpty();
    }

    [Fact]
    public void Fail_IsSuccess_False()
    {
        var r = DomainResult.Fail("bad");
        r.IsSuccess.Should().BeFalse();
        r.IsFailed.Should().BeTrue();
        r.Error.Should().Be("bad");
    }

    [Fact]
    public void OkT_CarriesValue()
    {
        var r = DomainResult.Ok(42);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
        r.Error.Should().BeEmpty();
    }

    [Fact]
    public void FailT_HasError()
    {
        var r = DomainResult.Fail<int>("oops");
        r.IsFailed.Should().BeTrue();
        r.Error.Should().Be("oops");
    }
}

using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace AIHelperNET.Integration.Tests.Architecture;

public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly      = typeof(AIHelperNET.Domain.Sessions.Session).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(AIHelperNET.Application.DependencyInjection).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOnApplication()
    {
        Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Application")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_ShouldNotDependOnInfrastructure()
    {
        Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_ShouldNotDependOnInfrastructure()
    {
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void QuestionDetector_LivesInDomain()
    {
        typeof(AIHelperNET.Domain.Questions.QuestionDetector).Assembly
            .Should().BeSameAs(DomainAssembly);
    }
}

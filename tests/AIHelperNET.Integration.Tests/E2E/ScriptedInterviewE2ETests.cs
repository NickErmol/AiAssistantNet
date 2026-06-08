using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class ScriptedInterviewE2ETests : IAsyncLifetime
{
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static BoundaryClassificationResult NewQuestion(string text) =>
        new(BoundaryLabel.NewQuestion, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: false, ShouldCreateNewTurn: true,
            NormalizedQuestionText: text, Reason: "scripted");

    private static BoundaryClassificationResult AdditionalRequirement(string text) =>
        new(BoundaryLabel.AdditionalRequirement, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: true, ShouldCreateNewTurn: false,
            NormalizedQuestionText: text, Reason: "scripted");

    [Fact]
    public async Task Scenario1_TwoOtherQuestions_ProduceTwoAnsweredCards()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;

        await using var scope = _host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ISessionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var pipeline = sp.GetRequiredService<TranscriptPipelineService>();
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);

        var driver = new InterviewDriver(pipeline, uow, _host.Sink, _host.Classifier, _host.Services);

        await driver.OtherAsync(session, "What is dependency injection in one sentence?",
            NewQuestion("What is dependency injection?"));
        await driver.OtherAsync(session, "Now explain CQRS in one sentence?",
            NewQuestion("Now explain CQRS?"));

        await using var verifyScope = _host.Services.CreateAsyncScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var reloaded = (await verifyRepo.GetAsync(session.Id, default)).Value;

        reloaded.ConversationTurns.Should().HaveCount(2);
        reloaded.ConversationTurns.Should().OnlyContain(t => t.AnswerVersions.Count >= 1);
        _host.Sink.Errors.Should().BeEmpty();
    }
}

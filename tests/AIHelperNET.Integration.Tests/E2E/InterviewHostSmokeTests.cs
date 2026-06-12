using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class InterviewHostSmokeTests
{
    [Fact]
    public async Task Host_Boots_CreatesSchema_AndAnswerCommandPersistsViaRealMediator()
    {
        await using var host = await InterviewHost.CreateAsync();

        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        var q = DetectedQuestion.Create("What is DI?", QuestionSource.Audio, DateTimeOffset.UtcNow);
        session.AddDetectedQuestion(q);
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is DI?", DateTimeOffset.UtcNow, 0.9f));
        var turn = session.AddConversationTurn(q.Id, "What is DI?", DateTimeOffset.UtcNow).Value;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(session, default);
            await uow.SaveChangesAsync(default);
        }

        var mediator = host.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary), default);
        result.IsSuccess.Should().BeTrue();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var reloaded = (await repo.GetAsync(session.Id, default)).Value;
            var t = reloaded.ConversationTurns.Single();
            t.AnswerVersions.Should().HaveCount(1);
            t.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        }

        var feedback = host.Services.GetRequiredService<ITurnStatusFeedback>();
        var statuses = new List<ConversationTurnStatus>();
        while (feedback.TryDrain(out var e)) statuses.Add(e.Status);
        statuses.Should().Contain(ConversationTurnStatus.PreliminaryReady);
    }
}

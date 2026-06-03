using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class SessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_ValidInputs_ReturnsActiveSession()
    {
        var result = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now);
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(SessionState.Active);
        result.Value.StartedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_NullSettings_Fails()
    {
        var result = Session.Create(null!, CodeProfile.Empty, Now);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Create_NullProfile_Fails()
    {
        var result = Session.Create(AnswerSettings.Default, null!, Now);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddTranscriptItem_ActiveSession_Succeeds()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var item = TranscriptItem.Create(Speaker.Other, "Hello", Now, 0.9f);
        session.AddTranscriptItem(item).IsSuccess.Should().BeTrue();
        session.Transcript.Should().HaveCount(1);
    }

    [Fact]
    public void AddTranscriptItem_StoppedSession_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        var item = TranscriptItem.Create(Speaker.Me, "Late", Now, 0.8f);
        session.AddTranscriptItem(item).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddDetectedQuestion_StoppedSession_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        var q = DetectedQuestion.Create("Why?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void StartAnswer_UnknownQuestion_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var result = session.StartAnswer(QuestionId.New(), Now);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void StartAnswer_KnownQuestion_ReturnsStreamingAnswer()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var q = DetectedQuestion.Create("What is DI?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var result = session.StartAnswer(q.Id, Now);
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(AnswerStatus.Streaming);
    }

    [Fact]
    public void Stop_AlreadyStopped_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        session.Stop(Now.AddSeconds(1)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Stop_Active_SetsEndedAt()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now.AddMinutes(30));
        session.EndedAt.Should().Be(Now.AddMinutes(30));
        session.State.Should().Be(SessionState.Stopped);
    }

    [Fact]
    public void UpdateAnswerSettings_Succeeds()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var newSettings = AnswerSettings.Default with { OutputLanguage = "Polish" };
        session.UpdateAnswerSettings(newSettings).IsSuccess.Should().BeTrue();
        session.AnswerSettings.OutputLanguage.Should().Be("Polish");
    }

    [Fact]
    public void Create_DefaultMode_IsAudioAndScreen()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Mode.Should().Be(SessionMode.AudioAndScreen);
        session.AudioSource.Should().Be(AudioSourceMode.Both);
    }

    [Fact]
    public void ChangeMode_ActiveSession_Succeeds()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var result = session.ChangeMode(SessionMode.AudioOnly, AudioSourceMode.MicrophoneOnly);
        result.IsSuccess.Should().BeTrue();
        session.Mode.Should().Be(SessionMode.AudioOnly);
        session.AudioSource.Should().Be(AudioSourceMode.MicrophoneOnly);
    }

    [Fact]
    public void ChangeMode_StoppedSession_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        session.ChangeMode(SessionMode.ScreenOnly, AudioSourceMode.Both).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddConversationTurn_ActiveSession_Succeeds()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var q = DetectedQuestion.Create("Q?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var result = session.AddConversationTurn(q.Id, "Q?", Now);
        result.IsSuccess.Should().BeTrue();
        session.ConversationTurns.Should().HaveCount(1);
    }

    [Fact]
    public void ActiveTurn_NoTurns_ReturnsNull()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.ActiveTurn.Should().BeNull();
    }

    [Fact]
    public void ActiveTurn_DismissedTurn_ReturnsNull()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var q = DetectedQuestion.Create("Q?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Q?", Now).Value;
        turn.Dismiss();
        session.ActiveTurn.Should().BeNull();
    }
}

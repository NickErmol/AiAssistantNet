using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Integration.Tests.E2E;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class FakesTests
{
    [Fact]
    public async Task FakeAnswerProvider_EchoesPromptUser()
    {
        var provider = new FakeAnswerProvider();
        var prompt = new AnswerPrompt("sys", "Question: What is DI?", "English", 256);

        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in provider.StreamAnswerAsync(prompt, default))
            sb.Append(chunk);

        sb.ToString().Should().Contain("What is DI?");
    }

    [Fact]
    public void FakeAnswerProviderResolver_AlwaysReturnsTheFake()
    {
        var provider = new FakeAnswerProvider();
        var resolver = new FakeAnswerProviderResolver(provider);
        resolver.Resolve(AiBackend.Claude).Should().BeSameAs(provider);
        resolver.Resolve(AiBackend.Ollama).Should().BeSameAs(provider);
    }

    [Fact]
    public async Task FakeBoundaryClassifier_ReturnsScriptedThenAmbiguous()
    {
        var classifier = new FakeQuestionBoundaryClassifier();
        classifier.Enqueue(new BoundaryClassificationResult(
            BoundaryLabel.NewQuestion, 0.95, true, false, true, "Q", "scripted"));

        var item = TranscriptItem.Create(Speaker.Other, "Q", DateTimeOffset.UnixEpoch, 0.9f);
        var first = await classifier.ClassifyAsync(null, new[] { item }, item, Speaker.Other, default);
        var second = await classifier.ClassifyAsync(null, new[] { item }, item, Speaker.Other, default);

        first.Classification.Should().Be(BoundaryLabel.NewQuestion);
        second.Classification.Should().Be(BoundaryLabel.NoQuestion); // Ambiguous default
    }
}

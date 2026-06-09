using AIHelperNET.Application;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure;
using AIHelperNET.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// A headless DI host that boots the real Application + Infrastructure registrations over an
/// in-memory SQLite database (schema created via migrations, matching the app), overriding
/// only the AI ports, the settings store, and the sinks. One host per test keeps the singleton
/// pipeline state isolated.
/// </summary>
public sealed class InterviewHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SqliteConnection _keepAlive;

    /// <summary>The resolved service provider for the headless host.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>The capturing answer sink (singleton) for awaiting completions and reading text.</summary>
    public CapturingAnswerStreamSink Sink { get; }

    /// <summary>The scripted boundary classifier (singleton) to enqueue per-Other-step results.</summary>
    public FakeQuestionBoundaryClassifier Classifier { get; }

    /// <summary>The capturing transcript sink (singleton) for asserting per-speaker transcripts.</summary>
    public CapturingTranscriptSink Transcripts { get; }

    private InterviewHost(ServiceProvider provider, SqliteConnection keepAlive,
        CapturingAnswerStreamSink sink, FakeQuestionBoundaryClassifier classifier,
        CapturingTranscriptSink transcripts)
    {
        _provider = provider;
        _keepAlive = keepAlive;
        Sink = sink;
        Classifier = classifier;
        Transcripts = transcripts;
    }

    /// <summary>Builds the host, opens the shared in-memory DB, and applies migrations.</summary>
    public static async Task<InterviewHost> CreateAsync(bool useRealAnswerProvider = false)
    {
        var dbName = "interview-e2e-" + Guid.NewGuid().ToString("N");
        var connString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connString);
        await keepAlive.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        IConfiguration config = new ConfigurationBuilder().Build();
        services.AddApplication();
        services.AddInfrastructure(config);

        RemoveDbContext(services);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connString));

        var fakeProvider = new FakeAnswerProvider();
        if (!useRealAnswerProvider)
            services.AddSingleton<IAnswerProviderResolver>(new FakeAnswerProviderResolver(fakeProvider));
        var classifier = new FakeQuestionBoundaryClassifier();
        services.AddSingleton<IQuestionBoundaryClassifier>(classifier);
        services.AddSingleton<ISettingsStore, StubSettingsStore>();

        var sink = new CapturingAnswerStreamSink();
        services.AddSingleton<IAnswerStreamSink>(sink);
        var transcripts = new CapturingTranscriptSink();
        services.AddSingleton<ITranscriptSink>(transcripts);
        services.AddSingleton<IConversationTurnSink>(Substitute.For<IConversationTurnSink>());
        // Override the file-writing recorder so the E2E never touches the real data root.
        services.AddSingleton<IBoundaryDecisionRecorder>(Substitute.For<IBoundaryDecisionRecorder>());

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        return new InterviewHost(provider, keepAlive, sink, classifier, transcripts);
    }

    private static void RemoveDbContext(ServiceCollection services)
    {
        var toRemove = services.Where(d =>
            d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
            d.ServiceType == typeof(DbContextOptions) ||
            d.ServiceType == typeof(AppDbContext)).ToList();
        foreach (var d in toRemove) services.Remove(d);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}

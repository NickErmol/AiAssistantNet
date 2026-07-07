using System.Net.Http;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>
/// Opt-in live eval for the candidate-profile condensation feature. Supplies a realistic
/// ~350-word C#/Azure resume and a short JD to <see cref="ProfileCondenser"/> and asserts
/// the model produces a well-formed profile card without hallucinating absent technologies.
///
/// <para>Self-skips (passes trivially) when no Anthropic API key is stored in Windows
/// Credential Manager (target <c>AIHelperNET:ClaudeApiKey</c>), so CI and offline runs
/// stay green.</para>
/// </summary>
[Trait("Category", "LiveLlm")]
public class CandidateProfileLiveTests(ITestOutputHelper output)
{
    // ─── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Realistic ~350-word resume. Two named companies; APIM + Key Vault project;
    /// skills: C#, .NET, Azure API Management, Key Vault, PostgreSQL, RabbitMQ, xUnit.
    /// Deliberately contains NO mention of Kubernetes (the canary for hallucination).
    /// </summary>
    private const string ResumeText = """
        JANE SMITH
        jane.smith@example.com | github.com/janesmith | Seattle, WA

        SUMMARY
        Backend engineer with 6+ years building distributed systems on .NET and Azure.
        Experienced in event-driven microservices, API security, and cloud infrastructure.
        Strong focus on testable, maintainable code and developer-friendly internal tooling.

        EXPERIENCE

        Lead Backend Engineer — Fabrikam Systems (2023 – present)
        Fabrikam Systems builds financial data pipelines and developer tooling for mid-market banks.
        - Designed and owns the company's Azure API Management gateway: 12 product APIs,
          subscription key rotation automation, and a custom policy library covering JWT validation,
          rate limiting, and IP allow-listing.
        - Migrated all service secrets from environment variables to Azure Key Vault references,
          reducing blast radius from a credential leak and enabling per-service managed-identity
          access policies.
        - Led adoption of RabbitMQ for inter-service messaging; authored the internal C# client
          wrapper (retry, dead-letter, outbox pattern) now used across 8 services.
        - Mentors two junior engineers; runs bi-weekly architecture review sessions.
        Stack: C#, .NET 8, Azure API Management, Azure Key Vault, PostgreSQL, RabbitMQ, xUnit.

        Senior .NET Developer — Contoso Financial (2019 – 2023)
        Contoso Financial provides investment-portfolio analytics to independent financial advisors.
        - Built the core portfolio-calculation engine (C# / .NET 6) handling 500 k positions/day
          with < 2 s p99 latency.
        - Introduced xUnit + integration tests (Testcontainers/PostgreSQL), raising coverage from
          18 % to 74 % and cutting production regressions by ~60 % year-over-year.
        - Maintained PostgreSQL schemas (complex views, partial indexes, pg_cron jobs) and
          performance-tuned slow queries identified via EXPLAIN ANALYZE.
        - Coordinated quarterly dependency-upgrade sprints; authored the internal runbook for
          .NET major-version migrations.
        Stack: C#, .NET 6, PostgreSQL, Azure DevOps, xUnit.

        SKILLS
        Languages: C#, SQL, Bash
        Platforms: .NET (6/8/10), Azure (API Management, Key Vault, Service Bus, App Service)
        Data: PostgreSQL, Redis
        Messaging: RabbitMQ
        Testing: xUnit, NSubstitute, Testcontainers, Playwright
        Tools: Git, GitHub Actions, Docker

        EDUCATION
        B.S. Computer Science — University of Washington, 2019
        """;

    /// <summary>
    /// Short job description. Mentions Azure, microservices, event-driven systems.
    /// Does NOT mention Kubernetes.
    /// </summary>
    private const string JobDescription = """
        Senior .NET Engineer — CloudPay Inc.

        We are looking for a Senior .NET Engineer to join our platform team building
        the next generation of payment microservices on Azure.

        You will:
        - Design and own backend microservices (C# / .NET 8+) with high reliability SLAs.
        - Work with Azure services (API Management, Service Bus, Key Vault, App Service).
        - Drive event-driven architecture patterns across our service mesh.
        - Collaborate with product and infrastructure to deliver features end-to-end.

        Must-have skills: C#, .NET 8+, Azure, microservices, event-driven systems,
        SQL (PostgreSQL preferred), unit and integration testing.
        """;

    [Fact]
    public async Task CondenseAsync_LiveCall_ProducesWellFormedCardWithNoHallucinations()
    {
        // ── Guard: skip if no API key ─────────────────────────────────────────
        var secrets = new WindowsCredentialSecretStore();
        if (!secrets.HasApiKey())
        {
            output.WriteLine("Skipped: no Claude API key in Windows Credential Manager " +
                "(target 'AIHelperNET:ClaudeApiKey').");
            return;
        }

        // ── Construct ProfileCondenser directly (no DI host) ──────────────────
        var opts = new ClaudeOptions();          // defaults: api.anthropic.com, 2023-06-01
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        IOptions<ClaudeOptions> optionsWrapper = Options.Create(opts);

        var condenser = new ProfileCondenser(http, secrets, optionsWrapper);

        // ── Call the real API ─────────────────────────────────────────────────
        var result = await condenser.CondenseAsync(ResumeText, JobDescription, CancellationToken.None);

        output.WriteLine("=== CONDENSE RESULT ===");
        if (result.IsSuccess)
            output.WriteLine(result.Value);
        else
            output.WriteLine("FAILED: " + string.Join("; ", result.Errors.Select(e => e.Message)));
        output.WriteLine("");

        result.IsSuccess.Should().BeTrue(
            "the condenser should succeed; errors: {0}",
            string.Join("; ", result.Errors.Select(e => e.Message)));

        var card = result.Value;

        // ── Assertion 1: required section markers ─────────────────────────────
        card.Should().Contain("**CANDIDATE PROFILE**",
            because: "the prompt requires a bold CANDIDATE PROFILE section marker");

        card.Should().Contain("**TARGET ROLE**",
            because: "a job description was supplied, so the TARGET ROLE block must be present");

        // ── Assertion 2: both company names present ───────────────────────────
        card.Should().Contain("Fabrikam Systems",
            because: "Fabrikam Systems is a named employer in the resume");

        card.Should().Contain("Contoso Financial",
            because: "Contoso Financial is a named employer in the resume");

        // ── Assertion 3: hallucination canary — Kubernetes must NOT appear ────
        card.Should().NotContainEquivalentOf("Kubernetes",
            because: "Kubernetes never appears in the resume or JD and must not be invented");

        // ── Assertion 4: total length within budget ───────────────────────────
        card.Length.Should().BeLessThan(5000,
            because: "the condensation prompt caps output at ~600 tokens (~2400 chars); " +
                     "5000 chars is a generous upper bound that still catches runaway generation");

        // ── Assertion 5: no markdown headings (# prefix banned by prompt) ─────
        card.Should().NotContain("## ",
            because: "the prompt forbids '#' headings — section titles must be bold text, not ATX headings");
    }
}

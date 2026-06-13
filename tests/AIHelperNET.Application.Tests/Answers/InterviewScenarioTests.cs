using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>End-to-end-ish scenario tests derived from the manual QA doc (Test 1.docx): each
/// scenario is interviewer speech + a captured screen (OCR) + an optional delayed follow-up. They
/// pin the deterministic chain that runs before the LLM call — mode classification and prompt
/// assembly — without any live model.</summary>
public class InterviewScenarioTests
{
    /// <summary>One interview situation: what the interviewer said, what the screen showed, the
    /// mode it should select (null = no auto mode), the candidate stack in play, and an optional
    /// delayed follow-up with the prior answer it refines.</summary>
    public sealed record InterviewScenario(
        string Name,
        string InterviewerSpeech,
        string ScreenOcr,
        ScreenAnalysisMode? ExpectedMode,
        CodeProfile Profile,
        string? FollowUpSpeech = null,
        string? PriorAnswer = null);

    private static readonly CodeProfile DotNet = CodeProfile.Empty with
    {
        ProgrammingLanguage = "C#",
        BackendFramework = "ASP.NET Core",
        Database = "EF Core",
    };

    private static readonly CodeProfile Angular = CodeProfile.Empty with
    {
        ProgrammingLanguage = "TypeScript",
        FrontendFramework = "Angular",
        TestingFramework = "Jasmine",
    };

    // OCR transcriptions of the three docx screenshots.
    private const string SqlAccessOcr =
        "Student Table\nID Name Age\n1 A 20\n2 B 21\n3 C 23\n4 D 24\n" +
        "Locations\nId Name\n1 Library\n2 FoodCourt\n3 Auditorium\n" +
        "StudentLocAccess\nId StudId LocationId\n1 1 1\n2 1 2\n3 2 1\n4 3 1\n5 3 2\n6 3 3";

    private const string ManagerOcr =
        "Employees\nA B C D E X Y\n" +
        "EmployeeManager\nA->B\nB->C\nC->D\nX->E\nE->Y\nY->X";

    private const string SuperManagerOcr =
        "DB Table: Employees\nEmployeeId | FullName\nA | Adam\nB | Bryan\nC | Celie\nD | Dany\n" +
        "E | Edward\nX | Xing\nZ | Zang\n" +
        "DB Table: EmployeeManagerRelation\nEmployeeId | ManagerId\n" +
        "A | B\nB | C\nC | D\nX | E\nE | Y\nY | X  (circular/break)";

    public static readonly IReadOnlyList<InterviewScenario> Catalog =
    [
        // ── The three Test 1.docx scenarios ──────────────────────────────────────
        new("Sql_StudentLocationAccess",
            "write a SQL to get the students who has an access to the location more than 2",
            SqlAccessOcr,
            ScreenAnalysisMode.SolveCodingTask,
            CodeProfile.Empty,
            FollowUpSpeech: "now select only distinct values",
            PriorAnswer: "SELECT s.Name FROM Student s JOIN StudentLocAccess a ON a.StudId = s.ID " +
                         "GROUP BY s.ID, s.Name HAVING COUNT(a.LocationId) > 2;"),

        new("Sql_FindMainManager",
            "write a SQL to find the main manager",
            ManagerOcr,
            ScreenAnalysisMode.SolveCodingTask,
            CodeProfile.Empty),

        new("Csharp_SuperManager",
            "write a C# method to return the super manager",
            SuperManagerOcr,
            ScreenAnalysisMode.SolveCodingTask,
            CodeProfile.Empty),

        // ── .NET scenarios ───────────────────────────────────────────────────────
        new("DotNet_Coding",
            "implement a minimal API endpoint that returns an order by id",
            "public record Order(int Id, decimal Total);\n// GET /orders/{id} -> 404 when missing",
            ScreenAnalysisMode.SolveCodingTask,
            DotNet),

        new("DotNet_Debug",
            "can you fix the bug, why is this request deadlocking",
            "public string Get() => GetAsync().Result; // blocks on async in ASP.NET context",
            ScreenAnalysisMode.DebugError,
            DotNet),

        new("DotNet_Explain",
            "explain this code for me",
            "var top = orders.GroupBy(o => o.CustomerId)\n" +
            "    .Select(g => new { g.Key, Total = g.Sum(x => x.Total) })\n" +
            "    .OrderByDescending(t => t.Total).Take(3);",
            ScreenAnalysisMode.ExplainCode,
            DotNet),

        new("DotNet_Design",
            "how would you design this checkout service",
            "Requirements: checkout service, 10k rps, idempotent payments, audit trail",
            ScreenAnalysisMode.SystemDesign,
            DotNet),

        // ── Angular scenarios ────────────────────────────────────────────────────
        new("Angular_Coding",
            "implement an Angular counter component",
            "// CounterComponent: a count signal with increment() and decrement()",
            ScreenAnalysisMode.SolveCodingTask,
            Angular),

        new("Angular_Debug",
            "fix the bug in this RxJS subscription, it leaks",
            "ngOnInit() { this.svc.data$.subscribe(d => this.data = d); } // never unsubscribed",
            ScreenAnalysisMode.DebugError,
            Angular),

        new("Angular_Explain",
            "explain this code, what does OnPush do here",
            "@Component({ changeDetection: ChangeDetectionStrategy.OnPush })\n" +
            "export class ListComponent { @Input() items: Item[] = []; }",
            ScreenAnalysisMode.ExplainCode,
            Angular),

        // ── Negative control: chit-chat must not auto-select a mode ───────────────
        new("ChitChat_NoMode",
            "tell me about your experience with databases",
            string.Empty,
            ExpectedMode: null,
            CodeProfile.Empty),
    ];

    private static readonly Dictionary<string, InterviewScenario> ByName =
        Catalog.ToDictionary(s => s.Name);

    public static IEnumerable<object[]> AllScenarioNames =>
        Catalog.Select(s => new object[] { s.Name });

    public static IEnumerable<object[]> ModeScenarioNames =>
        Catalog.Where(s => s.ExpectedMode is not null).Select(s => new object[] { s.Name });

    public static IEnumerable<object[]> FollowUpScenarioNames =>
        Catalog.Where(s => s.FollowUpSpeech is not null).Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(AllScenarioNames))]
    public void Classify_SelectsExpectedMode(string name)
    {
        var s = ByName[name];
        ScreenModeClassifier.Classify(s.InterviewerSpeech).Should().Be(s.ExpectedMode);
    }
}

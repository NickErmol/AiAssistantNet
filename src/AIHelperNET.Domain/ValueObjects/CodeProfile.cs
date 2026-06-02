namespace AIHelperNET.Domain.ValueObjects;

/// <summary>
/// Value object that describes a developer's technical stack and expertise profile.
/// </summary>
/// <param name="ProgrammingLanguage">Primary programming language (e.g., "C#", "Python", "Java").</param>
/// <param name="BackendFramework">Backend framework or runtime (e.g., ".NET", "Spring Boot", "Node.js").</param>
/// <param name="FrontendFramework">Frontend framework (e.g., "React", "Angular", "Vue").</param>
/// <param name="Database">Database technology (e.g., "SQL Server", "PostgreSQL", "MongoDB").</param>
/// <param name="CloudDevOps">Cloud platform and DevOps tools (e.g., "Azure", "AWS", "Docker").</param>
/// <param name="Messaging">Messaging/event system (e.g., "RabbitMQ", "Kafka", "Azure Service Bus").</param>
/// <param name="ArchitectureStyle">Architecture style or pattern (e.g., "Microservices", "Event-Driven", "Monolith").</param>
/// <param name="TestingFramework">Testing framework or approach (e.g., "xUnit", "Jest", "Pytest").</param>
/// <param name="CustomNotes">Additional custom notes about the developer's profile.</param>
public sealed record CodeProfile(
    string? ProgrammingLanguage,
    string? BackendFramework,
    string? FrontendFramework,
    string? Database,
    string? CloudDevOps,
    string? Messaging,
    string? ArchitectureStyle,
    string? TestingFramework,
    string? CustomNotes)
{
    /// <summary>
    /// Gets an empty code profile where all properties are null.
    /// </summary>
    public static CodeProfile Empty =>
        new(null, null, null, null, null, null, null, null, null);
}

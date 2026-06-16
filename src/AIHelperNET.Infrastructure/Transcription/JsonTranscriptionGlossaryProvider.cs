using System.IO;
using System.Reflection;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Glossary;
using AIHelperNET.Infrastructure.Common;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>Loads the glossary from an optional data-root override or the embedded default,
/// and builds prompt suffixes via <see cref="GlossarySelector"/>.</summary>
public sealed class JsonTranscriptionGlossaryProvider : ITranscriptionGlossaryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public IReadOnlyList<GlossaryDomain> Domains { get; }

    /// <summary>Creates the provider, loading the override file if present else the embedded default.</summary>
    public JsonTranscriptionGlossaryProvider() => Domains = Load();

    /// <inheritdoc />
    public string BuildPromptSuffix(IReadOnlySet<string> enabledKeys, string recentContext, int wordBudget)
    {
        var terms = GlossarySelector.Select(Domains, enabledKeys, recentContext, wordBudget);
        return terms.Count == 0 ? string.Empty : string.Join(", ", terms) + ".";
    }

    private static List<GlossaryDomain> Load()
    {
        try
        {
            // AppPaths.Base is private; derive the data-root via the public SettingsFile property.
            var dataRoot = Path.GetDirectoryName(AppPaths.SettingsFile)!;
            var overridePath = Path.Combine(dataRoot, "glossary.json");
            if (File.Exists(overridePath))
                return Parse(File.ReadAllText(overridePath));

            var asm = Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames()
                .First(n => n.EndsWith("glossary.json", StringComparison.OrdinalIgnoreCase));
            using var stream = asm.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Glossary load failed; transcription will run without bias terms");
            return [];
        }
    }

    private static List<GlossaryDomain> Parse(string json)
    {
        var root = JsonSerializer.Deserialize<GlossaryFile>(json, JsonOptions);
        return root?.Domains ?? [];
    }

    private sealed record GlossaryFile(List<GlossaryDomain> Domains);
}

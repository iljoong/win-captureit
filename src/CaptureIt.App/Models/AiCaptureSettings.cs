namespace CaptureIt.App.Models;

/// <summary>
/// Settings for the "AI capture" feature group. The API key is deliberately not a
/// property here — it's stored/retrieved separately via Windows Credential Manager
/// (see <see cref="Capture.CredentialManagerService"/>) so it never round-trips
/// through the plaintext settings.json file.
/// </summary>
public sealed class AiCaptureSettings
{
    public AiCaptureMode Mode { get; set; } = AiCaptureMode.WindowsOcr;

    /// <summary>Base URL of the OpenAI-compatible endpoint (e.g. https://api.openai.com/v1).</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Model/deployment name to request, e.g. gpt-4o-mini.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// Named prompt templates the user can pick between for "Use AI to answer" mode
    /// (e.g. "Translate to Korean", "Describe"). Ignored by "Use AI capture" mode
    /// (which always extracts Markdown).
    /// </summary>
    public List<AiTaskTemplate> AiTasks { get; set; } = CreateDefaultAiTasks();

    /// <summary>Name of the <see cref="AiTaskTemplate"/> last selected for "Use AI to answer" mode.</summary>
    public string? SelectedAiTaskName { get; set; }

    public const string DefaultBaseUrl = "https://api.openai.com/v1";
    public const string DefaultModel = "gpt-4o-mini";

    /// <summary>The built-in AI Task templates offered out of the box on a fresh install.</summary>
    public static List<AiTaskTemplate> CreateDefaultAiTasks() => new()
    {
        new AiTaskTemplate
        {
            Name = "Translate to Korean",
            Prompt = "Translate the user's text into Korean as accurately and naturally as possible.",
        },
        new AiTaskTemplate
        {
            Name = "Describe",
            Prompt = "Analyze the captured image and describe as accurately and concisely as possible.",
        },
        new AiTaskTemplate
        {
            Name = "Explain",
            Prompt = "Extract the keywords or key trends from user's text. Explain new trends, background and provide insights\n" +
                     "Use the web search tool to get latest information.\n" +
                     "Add citations to the sources you used in your answer, and provide links to the relevant documentation.",
            UseWebSearch = true,
        },
        new AiTaskTemplate
        {
            Name = "Azure Question",
            Prompt = "Answer the user's question as accurately and concisely as possible.\n" +
                     "Use the available Microsoft Learn documentation search tools whenever the question relates to Microsoft or Azure products, technologies, or documentation.\n" +
                     "Add citations to the sources you used in your answer, and provide links to the relevant documentation.",
            McpServers = new List<McpServerEntry> { new() { Enabled = true, Url = "https://learn.microsoft.com/api/mcp" } },
        },
    };
}

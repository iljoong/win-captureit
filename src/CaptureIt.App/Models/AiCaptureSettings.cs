namespace CaptureIt.App.Models;

/// <summary>
/// Settings for the "AI capture" feature group. The API key is deliberately not a
/// property here — it's stored/retrieved separately via Windows Credential Manager
/// (see <see cref="Capture.CredentialManagerService"/>) so it never round-trips
/// through the plaintext settings.json file.
/// </summary>
public sealed class AiCaptureSettings
{
    public AiCaptureMode Mode { get; set; } = AiCaptureMode.Off;

    /// <summary>Base URL of the OpenAI-compatible endpoint (e.g. https://api.openai.com/v1).</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Model/deployment name to request, e.g. gpt-4o-mini.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// Text used as the task/question for "Use AI to answer" mode. Ignored by
    /// "Use AI capture" mode (which always extracts Markdown).
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Optional MCP tool servers made available to the model in "Use AI to answer" mode.</summary>
    public List<McpServerEntry> McpServers { get; set; } = new();

    public const string DefaultBaseUrl = "https://api.openai.com/v1";
    public const string DefaultModel = "gpt-4o-mini";
}

namespace CaptureIt.App.Models;

/// <summary>
/// One user-configurable AI task template selectable in "Use AI to answer" mode: a
/// named prompt plus optional web-search and MCP tool capabilities. Replaces the
/// former single <c>AiCaptureSettings.Prompt</c>/<c>McpServers</c> pair, letting the
/// user keep several task presets (e.g. "Translate to Korean", "Describe") and pick
/// one before capturing.
/// </summary>
public sealed class AiTaskTemplate
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Text used as the task/question for "Use AI to answer" mode.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>When true, a <c>HostedWebSearchTool</c> is added to the chat request's tools.</summary>
    public bool UseWebSearch { get; set; }

    /// <summary>Optional MCP tool servers made available to the model for this task.</summary>
    public List<McpServerEntry> McpServers { get; set; } = new();
}

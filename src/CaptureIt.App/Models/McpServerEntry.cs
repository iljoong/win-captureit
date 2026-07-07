namespace CaptureIt.App.Models;

/// <summary>
/// One configured MCP (Model Context Protocol) server the "Use AI to answer" mode
/// may connect to for tool-calling. <see cref="Command"/> is a full command line
/// (e.g. <c>npx -y @modelcontextprotocol/server-everything</c>) run over stdio.
/// </summary>
public sealed class McpServerEntry
{
    public bool Enabled { get; set; }

    public string Command { get; set; } = string.Empty;
}

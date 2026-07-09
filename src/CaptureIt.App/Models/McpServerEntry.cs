namespace CaptureIt.App.Models;

/// <summary>
/// One configured remote MCP (Model Context Protocol) server the "Use AI to answer"
/// mode may connect to for tool-calling. <see cref="Url"/> is the HTTP(S) endpoint of
/// a remote server (e.g. <c>https://example.com/mcp</c>); only remote servers are
/// supported — no local processes are launched.
/// </summary>
public sealed class McpServerEntry
{
    public bool Enabled { get; set; }

    /// <summary>HTTP(S) endpoint URL of the remote MCP server.</summary>
    public string Url { get; set; } = string.Empty;
}

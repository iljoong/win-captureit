using System.Drawing;
using System.Drawing.Imaging;
using System.ClientModel;
using System.IO;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using CaptureIt.App.Models;

namespace CaptureIt.App.Capture;

/// <summary>
/// Talks to the user-configured OpenAI-compatible endpoint for the "AI capture"
/// feature: either extracting a screenshot's content as Markdown ("Use AI capture")
/// or answering/acting on the configured Prompt using the screenshot as context
/// ("Use AI to answer"), optionally with tools from configured MCP servers.
/// </summary>
public static class AiCaptureService
{
    /// <summary>
    /// Runs the "Use AI capture" flow: extracts the screenshot's visible content as
    /// well-formatted Markdown. Blocks the calling thread until the response is
    /// received, matching the rest of the capture pipeline's synchronous style.
    /// </summary>
    public static string ExtractMarkdown(Bitmap bitmap, AppSettings settings)
        => Task.Run(() => ExtractMarkdownAsync(bitmap, settings)).GetAwaiter().GetResult();

    private static async Task<string> ExtractMarkdownAsync(Bitmap bitmap, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var client = BuildChatClient(settings);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                "You are an assistant that transcribes the visible content of a screenshot into " +
                "clean, well-structured Markdown (headings, lists, tables, and code blocks where " +
                "applicable). Respond with the Markdown only — no commentary, no surrounding code fence."),
            new(ChatRole.User, [ToImageContent(bitmap)]),
        };

        var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Runs the "Use AI to answer" flow: uses the configured Prompt text as the task
    /// or question, with the screenshot as context, optionally invoking tools from
    /// enabled MCP servers. Returns the model's answer text.
    /// </summary>
    public static async Task<string> AnswerAsync(Bitmap bitmap, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var client = BuildChatClient(settings);
        var mcpClients = new List<McpClient>();

        try
        {
            var tools = new List<AITool>();
            foreach (var server in settings.AiCapture.McpServers)
            {
                if (!server.Enabled || string.IsNullOrWhiteSpace(server.Command))
                {
                    continue;
                }

                try
                {
                    var transport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = server.Command,
                        Command = "cmd.exe",
                        Arguments = ["/c", server.Command],
                    });
                    var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
                    mcpClients.Add(mcpClient);
                    tools.AddRange(await mcpClient.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false));
                }
                catch
                {
                    // A misbehaving/unreachable MCP server shouldn't block answering
                    // the question at all; just skip its tools.
                }
            }

            var prompt = string.IsNullOrWhiteSpace(settings.AiCapture.Prompt)
                ? "Answer the question or complete the task shown in the image."
                : settings.AiCapture.Prompt;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "You are a helpful assistant. Use the attached screenshot as context for the user's request."),
                new(ChatRole.User, [new TextContent(prompt), ToImageContent(bitmap)]),
            };

            var options = tools.Count > 0 ? new ChatOptions { Tools = tools } : null;
            var response = await client.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            return response.Text ?? string.Empty;
        }
        finally
        {
            foreach (var mcpClient in mcpClients)
            {
                await mcpClient.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static DataContent ToImageContent(Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return new DataContent(memoryStream.ToArray(), "image/png");
    }

    private static IChatClient BuildChatClient(AppSettings settings)
    {
        var apiKey = CredentialManagerService.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No AI capture API key is configured. Add one in Settings > AI capture.");
        }

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(settings.AiCapture.BaseUrl))
        {
            clientOptions.Endpoint = new Uri(settings.AiCapture.BaseUrl);
        }

        var model = string.IsNullOrWhiteSpace(settings.AiCapture.Model)
            ? AiCaptureSettings.DefaultModel
            : settings.AiCapture.Model;

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        IChatClient baseClient = openAiClient.GetChatClient(model).AsIChatClient();

        // UseFunctionInvocation lets the client automatically execute AITools (e.g.
        // MCP client tools) the model asks to call and feed the results back in.
        return new ChatClientBuilder(baseClient).UseFunctionInvocation().Build();
    }
}

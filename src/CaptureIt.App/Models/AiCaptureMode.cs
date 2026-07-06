namespace CaptureIt.App.Models;

/// <summary>
/// The three AI Capture modes, selectable in the "AI capture" settings group.
/// </summary>
public enum AiCaptureMode
{
    /// <summary>AI processing is disabled; captures behave exactly as before (optionally with plain OCR).</summary>
    Off,

    /// <summary>Uses the configured OpenAI-compatible endpoint to extract the capture's content as formatted Markdown.</summary>
    Capture,

    /// <summary>Uses the configured OpenAI-compatible endpoint to answer/act on the "Prompt" text using the capture as context, and shows the answer in an overlay instead of saving.</summary>
    Answer
}

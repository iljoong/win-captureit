namespace CaptureIt.App.Models;

/// <summary>
/// The text-extraction engine, selectable in the "Text Extract Options" settings
/// group (only relevant when "Extract text from captured screenshots" is enabled).
/// </summary>
public enum AiCaptureMode
{
    /// <summary>Uses the built-in Windows OCR engine to extract plain text from the capture.</summary>
    WindowsOcr,

    /// <summary>Uses the configured OpenAI-compatible endpoint to extract the capture's content as formatted Markdown.</summary>
    Capture,

    /// <summary>Uses the configured OpenAI-compatible endpoint to answer/act on the "Prompt" text using the capture as context, and shows the answer in an overlay instead of saving.</summary>
    Answer
}

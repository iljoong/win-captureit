namespace CaptureIt.App.Capture;

/// <summary>
/// Places a capture result on the Windows clipboard, either as an image or as text.
/// Used when the "Save to clipboard" option is enabled instead of writing to disk.
/// Must be called on an STA thread (the app's UI thread), as clipboard access requires it.
/// </summary>
public static class ClipboardService
{
    /// <summary>Copies the captured bitmap to the clipboard as an image.</summary>
    public static void SetImage(System.Drawing.Bitmap bitmap)
        => System.Windows.Forms.Clipboard.SetImage(bitmap);

    /// <summary>Copies the extracted text to the clipboard.</summary>
    public static void SetText(string text)
        => System.Windows.Forms.Clipboard.SetText(text);
}

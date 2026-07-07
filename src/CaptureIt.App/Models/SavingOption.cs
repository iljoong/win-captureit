namespace CaptureIt.App.Models;

/// <summary>
/// Where a capture result is stored. Independent of the text-extraction method:
/// whichever engine (if any) runs, its output is routed to file and/or clipboard
/// according to this option. <see cref="Off"/> stores nothing at all.
/// </summary>
public enum SavingOption
{
    /// <summary>Save the capture to a file (image, plus a sidecar text file when extraction is enabled).</summary>
    SaveToFile,

    /// <summary>Place the capture on the Windows clipboard (image, or extracted text when extraction is enabled).</summary>
    SaveToClipboard,

    /// <summary>Both save to a file and place on the clipboard.</summary>
    SaveToFileAndClipboard,

    /// <summary>Store nothing. (An AI answer overlay may still be shown, but nothing is saved.)</summary>
    Off
}

/// <summary>Convenience predicates over <see cref="SavingOption"/>.</summary>
public static class SavingOptionExtensions
{
    public static bool SavesToFile(this SavingOption option)
        => option is SavingOption.SaveToFile or SavingOption.SaveToFileAndClipboard;

    public static bool SavesToClipboard(this SavingOption option)
        => option is SavingOption.SaveToClipboard or SavingOption.SaveToFileAndClipboard;
}

namespace CaptureIt.App.Models;

/// <summary>
/// The two capture modes the app supports. Persisted in settings so the global
/// hotkey can repeat whichever mode was used most recently.
/// </summary>
public enum CaptureMode
{
    Region,
    FullScreen
}

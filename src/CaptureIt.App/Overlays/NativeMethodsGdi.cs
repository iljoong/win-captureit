using System.Runtime.InteropServices;

namespace CaptureIt.App.Overlays;

/// <summary>GDI cleanup helper for HBITMAP handles created when converting to WPF ImageSources.</summary>
internal static class NativeMethodsGdi
{
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}

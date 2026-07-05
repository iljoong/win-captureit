using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CaptureIt.App.Capture;

/// <summary>
/// Extracts text from a captured screenshot using the OS-provided OCR engine
/// (Windows.Media.Ocr), scoped to whichever language(s) the user's Windows profile
/// (and installed OCR language packs) support.
/// </summary>
public static class OcrService
{
    /// <summary>
    /// Runs OCR on <paramref name="bitmap"/> and returns the recognized text, or null
    /// if no OCR engine/language pack is available on this machine, or no text was
    /// found. Blocks the calling thread until complete (the WinRT async operation is
    /// awaited on a background thread via <see cref="Task.Run(Func{Task})"/>), keeping
    /// this call in line with the rest of the capture pipeline's synchronous flow.
    /// </summary>
    public static string? ExtractText(System.Drawing.Bitmap bitmap)
        => Task.Run(() => ExtractTextAsync(bitmap)).GetAwaiter().GetResult();

    private static async Task<string?> ExtractTextAsync(System.Drawing.Bitmap bitmap)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            // No OCR language pack installed for any of the user's profile languages.
            return null;
        }

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        memoryStream.Position = 0;

        using var randomAccessStream = memoryStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var decodedBitmap = await decoder.GetSoftwareBitmapAsync();

        // OcrEngine.RecognizeAsync requires the input in Bgra8 format with
        // Premultiplied (or Ignore) alpha; the PNG-decoded bitmap isn't guaranteed to
        // already be in that format/alpha mode.
        using var recognizableBitmap = SoftwareBitmap.Convert(decodedBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(recognizableBitmap);
        return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text;
    }
}

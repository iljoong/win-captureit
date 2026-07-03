namespace CaptureIt.App.Models;

/// <summary>
/// A serializable rectangle in physical-pixel virtual-desktop coordinates. Used to
/// remember the last region capture so it can be pre-shown (and re-captured with
/// Enter) the next time the region overlay opens. Kept as a plain POCO of four ints
/// so it round-trips cleanly through System.Text.Json (unlike System.Drawing.Rectangle,
/// which exposes computed/read-only members that don't serialize predictably).
/// </summary>
public sealed class CaptureRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public System.Drawing.Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static CaptureRegion FromRectangle(System.Drawing.Rectangle r)
        => new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
}

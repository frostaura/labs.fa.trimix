namespace Trimix.Core.Connectors.Push;

/// <summary>
/// A rendered QR symbol: a square grid of dark/light modules WITHOUT the surrounding quiet zone (the
/// renderer adds its own margin). Pure data — no platform types — so the encode step stays unit-testable in
/// Core and the App's <c>QrCodeDrawable</c> only reads <see cref="IsDark"/>.
/// </summary>
/// <param name="Size">Module count per side (a QR symbol is always square; <c>17 + 4·version</c>).</param>
/// <param name="Modules">The module grid indexed <c>[y, x]</c> (row-major); <c>true</c> is a dark module.</param>
public sealed record QrMatrix(int Size, bool[,] Modules)
{
    /// <summary>True when the module at (<paramref name="x"/>,<paramref name="y"/>) is dark. Out-of-range → light.</summary>
    public bool IsDark(int x, int y) =>
        x >= 0 && y >= 0 && x < Size && y < Size && Modules[y, x];
}

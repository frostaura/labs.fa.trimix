using Trimix.Core.Connectors.Push;

namespace Trimix.App.Controls;

/// <summary>
/// Renders a <see cref="QrMatrix"/> (the diver-mediated MySSI export payload) as black modules on a white
/// card with the standard 4-module quiet zone. Deliberately fixed dark-on-light in <b>both</b> app themes:
/// QR scanners expect dark modules on a light background, so honoring the app's dark theme here would hurt
/// scannability. Mirrors <see cref="DepthChartDrawable"/>'s <see cref="IDrawable"/> shape; the page sets
/// <see cref="Matrix"/> and calls <c>Invalidate</c> when the viewmodel produces a new symbol.
/// </summary>
public sealed class QrCodeDrawable : IDrawable
{
    private const int QuietZoneModules = 4;

    public QrMatrix? Matrix { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // White backing (with quiet zone) — always, regardless of app theme, for reliable scanning.
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(dirtyRect);

        var matrix = Matrix;
        if (matrix is null || matrix.Size <= 0)
        {
            return;
        }

        var totalModules = matrix.Size + (QuietZoneModules * 2);
        var scale = Math.Min(dirtyRect.Width, dirtyRect.Height) / totalModules;
        if (scale <= 0f)
        {
            return;
        }

        var originX = dirtyRect.X + ((dirtyRect.Width - (scale * totalModules)) / 2f);
        var originY = dirtyRect.Y + ((dirtyRect.Height - (scale * totalModules)) / 2f);

        canvas.FillColor = Colors.Black;
        for (var y = 0; y < matrix.Size; y++)
        {
            for (var x = 0; x < matrix.Size; x++)
            {
                if (!matrix.IsDark(x, y))
                {
                    continue;
                }

                var px = originX + ((x + QuietZoneModules) * scale);
                var py = originY + ((y + QuietZoneModules) * scale);
                // A hair of overdraw closes sub-pixel seams between adjacent dark modules.
                canvas.FillRectangle(px, py, scale + 0.5f, scale + 0.5f);
            }
        }
    }
}

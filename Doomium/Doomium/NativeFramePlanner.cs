#nullable enable

namespace Doomium;

internal readonly record struct NativePaletteColor(byte Red, byte Green, byte Blue);

internal readonly record struct NativeTileRect(int Left, int Top, int Right, int Bottom, int Color);

internal sealed record NativeFramePlan(int Columns, int Rows, IReadOnlyList<NativeTileRect> Rectangles);

internal static class NativeFramePlanner
{
    public const int MaximumRectangles = 640;
    private static readonly (int Columns, int Rows)[] Sizes =
    [
        (80, 50),
        (64, 40),
        (48, 30),
        (40, 25),
        (32, 20)
    ];

    public static NativeFramePlan Build(byte[] rgba, int width, int height,
        IReadOnlyList<NativePaletteColor> palette, int maxRectangles = MaximumRectangles)
    {
        if (width <= 0 || height <= 0 || rgba.Length < (long)width * height * 4)
            throw new ArgumentException("Invalid RGBA frame.", nameof(rgba));
        if (palette.Count is < 1 or > 255)
            throw new ArgumentException("A palette of 1 to 255 colors is required.", nameof(palette));

        NativeFramePlan? last = null;
        foreach (var (columns, rows) in Sizes)
        {
            last = BuildAtSize(rgba, width, height, palette, columns, rows);
            if (last.Rectangles.Count <= maxRectangles) return last;
        }
        return last!;
    }

    private static NativeFramePlan BuildAtSize(byte[] rgba, int width, int height,
        IReadOnlyList<NativePaletteColor> palette, int columns, int rows)
    {
        var cells = new byte[columns * rows];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                var red = 0;
                var green = 0;
                var blue = 0;
                for (var dy = 1; dy <= 3; dy += 2)
                for (var dx = 1; dx <= 3; dx += 2)
                {
                    var sourceX = (int)((long)(4 * x + dx) * width / (4 * columns));
                    var sourceY = (int)((long)(4 * y + dy) * height / (4 * rows));
                    var offset = (sourceX * height + sourceY) * 4;
                    red += rgba[offset];
                    green += rgba[offset + 1];
                    blue += rgba[offset + 2];
                }
                cells[y * columns + x] = (byte)NearestColor(
                    red / 4, green / 4, blue / 4, palette);
            }
        }

        var rectangles = new List<NativeTileRect>();
        var active = new Dictionary<(int Left, int Right, int Color), NativeTileRect>();
        for (var y = 0; y < rows; y++)
        {
            var next = new Dictionary<(int Left, int Right, int Color), NativeTileRect>();
            for (var x = 0; x < columns;)
            {
                var color = cells[y * columns + x];
                var left = x;
                do { x++; }
                while (x < columns && cells[y * columns + x] == color);
                var key = (left, x, (int)color);
                if (active.Remove(key, out var previous))
                    next[key] = previous with { Bottom = y + 1 };
                else
                    next[key] = new NativeTileRect(left, y, x, y + 1, color);
            }
            rectangles.AddRange(active.Values);
            active = next;
        }
        rectangles.AddRange(active.Values);
        rectangles.Sort(static (a, b) =>
        {
            var byTop = a.Top.CompareTo(b.Top);
            if (byTop != 0) return byTop;
            var byLeft = a.Left.CompareTo(b.Left);
            if (byLeft != 0) return byLeft;
            return a.Color.CompareTo(b.Color);
        });
        return new NativeFramePlan(columns, rows, rectangles);
    }

    private static int NearestColor(int red, int green, int blue,
        IReadOnlyList<NativePaletteColor> palette)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            var dr = red - palette[i].Red;
            var dg = green - palette[i].Green;
            var db = blue - palette[i].Blue;
            var distance = 2 * dr * dr + 4 * dg * dg + db * db;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }
}

namespace Doomium;

internal static class DoomPixelConverter
{
    public static void ToBgra(byte[] rgba, byte[] bgra, int width, int height)
    {
        if (rgba.Length < width * height * 4 || bgra.Length < width * height * 4)
            throw new ArgumentException("Pixel buffers are too small.");
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = (x * height + y) * 4;
                var target = (y * width + x) * 4;
                bgra[target] = rgba[source + 2];
                bgra[target + 1] = rgba[source + 1];
                bgra[target + 2] = rgba[source];
                bgra[target + 3] = 255;
            }
        }
    }
}

namespace ManagedDoom;

internal static class ManagedDoomFileStreamCompat
{
    public static void ReadExactly(this FileStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = stream.Read(buffer, offset, buffer.Length - offset);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }
}

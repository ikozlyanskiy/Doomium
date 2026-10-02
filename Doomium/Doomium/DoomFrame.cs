#nullable enable
namespace Doomium;

internal readonly record struct DoomFrameBounds(
    int Left,
    int Bottom,
    int Right,
    int Top)
{
    public bool IsValid => Right > Left && Top > Bottom;
    public long Width => (long)Right - Left;
    public long Height => (long)Top - Bottom;
}

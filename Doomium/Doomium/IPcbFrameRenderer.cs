#nullable enable

namespace Doomium;

internal enum DoomiumRenderMode { Window, Fills, Regions }

internal interface IPcbFrameRenderer : IDisposable
{
    void Start(DoomFrameBounds bounds);
    void Render(byte[] rgba, int width, int height, DoomFrameBounds bounds);
}

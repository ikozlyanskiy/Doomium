#nullable enable
using ManagedDoom;
using ManagedDoom.Video;

namespace Doomium;

internal sealed class DoomiumVideo : IVideo
{
    private readonly Renderer _renderer;
    private readonly byte[] _frame;
    private readonly Action<byte[], int, int> _onFrame;

    public DoomiumVideo(
        Config config,
        GameContent content,
        Action<byte[], int, int> onFrame)
    {
        _renderer = new Renderer(config, content);
        _frame = new byte[_renderer.Width * _renderer.Height * 4];
        _onFrame = onFrame;
    }

    public void Render(Doom doom, Fixed frameFrac)
    {
        _renderer.Render(doom, _frame, frameFrac);
        _onFrame(_frame, _renderer.Width, _renderer.Height);
    }

    public void InitializeWipe() => _renderer.InitializeWipe();

    public bool HasFocus() => true;

    public int MaxWindowSize => _renderer.MaxWindowSize;

    public int WindowSize
    {
        get => _renderer.WindowSize;
        set => _renderer.WindowSize = value;
    }

    public bool DisplayMessage
    {
        get => _renderer.DisplayMessage;
        set => _renderer.DisplayMessage = value;
    }

    public int MaxGammaCorrectionLevel
        => _renderer.MaxGammaCorrectionLevel;

    public int GammaCorrectionLevel
    {
        get => _renderer.GammaCorrectionLevel;
        set => _renderer.GammaCorrectionLevel = value;
    }

    public int WipeBandCount => _renderer.WipeBandCount;
    public int WipeHeight => _renderer.WipeHeight;
}

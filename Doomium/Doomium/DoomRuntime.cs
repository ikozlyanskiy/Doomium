#nullable enable
using System.Diagnostics;
using ManagedDoom;
using ManagedDoom.Audio;

namespace Doomium;

internal sealed class DoomRuntime : IDisposable
{
    private const double TicMs = 1000.0 / 35.0;
    private const double RenderMs = 1000.0 / 30.0;
    private readonly Action<byte[], int, int> _onFrame;
    private readonly Stopwatch _clock = new();
    private GameContent? _content;
    private DoomiumVideo? _video;
    private DoomiumUserInput? _input;
    private Doom? _doom;
    private double _nextTic;
    private double _nextRender;

    public DoomRuntime(Action<byte[], int, int> onFrame) => _onFrame = onFrame;
    public bool IsRunning { get; private set; }
    public void KeyDown(int key) => _input?.KeyDown(key);
    public void KeyUp(int key) => _input?.KeyUp(key);
    public void ClearKeys() => _input?.Reset();
    public void AddMouseDelta(int dx) => _input?.AddMouseDelta(dx);
    public void SetMouseButton(System.Windows.Forms.MouseButtons button, bool down)
        => _input?.SetMouseButton(button, down);
    public void PostDoomKey(DoomKey key, bool down)
        => _doom?.PostEvent(new DoomEvent(down ? EventType.KeyDown : EventType.KeyUp, key));

    public void Start(string wadPath)
    {
        Stop();
        try
        {
            var args = new CommandLineArgs(["-iwad", wadPath, "-warp", "1", "-skill", "3", "-nosound", "-nomusic", "-nomouse"]);
            var config = new Config
            {
                video_screenwidth = 320,
                video_screenheight = 200,
                video_highresolution = false,
                video_fullscreen = false,
                video_fpsscale = 1,
                video_gamescreensize = 7,
                video_displaymessage = true,
                game_alwaysrun = false
            };
            _content = new GameContent(args);
            _input = new DoomiumUserInput();
            _video = new DoomiumVideo(config, _content, _onFrame);
            _doom = new Doom(args, config, _content, _video,
                NullSound.GetInstance(), NullMusic.GetInstance(), _input);
            _nextTic = 0;
            _nextRender = 0;
            _clock.Restart();
            IsRunning = true;
        }
        catch { Stop(); throw; }
    }

    public void Pump()
    {
        if (!IsRunning || _doom is null || _video is null) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        var steps = 0;
        while (now >= _nextTic && steps < 4)
        {
            if (_doom.Update() == UpdateResult.Completed) { Stop(); return; }
            _nextTic += TicMs;
            steps++;
        }
        if (steps == 4 && now - _nextTic > 4 * TicMs) _nextTic = now;
        if (now < _nextRender) return;
        _video.Render(_doom, Fixed.One);
        _nextRender = _clock.Elapsed.TotalMilliseconds + RenderMs;
    }

    public void Stop()
    {
        IsRunning = false;
        _clock.Stop();
        _doom = null;
        _video = null;
        _input = null;
        _content?.Dispose();
        _content = null;
    }

    public void Dispose() => Stop();
}

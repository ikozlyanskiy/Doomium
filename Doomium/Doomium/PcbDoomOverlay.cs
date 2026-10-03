#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ManagedDoom;
using PCB;

namespace Doomium;

internal sealed class PcbDoomOverlay : IDisposable
{
    private readonly IPCB_Board _board;
    private DoomFrameBounds _bounds;
    private readonly Func<DoomFrameBounds?> _liveBounds;
    private readonly Action _onFinished;
    private readonly GameSurface _surface;
    private readonly PcbNativeFrameRenderer? _nativeRenderer;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DoomRuntime _runtime;
    private readonly IntPtr _parent;
    private Rectangle _nativeViewport;
    private bool _disposed;
    private bool _mouseCaptured;
    private bool _middleDown;
    private bool _escapeDown;
    private bool _f1Down;
    private bool _tabDown;
    private int _ticks;

    public PcbDoomOverlay(IPCB_Board board, DoomFrameBounds bounds,
        Func<DoomFrameBounds?> liveBounds, Action onFinished, bool nativeRenderer = false)
    {
        if (!bounds.IsValid) throw new ArgumentException("Rectangle has no area.", nameof(bounds));
        _board = board;
        _bounds = bounds;
        _liveBounds = liveBounds;
        _onFinished = onFinished;
        _parent = new IntPtr(board.GetState_Window());
        if (_parent == IntPtr.Zero || !IsWindow(_parent))
            throw new InvalidOperationException("PCB document window is unavailable.");
        _nativeRenderer = nativeRenderer ? new PcbNativeFrameRenderer(board) : null;
        _surface = new GameSurface(this);
        _runtime = new DoomRuntime(_nativeRenderer is null
            ? _surface.SetFrame
            : (rgba, width, height) => _nativeRenderer.Render(rgba, width, height, _bounds));
        _timer = new System.Windows.Forms.Timer { Interval = 15 };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => !_disposed && _runtime.IsRunning;

    public void Start(string wad)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PcbDoomOverlay));
        _runtime.Start(wad);
        _nativeRenderer?.Start(_bounds);
        if (_nativeRenderer is null)
        {
            _surface.CreateControl();
            SetParent(_surface.Handle, _parent);
            if (GetParent(_surface.Handle) != _parent)
                throw new InvalidOperationException($"Cannot attach Doom display to PCB window (Win32 {Marshal.GetLastWin32Error()}).");
        }
        UpdatePosition();
        if (_nativeRenderer is null)
        {
            _surface.Show();
            _surface.Focus();
        }
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        try
        {
            if (!IsWindow(_parent) || !DoomiumServerModule.IsCurrentBoard(_board))
            {
                _onFinished();
                return;
            }
            DoomFrameBounds? nextBounds;
            try { nextBounds = _liveBounds(); }
            catch (System.Runtime.InteropServices.COMException) { nextBounds = null; }
            if (nextBounds is not { IsValid: true })
            {
                _onFinished();
                return;
            }
            _bounds = nextBounds.Value;
            UpdatePosition();
            if (!PollInput()) return;
            _runtime.Pump();
            if (!_runtime.IsRunning) _onFinished();
        }
        catch (Exception ex)
        {
            DoomiumTrace.Write("Game loop failed: " + ex);
            _onFinished();
            MessageBox.Show(ex.ToString(), "Doomium", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdatePosition()
    {
        var view = _board.Internal_GetState_MainGraphicalView() as IPCB_GraphicalView
            ?? throw new InvalidOperationException("PCB graphical view is unavailable.");
        var x1 = 0; var y1 = 0; var x2 = 0; var y2 = 0;
        view.PCBLocationToClient(_bounds.Left, _bounds.Top, out x1, out y1);
        view.PCBLocationToClient(_bounds.Right, _bounds.Bottom, out x2, out y2);
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);
        if (width < 2 || height < 2)
        {
            HideViewport();
            return;
        }
        if (!GetClientRect(_parent, out var client))
            throw new InvalidOperationException("PCB viewport size is unavailable.");
        var interior = Rectangle.FromLTRB(left + 4, top + 4,
            left + width - 4, top + height - 4);
        if (interior.Width < 2 || interior.Height < 2)
        {
            HideViewport();
            return;
        }
        var visible = Rectangle.Intersect(
            interior,
            new Rectangle(0, 0, client.Right, client.Bottom));
        if (visible.Width < 2 || visible.Height < 2)
        {
            HideViewport();
            return;
        }
        if (_nativeRenderer is not null)
        {
            _nativeViewport = visible;
            if (_mouseCaptured) Cursor.Clip = GetGameScreenRectangle();
            return;
        }
        _surface.SetViewport(visible, new RectangleF(
            (visible.Left - left) * 320f / width,
            (visible.Top - top) * 200f / height,
            visible.Width * 320f / width,
            visible.Height * 200f / height));
        if (_mouseCaptured)
            Cursor.Clip = _surface.RectangleToScreen(_surface.ClientRectangle);
        if (!_surface.Visible) _surface.Show();
        if (++_ticks % 30 == 0) _surface.BringToFront();
    }

    private void HideViewport()
    {
        if (_nativeRenderer is null) _surface.Hide();
        else
        {
            _nativeViewport = Rectangle.Empty;
            ReleaseMouseCapture();
        }
    }

    private Rectangle GetGameScreenRectangle()
    {
        if (_nativeRenderer is null)
            return _surface.RectangleToScreen(_surface.ClientRectangle);
        if (_nativeViewport.IsEmpty) return Rectangle.Empty;
        var origin = new NativePoint { X = _nativeViewport.Left, Y = _nativeViewport.Top };
        if (!ClientToScreen(_parent, ref origin))
            throw new InvalidOperationException($"Cannot locate PCB viewport (Win32 {Marshal.GetLastWin32Error()}).");
        return new Rectangle(origin.X, origin.Y, _nativeViewport.Width, _nativeViewport.Height);
    }

    public void KeyDown(Keys key)
    {
        if (key == Keys.Escape) { _onFinished(); return; }
        _runtime.KeyDown((int)key);
    }
    public void KeyUp(Keys key) => _runtime.KeyUp((int)key);
    public void ClearKeys() => _runtime.ClearKeys();
    public void SpecialKey(Keys key, bool down)
    {
        if (key == Keys.F1)
        {
            if (down && !_f1Down) _surface.ToggleHelp();
            _f1Down = down;
        }
        else if (key == Keys.Tab)
        {
            if (down != _tabDown) _runtime.PostDoomKey(DoomKey.Tab, down);
            _tabDown = down;
        }
    }
    public void AddMouseDelta(int dx) => _runtime.AddMouseDelta(dx);
    public void SetMouseButton(MouseButtons button, bool down)
        => _runtime.SetMouseButton(button, down);

    public void ToggleMouseCapture()
    {
        if (_mouseCaptured)
        {
            ReleaseMouseCapture();
            return;
        }
        var screen = GetGameScreenRectangle();
        if (screen.IsEmpty) return;
        _mouseCaptured = true;
        if (_nativeRenderer is null) _surface.Focus();
        Cursor.Hide();
        Cursor.Clip = screen;
        CenterMouse();
    }

    public void ReleaseMouseCapture()
    {
        if (!_mouseCaptured) return;
        _mouseCaptured = false;
        Cursor.Clip = Rectangle.Empty;
        Cursor.Show();
        _runtime.SetMouseButton(MouseButtons.Left, false);
        _runtime.SetMouseButton(MouseButtons.Right, false);
    }

    public void TrackMouse()
    {
        if (!_mouseCaptured) return;
        var screen = GetGameScreenRectangle();
        var center = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
        var dx = Cursor.Position.X - center.X;
        if (dx != 0) _runtime.AddMouseDelta(dx);
        if (Cursor.Position != center) Cursor.Position = center;
    }

    private void CenterMouse()
    {
        var screen = GetGameScreenRectangle();
        Cursor.Position = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
    }

    public void MiddleClick()
    {
        _middleDown = true;
        ToggleMouseCapture();
    }

    private bool PollInput()
    {
        if (!IsAltiumForeground())
        {
            ReleaseMouseCapture();
            _runtime.ClearKeys();
            _middleDown = false;
            _escapeDown = false;
            SpecialKey(Keys.F1, false);
            SpecialKey(Keys.Tab, false);
            return true;
        }
        var middle = IsPressed(0x04);
        if (middle && !_middleDown &&
            (_mouseCaptured || GetGameScreenRectangle().Contains(Cursor.Position)))
            ToggleMouseCapture();
        _middleDown = middle;
        if (!_mouseCaptured) return true;

        foreach (var key in TrackedKeys)
        {
            if (IsPressed((int)key)) _runtime.KeyDown((int)key);
            else _runtime.KeyUp((int)key);
        }
        SpecialKey(Keys.F1, IsPressed((int)Keys.F1));
        SpecialKey(Keys.Tab, IsPressed((int)Keys.Tab));
        var escape = IsPressed(0x1B);
        if (escape && !_escapeDown)
        {
            _onFinished();
            return false;
        }
        _escapeDown = escape;
        _runtime.SetMouseButton(MouseButtons.Left, IsPressed(0x01));
        _runtime.SetMouseButton(MouseButtons.Right, IsPressed(0x02));
        TrackMouse();
        return true;
    }

    private static readonly Keys[] TrackedKeys =
    [
        Keys.W, Keys.A, Keys.S, Keys.D, Keys.Q, Keys.E,
        Keys.Up, Keys.Down, Keys.Left, Keys.Right,
        Keys.ShiftKey, Keys.ControlKey, Keys.Space,
        Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7,
        Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4,
        Keys.NumPad5, Keys.NumPad6, Keys.NumPad7
    ];

    private static bool IsPressed(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsAltiumForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseMouseCapture();
        _timer.Stop();
        _timer.Dispose();
        _runtime.Dispose();
        _nativeRenderer?.Dispose();
        _surface.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    private sealed class GameSurface : Control
    {
        private readonly PcbDoomOverlay _owner;
        private readonly Bitmap _bitmap = new(320, 200, PixelFormat.Format32bppArgb);
        private readonly byte[] _bgra = new byte[320 * 200 * 4];
        private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private RectangleF _source = new(0, 0, 320, 200);
        private readonly Stopwatch _helpClock = Stopwatch.StartNew();
        private bool? _helpOverride;

        public GameSurface(PcbDoomOverlay owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque |
                ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.Black;
            Size = new Size(320, 200);
        }

        public void SetFrame(byte[] rgba, int width, int height)
        {
            if (width != 320 || height != 200 || rgba.Length < _bgra.Length)
                throw new ArgumentException("Expected a 320x200 RGBA Doom frame.");
            DoomPixelConverter.ToBgra(rgba, _bgra, width, height);
            var bits = _bitmap.LockBits(new Rectangle(0, 0, 320, 200),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(_bgra, 0, bits.Scan0, _bgra.Length); }
            finally { _bitmap.UnlockBits(bits); }
            _frameCount++;
            if (_fpsClock.ElapsedMilliseconds >= 1000)
            {
                _fps = (int)Math.Round(_frameCount * 1000.0 / _fpsClock.ElapsedMilliseconds);
                _frameCount = 0;
                _fpsClock.Restart();
            }
            Invalidate();
        }

        public void SetViewport(Rectangle bounds, RectangleF source)
        {
            if (Bounds != bounds || _source != source)
            {
                Bounds = bounds;
                _source = source;
                Invalidate();
            }
        }

        public void ToggleHelp()
        {
            _helpOverride = !(_helpOverride ?? (_helpClock.ElapsedMilliseconds < 8000));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_bitmap, ClientRectangle,
                _source.X, _source.Y, _source.Width, _source.Height, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
            e.Graphics.FillRectangle(brush, 2, 2, 64, 19);
            e.Graphics.DrawString($"{_fps} FPS", Font, Brushes.White, 5, 4);
            e.Graphics.DrawString("F1: controls", Font, Brushes.White, 70, 4);
            if ((_helpOverride ?? (_helpClock.ElapsedMilliseconds < 8000)) &&
                Width > 280 && Height > 95)
            {
                var help = "W/S move  A/D strafe  Q/E or arrows turn  Shift run\n" +
                    "Ctrl/LMB fire  Space/RMB use  1-7 weapons  Tab map\n" +
                    "Middle click: mouse capture  Esc: stop  F1: help";
                var helpBounds = new Rectangle(4, Height - 60, Width - 8, 56);
                e.Graphics.FillRectangle(brush, helpBounds);
                e.Graphics.DrawString(help, Font, Brushes.White, helpBounds);
            }
            using var pen = new Pen(Color.Yellow, 2);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                _owner.MiddleClick();
                return;
            }
            Focus();
            base.OnMouseDown(e);
        }
        protected override bool IsInputKey(Keys keyData) => true;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_owner._mouseCaptured && (e.KeyCode == Keys.F1 || e.KeyCode == Keys.Tab))
                _owner.SpecialKey(e.KeyCode, true);
            else _owner.KeyDown(e.KeyCode);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (!_owner._mouseCaptured && (e.KeyCode == Keys.F1 || e.KeyCode == Keys.Tab))
                _owner.SpecialKey(e.KeyCode, false);
            else _owner.KeyUp(e.KeyCode);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        protected override void OnLostFocus(EventArgs e)
        {
            if (!_owner._mouseCaptured) _owner.ClearKeys();
            if (!_owner._mouseCaptured)
            {
                _owner.SpecialKey(Keys.F1, false);
                _owner.SpecialKey(Keys.Tab, false);
            }
            base.OnLostFocus(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _bitmap.Dispose();
            base.Dispose(disposing);
        }
    }
}

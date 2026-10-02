#nullable enable
using ManagedDoom;
using ManagedDoom.UserInput;
using System.Windows.Forms;

namespace Doomium;

internal sealed class DoomiumUserInput : IUserInput
{
    private readonly HashSet<Keys> _pressed = new();
    private int _turnHeld;
    private int _mouseDeltaX;
    private bool _mouseFire;
    private bool _mouseUse;
    public void KeyDown(int key) => _pressed.Add((Keys)key);
    public void KeyUp(int key) => _pressed.Remove((Keys)key);
    public void AddMouseDelta(int dx) => _mouseDeltaX = Math.Clamp(_mouseDeltaX + dx, -500, 500);
    public void SetMouseButton(MouseButtons button, bool down)
    {
        if (button == MouseButtons.Left) _mouseFire = down;
        if (button == MouseButtons.Right) _mouseUse = down;
    }
    private bool IsDown(Keys key) => _pressed.Contains(key);

    public void BuildTicCmd(TicCmd cmd)
    {
        cmd.Clear();
        var speed = IsDown(Keys.ShiftKey) ? 1 : 0;
        var forward = 0;
        var side = 0;
        var turnLeft = IsDown(Keys.Left) || IsDown(Keys.Q);
        var turnRight = IsDown(Keys.Right) || IsDown(Keys.E);
        _turnHeld = turnLeft || turnRight ? _turnHeld + 1 : 0;
        var turnSpeed = _turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;
        if (turnRight) cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
        if (turnLeft) cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
        cmd.AngleTurn -= (short)(_mouseDeltaX * 40);
        _mouseDeltaX = 0;
        if (IsDown(Keys.W) || IsDown(Keys.Up)) forward += PlayerBehavior.ForwardMove[speed];
        if (IsDown(Keys.S) || IsDown(Keys.Down)) forward -= PlayerBehavior.ForwardMove[speed];
        if (IsDown(Keys.A)) side -= PlayerBehavior.SideMove[speed];
        if (IsDown(Keys.D)) side += PlayerBehavior.SideMove[speed];
        if (IsDown(Keys.ControlKey) || _mouseFire) cmd.Buttons |= TicCmdButtons.Attack;
        if (IsDown(Keys.Space) || _mouseUse) cmd.Buttons |= TicCmdButtons.Use;
        for (var i = 0; i < 7; i++)
        {
            if (!IsDown(Keys.D1 + i) && !IsDown(Keys.NumPad1 + i)) continue;
            cmd.Buttons |= TicCmdButtons.Change;
            cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
            break;
        }
        cmd.ForwardMove = (sbyte)Math.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
        cmd.SideMove = (sbyte)Math.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
    }

    public void Reset()
    {
        _pressed.Clear();
        _turnHeld = 0;
        _mouseDeltaX = 0;
        _mouseFire = false;
        _mouseUse = false;
    }
    public void GrabMouse() { }
    public void ReleaseMouse() { }
    public int MaxMouseSensitivity => 0;
    public int MouseSensitivity { get => 0; set { } }
}

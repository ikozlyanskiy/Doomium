#nullable enable
using System.Diagnostics;
using DXP;
using PCB;

namespace Doomium;

internal sealed class PcbNativeFrameRenderer : IDisposable
{
    private const int PaletteSize = 12;
    private readonly IPCB_Board _board;
    private readonly IPCB_ServerInterface _pcb;
    private readonly List<LayerChoice> _layers = [];
    private readonly List<IPCB_Fill> _fills = [];
    private readonly List<FillState> _states = [];
    private readonly List<bool> _visible = [];
    private readonly Stopwatch _statistics = Stopwatch.StartNew();
    private long _renderMilliseconds;
    private int _frames;
    private bool _started;
    private bool _disposed;

    public PcbNativeFrameRenderer(IPCB_Board board)
    {
        _board = board;
        var client = GlobalVars.Client
            ?? throw new InvalidOperationException("Altium client is unavailable.");
        _pcb = client.GetServerModuleByName("PCB") as IPCB_ServerInterface
            ?? throw new InvalidOperationException("PCB server is unavailable.");
    }

    public void Start(DoomFrameBounds bounds)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PcbNativeFrameRenderer));
        if (_started) return;

        var candidates = ReadMechanicalLayers();
        if (candidates.Count < 2)
            throw new InvalidOperationException(
                "Native renderer needs at least two mechanical layers with different colors.");
        _layers.AddRange(ChoosePalette(candidates));
        try
        {
            foreach (var choice in _layers)
            {
                if (!choice.WasVisible)
                    _board.SetState_LayerIsDisplayed(choice.Layer, true);
            }
            _pcb.PreProcess();
            try
            {
                var placeholder = new FillState(
                    bounds.Left, bounds.Bottom, bounds.Left + 1, bounds.Bottom + 1, 0);
                for (var i = 0; i < NativeFramePlanner.MaximumRectangles; i++)
                {
                    var fill = _pcb.Internal_PCBObjectFactory(
                        (int)TObjectId.eFillObject,
                        (int)TDimensionKind.eNoDimension,
                        (int)TObjectCreationMode.eCreate_Default) as IPCB_Fill
                        ?? throw new InvalidOperationException("Altium did not create a PCB fill.");
                    SetFill(fill, placeholder);
                    fill.SetState_Selected(false);
                    _board.AddPCBObject(fill);
                    _fills.Add(fill);
                    _states.Add(placeholder);
                    _visible.Add(false);
                    _board.DispatchMessage(_board, null, 2, fill);
                    _board.HidePCBObject(fill);
                }
            }
            finally { _pcb.PostProcess(); }
            _board.ViewManager_FullUpdate();
            _started = true;
            DoomiumTrace.Write($"Native PCB renderer: {_layers.Count} mechanical-layer colors, {_fills.Count} fills allocated.");
        }
        catch
        {
            RestoreLayerVisibility();
            _layers.Clear();
            throw;
        }
    }

    public void Render(byte[] rgba, int width, int height, DoomFrameBounds bounds)
    {
        if (!_started || _disposed) return;
        var clock = Stopwatch.StartNew();
        var colors = _layers.Select(layer => layer.Color).ToArray();
        var plan = NativeFramePlanner.Build(rgba, width, height, colors);
        var shown = 0;

        foreach (var rectangle in plan.Rectangles)
        {
            var placement = MapRectangle(rectangle, plan, bounds);
            if (placement.X2 <= placement.X1 || placement.Y2 <= placement.Y1) continue;
            if (shown >= _fills.Count)
                throw new InvalidOperationException("Native frame exceeds the allocated PCB fill pool.");
            var fill = _fills[shown];
            var changed = false;
            if (!_visible[shown])
            {
                _board.ShowPCBObject(fill);
                _visible[shown] = true;
                changed = true;
            }
            if (_states[shown] != placement)
            {
                _board.ViewManager_GraphicallyInvalidatePrimitive(fill);
                SetFill(fill, placement);
                _states[shown] = placement;
                changed = true;
            }
            if (changed) _board.ViewManager_GraphicallyInvalidatePrimitive(fill);
            shown++;
        }

        for (var i = shown; i < _fills.Count; i++)
        {
            if (!_visible[i]) continue;
            _board.HidePCBObject(_fills[i]);
            _board.ViewManager_GraphicallyInvalidatePrimitive(_fills[i]);
            _visible[i] = false;
        }

        _board.Navigate_RedrawChangedObjectsInBoard();

        clock.Stop();
        _frames++;
        _renderMilliseconds += clock.ElapsedMilliseconds;
        if (_statistics.ElapsedMilliseconds >= 1000)
        {
            DoomiumTrace.Write($"Native PCB renderer: {_frames * 1000.0 / _statistics.ElapsedMilliseconds:F1} FPS, " +
                $"{shown} visible / {_fills.Count} pooled fills, {plan.Columns}x{plan.Rows} cells, " +
                $"{_renderMilliseconds / Math.Max(1, _frames)} ms/frame.");
            _statistics.Restart();
            _frames = 0;
            _renderMilliseconds = 0;
        }
    }

    private void SetFill(IPCB_Fill fill, FillState state)
    {
        fill.SetState_V7Layer(_layers[state.Color].Layer);
        fill.SetState_X1Location(state.X1);
        fill.SetState_Y1Location(state.Y1);
        fill.SetState_X2Location(state.X2);
        fill.SetState_Y2Location(state.Y2);
        fill.SetState_Rotation(0);
    }

    private static FillState MapRectangle(NativeTileRect rectangle,
        NativeFramePlan plan, DoomFrameBounds bounds)
    {
        var x1 = (int)(bounds.Left + rectangle.Left * bounds.Width / plan.Columns);
        var x2 = (int)(bounds.Left + rectangle.Right * bounds.Width / plan.Columns);
        var y2 = (int)(bounds.Top - rectangle.Top * bounds.Height / plan.Rows);
        var y1 = (int)(bounds.Top - rectangle.Bottom * bounds.Height / plan.Rows);
        return new FillState(x1, y1, x2, y2, rectangle.Color);
    }

    private List<LayerChoice> ReadMechanicalLayers()
    {
        var choices = new List<LayerChoice>();
        var iterator = (IPCB_LayerIterator)_board.Internal_MechanicalLayerIterator();
        if (iterator is null || !iterator.First()) return choices;
        do
        {
            var layer = iterator.Internal_Layer();
            if (layer is null) continue;
            var raw = _board.GetState_ViewConfigColor2D(layer);
            var color = new NativePaletteColor(
                (byte)(raw & 0xFF),
                (byte)((raw >> 8) & 0xFF),
                (byte)((raw >> 16) & 0xFF));
            if (choices.Any(choice => choice.Color == color)) continue;
            choices.Add(new LayerChoice(layer, color, _board.GetState_LayerIsDisplayed(layer)));
        }
        while (iterator.Next());
        return choices;
    }

    private static List<LayerChoice> ChoosePalette(List<LayerChoice> candidates)
    {
        var selected = new List<LayerChoice>();
        selected.Add(candidates.MinBy(candidate =>
            candidate.Color.Red + candidate.Color.Green + candidate.Color.Blue)!);
        while (selected.Count < Math.Min(PaletteSize, candidates.Count))
        {
            var next = candidates.Where(candidate => !selected.Contains(candidate))
                .MaxBy(candidate => selected.Min(chosen =>
                    ColorDistance(candidate.Color, chosen.Color)));
            if (next is null) break;
            selected.Add(next);
        }
        return selected;
    }

    private static int ColorDistance(NativePaletteColor a, NativePaletteColor b)
    {
        var red = a.Red - b.Red;
        var green = a.Green - b.Green;
        var blue = a.Blue - b.Blue;
        return 2 * red * red + 4 * green * green + blue * blue;
    }

    private void RestoreLayerVisibility()
    {
        foreach (var layer in _layers)
        {
            try { _board.SetState_LayerIsDisplayed(layer.Layer, layer.WasVisible); }
            catch (Exception ex) { DoomiumTrace.Write("Layer visibility restore failed: " + ex); }
        }
        try { _board.ViewManager_FullUpdate(); }
        catch (Exception ex) { DoomiumTrace.Write("Native view refresh failed: " + ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (var fill in _fills)
            {
                try { _board.RemovePCBObject(fill); }
                catch (Exception ex) { DoomiumTrace.Write("Native fill removal failed: " + ex); }
            }
        }
        catch (Exception ex) { DoomiumTrace.Write("Native renderer cleanup failed: " + ex); }
        finally
        {
            _fills.Clear();
            _states.Clear();
            _visible.Clear();
            RestoreLayerVisibility();
        }
    }

    private sealed record LayerChoice(IV7_Layer Layer, NativePaletteColor Color, bool WasVisible);

    private readonly record struct FillState(int X1, int Y1, int X2, int Y2, int Color);
}

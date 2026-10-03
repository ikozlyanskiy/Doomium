#nullable enable
using PCB;

namespace Doomium;

internal sealed class PcbMechanicalPalette : IDisposable
{
    private readonly IPCB_Board _board;
    private readonly IPCB_MasterLayerStack2 _stack;
    private readonly IPCB_SystemOptions _options;
    private readonly List<LayerState> _states = [];
    private bool _disposed;

    public IReadOnlyList<(IV7_Layer Layer, NativePaletteColor Color)> Colors { get; private set; }
        = Array.Empty<(IV7_Layer, NativePaletteColor)>();

    public PcbMechanicalPalette(IPCB_Board board, IPCB_ServerInterface pcb)
    {
        _board = board;
        _stack = board.Internal_GetState_LayerStack() as IPCB_MasterLayerStack2
            ?? throw new InvalidOperationException("PCB layer stack is unavailable.");
        _options = pcb.Internal_GetState_SystemOptions() as IPCB_SystemOptions
            ?? throw new InvalidOperationException("PCB layer colors are unavailable.");
        var utils = pcb.Internal_LayerUtils() as IPCB_LayerUtils
            ?? throw new InvalidOperationException("PCB layer utilities are unavailable.");
        var legacyStack = board.Internal_GetState_LayerStack_V7() as IPCB_LayerStack_V7;

        try
        {
            for (uint index = 1; index <= 16 && _states.Count < Palette.Length; index++)
            {
                var id = utils.Internal_MechanicalLayer(index);
                if (id is null) continue;
                if (TryLayerCall(() => (bool?)board.GetState_LayerIsUsed(id), index,
                    "used check") is not false)
                    continue;

                var layer = TryLayerCall(() => legacyStack?.Internal_GetState_LayerObject_V7(id)
                    as IPCB_MechanicalLayer, index, "legacy lookup");
                layer ??= TryLayerCall(() => _stack.Internal_GetMechanicalLayer((int)index)
                    as IPCB_MechanicalLayer, index, "modern lookup");
                var created = false;
                if (layer is null)
                {
                    layer = TryLayerCall(() => _stack.Internal_CreateLayer(id)
                        as IPCB_MechanicalLayer, index, "create layer");
                    created = layer is not null;
                }
                if (layer is null) continue;

                var actualId = layer.Internal_V7_LayerID();
                var original = new LayerState(layer, actualId,
                    layer.GetState_LayerName(), layer.GetState_MechLayerEnabled(),
                    board.GetState_LayerIsDisplayed(actualId),
                    _options.GetState_LayerColors_V7(actualId), created);
                _states.Add(original);
                var entry = Palette[_states.Count - 1];
                layer.SetState_LayerName($"Doomium {index:00} {entry.Name}");
                layer.SetState_MechLayerEnabled(true);
                _options.SetState_LayerColors_V7(actualId, ToColorRef(entry.Color));
                board.SetState_LayerIsDisplayed(actualId, true);
                DoomiumTrace.Write($"Palette layer {index}: id={actualId.GetID()}, " +
                    $"created={created}, color=0x{board.GetState_ViewConfigColor2D(actualId):X6}.");
            }

            if (_states.Count < 2)
                throw new InvalidOperationException(
                    "Altium did not expose two free mechanical layer slots. " +
                    "Check Doomium log for layer details.");
            Colors = _states.Select((state, i) => (state.Id, Palette[i].Color)).ToArray();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static uint ToColorRef(NativePaletteColor color)
        => (uint)(color.Red | color.Green << 8 | color.Blue << 16);

    private static T? TryLayerCall<T>(Func<T?> call, uint index, string operation)
    {
        try { return call(); }
        catch (Exception ex)
        {
            DoomiumTrace.Write($"Palette mechanical {index} {operation} failed: {ex}");
            return default;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var i = _states.Count - 1; i >= 0; i--)
        {
            var state = _states[i];
            try
            {
                _options.SetState_LayerColors_V7(state.Id, state.Color);
                _board.SetState_LayerIsDisplayed(state.Id, state.Visible);
                if (state.Created)
                {
                    if (!_stack.RemoveLayer(state.Layer))
                        DoomiumTrace.Write("Created palette layer removal failed: " + state.Name);
                }
                else
                {
                    state.Layer.SetState_LayerName(state.Name);
                    state.Layer.SetState_MechLayerEnabled(state.Enabled);
                }
            }
            catch (Exception ex) { DoomiumTrace.Write("Palette layer restoration failed: " + ex); }
        }
        _states.Clear();
        Colors = Array.Empty<(IV7_Layer, NativePaletteColor)>();
    }

    private sealed record LayerState(IPCB_MechanicalLayer Layer, IV7_Layer Id,
        string Name, bool Enabled, bool Visible, uint Color, bool Created);

    private static readonly (string Name, NativePaletteColor Color)[] Palette =
    [
        ("Black", new(9, 9, 10)), ("Charcoal", new(39, 40, 40)),
        ("Stone", new(82, 84, 82)), ("Silver", new(163, 164, 156)),
        ("Ivory", new(224, 216, 190)), ("Umber", new(49, 34, 25)),
        ("Brown", new(97, 66, 41)), ("Tan", new(154, 119, 76)),
        ("Sand", new(210, 176, 126)), ("Maroon", new(86, 25, 22)),
        ("Red", new(177, 42, 31)), ("Orange", new(223, 111, 44)),
        ("Gold", new(224, 191, 70)), ("Olive", new(84, 91, 42)),
        ("Green", new(108, 143, 69)), ("Blue", new(59, 98, 144))
    ];
}

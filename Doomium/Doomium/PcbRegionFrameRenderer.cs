#nullable enable
using System.Diagnostics;
using DXP;
using PCB;

namespace Doomium;

internal sealed class PcbRegionFrameRenderer : IPcbFrameRenderer
{
    private const int ContourBudget = 5000;
    private readonly IPCB_Board _board;
    private readonly IPCB_ServerInterface _pcb;
    private PcbMechanicalPalette? _palette;
    private readonly List<IPCB_Region> _regions = [];
    private readonly List<bool> _visible = [];
    private readonly Stopwatch _statistics = Stopwatch.StartNew();
    private IPCB_Fill? _sourceFill;
    private bool _sourceWasHidden;
    private int _frames;
    private long _renderMilliseconds;
    private bool _started;
    private bool _disposed;

    public PcbRegionFrameRenderer(IPCB_Board board)
    {
        _board = board;
        var client = GlobalVars.Client
            ?? throw new InvalidOperationException("Altium client is unavailable.");
        _pcb = client.GetServerModuleByName("PCB") as IPCB_ServerInterface
            ?? throw new InvalidOperationException("PCB server is unavailable.");
    }

    public void Start(DoomFrameBounds bounds)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PcbRegionFrameRenderer));
        if (_started) return;
        try
        {
            if (_board.GetState_SelectecObjectCount() == 1 &&
                _board.Internal_GetState_SelectecObject(0) is IPCB_Fill sourceFill)
            {
                _sourceFill = sourceFill;
                _sourceWasHidden = sourceFill.IsHidden();
                if (!_sourceWasHidden) _board.HidePCBObject(sourceFill);
            }

            _palette = new PcbMechanicalPalette(_board, _pcb);
            _pcb.PreProcess();
            try
            {
                for (var i = 0; i < _palette.Colors.Count; i++)
                {
                    var v7 = _palette.Colors[i].Layer;

                    var region = _pcb.Internal_PCBObjectFactory(
                        (int)TObjectId.eRegionObject,
                        (int)TDimensionKind.eNoDimension,
                        (int)TObjectCreationMode.eCreate_Default) as IPCB_Region
                        ?? throw new InvalidOperationException("Altium did not create a PCB region.");
                    region.SetState_V7Layer(v7);
                    region.SetState_Kind((int)TRegionKind.eRegionKind_Copper);
                    region.SetState_Selected(false);
                    region.SetGeometricPolygon(RectanglePolygon(
                        bounds.Left, bounds.Bottom, bounds.Left + 1, bounds.Bottom + 1));
                    _board.AddPCBObject(region);
                    _regions.Add(region);
                    _visible.Add(false);
                    _board.DispatchMessage(_board, null, 2, region);
                    _board.HidePCBObject(region);
                }
            }
            finally { _pcb.PostProcess(); }
            _board.ViewManager_FullUpdate();
            _started = true;
            DoomiumTrace.Write($"Native region renderer: {_palette.Colors.Count} mechanical layers prepared.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Render(byte[] rgba, int width, int height, DoomFrameBounds bounds)
    {
        if (!_started || _disposed) return;
        var clock = Stopwatch.StartNew();
        var colors = _palette!.Colors.Select(entry => entry.Color).ToArray();
        var plan = NativeFramePlanner.Build(rgba, width, height, colors, ContourBudget);
        var polygons = new IPCB_GeometricPolygon?[colors.Length];
        var counts = new int[colors.Length];

        foreach (var rect in plan.Rectangles)
        {
            var x1 = (int)(bounds.Left + rect.Left * bounds.Width / plan.Columns);
            var x2 = (int)(bounds.Left + rect.Right * bounds.Width / plan.Columns);
            var y2 = (int)(bounds.Top - rect.Top * bounds.Height / plan.Rows);
            var y1 = (int)(bounds.Top - rect.Bottom * bounds.Height / plan.Rows);
            if (x2 <= x1 || y2 <= y1) continue;
            var polygon = polygons[rect.Color] ??= NewPolygon();
            AddRectangle(polygon, x1, y1, x2, y2);
            counts[rect.Color]++;
        }

        for (var i = 0; i < _regions.Count; i++)
        {
            var region = _regions[i];
            var polygon = polygons[i];
            if (polygon is null)
            {
                if (!_visible[i]) continue;
                _board.HidePCBObject(region);
                _board.ViewManager_GraphicallyInvalidatePrimitive(region);
                _visible[i] = false;
                continue;
            }
            if (_visible[i]) _board.ViewManager_GraphicallyInvalidatePrimitive(region);
            region.BeginModify();
            try { region.SetGeometricPolygon(polygon); }
            finally { region.EndModify(); }
            if (!_visible[i])
            {
                _board.ShowPCBObject(region);
                _visible[i] = true;
            }
            _board.ViewManager_GraphicallyInvalidatePrimitive(region);
        }
        _board.ViewManager_FullUpdate();

        _frames++;
        _renderMilliseconds += clock.ElapsedMilliseconds;
        if (_statistics.ElapsedMilliseconds >= 1000)
        {
            DoomiumTrace.Write($"Native regions: {_frames * 1000.0 / _statistics.ElapsedMilliseconds:F1} FPS, " +
                $"{plan.Rectangles.Count} contours, {counts.Count(count => count > 0)} colors, " +
                $"{plan.Columns}x{plan.Rows} cells, {_renderMilliseconds / Math.Max(1, _frames)} ms/frame.");
            _frames = 0;
            _renderMilliseconds = 0;
            _statistics.Restart();
        }
    }

    private IPCB_GeometricPolygon RectanglePolygon(int x1, int y1, int x2, int y2)
    {
        var polygon = NewPolygon();
        AddRectangle(polygon, x1, y1, x2, y2);
        return polygon;
    }

    private IPCB_GeometricPolygon NewPolygon()
        => _pcb.Internal_PCBGeometricPolygonFactory() as IPCB_GeometricPolygon
           ?? throw new InvalidOperationException("Altium did not create a geometric polygon.");

    private void AddRectangle(IPCB_GeometricPolygon polygon, int x1, int y1, int x2, int y2)
    {
        var contour = _pcb.Internal_PCBContourFactory() as IPCB_Contour
            ?? throw new InvalidOperationException("Altium did not create a polygon contour.");
        contour.AddPoint(x1, y1);
        contour.AddPoint(x2, y1);
        contour.AddPoint(x2, y2);
        contour.AddPoint(x1, y2);
        polygon.Internal_AddContourIsHole(contour, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var region in _regions)
        {
            try { _board.RemovePCBObject(region); }
            catch (Exception ex) { DoomiumTrace.Write("Native region removal failed: " + ex); }
        }
        _regions.Clear();
        _visible.Clear();
        _palette?.Dispose();
        _palette = null;
        if (_sourceFill is not null)
        {
            try
            {
                if (!_sourceWasHidden) _board.ShowPCBObject(_sourceFill);
            }
            catch (Exception ex) { DoomiumTrace.Write("Region source rectangle restore failed: " + ex); }
            _sourceFill = null;
        }
        try { _board.ViewManager_FullUpdate(); }
        catch (Exception ex) { DoomiumTrace.Write("Region view refresh failed: " + ex); }
    }

}

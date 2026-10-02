#nullable enable
using PCB;

namespace Doomium;

internal static class PcbRectangleSelection
{
    public static bool TryGetBounds(
        IPCB_Board board,
        out DoomFrameBounds bounds,
        out Func<DoomFrameBounds?> liveBounds,
        out string error)
    {
        bounds = default;
        liveBounds = () => null;
        error = "Select one PCB Rectangle (Place > Rectangle) and run Tools > Convert > Rect to Doomium.";
        var selected = new List<object>();
        for (var i = 0; i < board.GetState_SelectecObjectCount(); i++)
        {
            var obj = board.Internal_GetState_SelectecObject(i);
            if (obj is not null) selected.Add(obj);
        }
        if (selected.Count == 1 && selected[0] is IPCB_SharedUnion)
        {
            var allTracks = EnumerateTracks(board);
            var marked = new List<IPCB_Track>();
            foreach (var track in allTracks)
            {
                try { if (track.GetState_Selected()) marked.Add(track); }
                catch (System.Runtime.InteropServices.COMException) { }
            }
            DoomiumTrace.Write($"SharedUnion selected track count={marked.Count}, board track count={allTracks.Count}");
            if (marked.Count != 4)
                marked = FindRectangleAtCursor(board, allTracks);
            if (marked.Count != 4)
                error = "This AD25 build does not expose the selected Rectangle's members to the SDK. Use Tools > Convert > Explode Rectangle to Free Primitives, select the four resulting tracks, then retry.";
            selected.Clear();
            selected.AddRange(marked);
        }
        if (selected.Count == 1 && selected[0] is IPCB_Fill fill)
        {
            if (Math.Abs(fill.GetState_Rotation()) > 0.001)
            {
                error = "Rotated fills are not supported. Select an axis-aligned PCB Rectangle.";
                return false;
            }
            bounds = new DoomFrameBounds(
                Math.Min(fill.GetState_X1Location(), fill.GetState_X2Location()),
                Math.Min(fill.GetState_Y1Location(), fill.GetState_Y2Location()),
                Math.Max(fill.GetState_X1Location(), fill.GetState_X2Location()),
                Math.Max(fill.GetState_Y1Location(), fill.GetState_Y2Location()));
            if (!bounds.IsValid) return false;
            liveBounds = () => fill.GetState_InBoard()
                ? BoundsOfFill(fill) : null;
            return true;
        }

        var tracks = selected.OfType<IPCB_Track>().ToList();
        if (tracks.Count == 1 && selected.Count == 1 && tracks[0].GetState_UnionIndex() != 0)
        {
            var index = tracks[0].GetState_UnionIndex();
            tracks = FindTracksInUnion(board, index);
        }

        if (tracks.Count != 4 || selected.Any(x => x is not IPCB_Track))
            return false;

        var left = tracks.Min(t => Math.Min(t.GetState_X1(), t.GetState_X2()));
        var right = tracks.Max(t => Math.Max(t.GetState_X1(), t.GetState_X2()));
        var bottom = tracks.Min(t => Math.Min(t.GetState_Y1(), t.GetState_Y2()));
        var top = tracks.Max(t => Math.Max(t.GetState_Y1(), t.GetState_Y2()));
        bounds = new DoomFrameBounds(left, bottom, right, top);
        if (!bounds.IsValid) return false;

        var edges = new HashSet<string>();
        foreach (var track in tracks)
        {
            var x1 = track.GetState_X1();
            var x2 = track.GetState_X2();
            var y1 = track.GetState_Y1();
            var y2 = track.GetState_Y2();
            if (x1 == x2 && x1 == left && Math.Min(y1, y2) == bottom && Math.Max(y1, y2) == top) edges.Add("L");
            else if (x1 == x2 && x1 == right && Math.Min(y1, y2) == bottom && Math.Max(y1, y2) == top) edges.Add("R");
            else if (y1 == y2 && y1 == bottom && Math.Min(x1, x2) == left && Math.Max(x1, x2) == right) edges.Add("B");
            else if (y1 == y2 && y1 == top && Math.Min(x1, x2) == left && Math.Max(x1, x2) == right) edges.Add("T");
        }
        if (edges.Count != 4)
        {
            error = "The selected smart union is not an axis-aligned rectangle.";
            return false;
        }
        liveBounds = () => tracks.All(t => t.GetState_InBoard())
            ? BoundsOfTracks(tracks) : null;
        return true;
    }

    private static DoomFrameBounds BoundsOfFill(IPCB_Fill fill) => new(
        Math.Min(fill.GetState_X1Location(), fill.GetState_X2Location()),
        Math.Min(fill.GetState_Y1Location(), fill.GetState_Y2Location()),
        Math.Max(fill.GetState_X1Location(), fill.GetState_X2Location()),
        Math.Max(fill.GetState_Y1Location(), fill.GetState_Y2Location()));

    private static DoomFrameBounds BoundsOfTracks(List<IPCB_Track> tracks) => new(
        tracks.Min(t => Math.Min(t.GetState_X1(), t.GetState_X2())),
        tracks.Min(t => Math.Min(t.GetState_Y1(), t.GetState_Y2())),
        tracks.Max(t => Math.Max(t.GetState_X1(), t.GetState_X2())),
        tracks.Max(t => Math.Max(t.GetState_Y1(), t.GetState_Y2())));

    private static List<IPCB_Track> FindTracksInUnion(IPCB_Board board, int unionIndex)
    {
        return EnumerateTracks(board)
            .Where(track => track.GetState_UnionIndex() == unionIndex)
            .ToList();
    }

    private static List<IPCB_Track> EnumerateTracks(IPCB_Board board)
    {
        var tracks = new List<IPCB_Track>();
        object iterator = board.Internal_BoardIterator_Create();
        try
        {
            var scan = (IPCB_BoardIterator)iterator;
            scan.SetState_FilterAll();
            for (var obj = scan.Internal_FirstPCBObject(); obj is not null; obj = scan.Internal_NextPCBObject())
            {
                if (obj is IPCB_Track track)
                    tracks.Add(track);
            }
        }
        finally { board.BoardIterator_Destroy(ref iterator); }
        return tracks;
    }

    private static List<IPCB_Track> FindRectangleAtCursor(IPCB_Board board, List<IPCB_Track> allTracks)
    {
        var x = board.GetState_XCursor();
        var y = board.GetState_YCursor();
        DoomiumTrace.Write($"PCB cursor=({x},{y})");
        var candidates = new List<(List<IPCB_Track> Tracks, long Area)>();
        foreach (var group in allTracks
            .GroupBy(track => track.GetState_UnionIndex())
            .Where(group => group.Key > 0 && group.Count() == 4))
        {
            var tracks = group.ToList();
            var left = tracks.Min(t => Math.Min(t.GetState_X1(), t.GetState_X2()));
            var right = tracks.Max(t => Math.Max(t.GetState_X1(), t.GetState_X2()));
            var bottom = tracks.Min(t => Math.Min(t.GetState_Y1(), t.GetState_Y2()));
            var top = tracks.Max(t => Math.Max(t.GetState_Y1(), t.GetState_Y2()));
            if (x >= left && x <= right && y >= bottom && y <= top)
                candidates.Add((tracks, (long)(right - left) * (top - bottom)));
        }
        DoomiumTrace.Write($"Rectangle candidates at cursor={candidates.Count}");
        return candidates.OrderBy(candidate => candidate.Area)
            .Select(candidate => candidate.Tracks)
            .FirstOrDefault() ?? new List<IPCB_Track>();
    }
}

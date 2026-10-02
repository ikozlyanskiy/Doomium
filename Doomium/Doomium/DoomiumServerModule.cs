#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using DXP;
using PCB;

namespace Doomium;

[ClassInterface(ClassInterfaceType.None)]
public sealed class DoomiumServerModule : ServerModule
{
    private static PcbDoomOverlay? _overlay;
    private static string? _wadPath;

    public DoomiumServerModule() : base("Doomium") { }

    protected override void InitializeCommands()
    {
        DoomiumTrace.Write("InitializeCommands entered");
        ((CommandLauncher)CommandLauncher).RegisterCommand(
            "RectToDoomium",
            (CommandProc)((RT_ClientServerInterface.IServerDocumentView view, StringBuilder parameters) =>
            {
                DoomiumTrace.Write("RectToDoomium command invoked");
                try { RectToDoomium(); }
                catch (Exception ex)
                {
                    DoomiumTrace.Write("RectToDoomium failed: " + ex);
                    _overlay?.Dispose();
                    _overlay = null;
                    MessageBox.Show(ex.ToString(), "Doomium", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }), null);
        DoomiumTrace.Write("InitializeCommands complete");
    }

    private static void RectToDoomium()
    {
        if (_overlay is { IsRunning: true })
        {
            _overlay.Dispose();
            _overlay = null;
            return;
        }
        if (!TryGetPcb(out var board))
        {
            DoomiumTrace.Write("No active PCB board");
            MessageBox.Show("Open a .PcbDoc first.", "Doomium", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PcbRectangleSelection.TryGetBounds(board, out var bounds, out var liveBounds, out var error))
        {
            DoomiumTrace.Write("Rectangle selection rejected: " + error);
            MessageBox.Show(error, "Doomium", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var wad = ResolveWad();
        if (wad is null) return;
        DoomiumTrace.Write($"Starting game in {bounds} with WAD {Path.GetFileName(wad)}");
        _overlay?.Dispose();
        _overlay = new PcbDoomOverlay(board, bounds, liveBounds, () =>
        {
            _overlay?.Dispose();
            _overlay = null;
        });
        _overlay.Start(wad);
        DoomiumTrace.Write("Game overlay started");
    }

    private static string? ResolveWad()
    {
        if (!string.IsNullOrWhiteSpace(_wadPath) && File.Exists(_wadPath)) return _wadPath;
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        foreach (var name in new[] { "DOOM2.WAD", "DOOM.WAD", "DOOM1.WAD", "FREEDOOM2.WAD", "FREEDOOM1.WAD" })
        {
            var candidate = Path.Combine(assemblyDir, name);
            if (!File.Exists(candidate)) continue;
            _wadPath = candidate;
            return candidate;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a DOOM IWAD",
            Filter = "DOOM WAD (*.wad)|*.wad|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK) return null;
        _wadPath = dialog.FileName;
        return _wadPath;
    }

    private static bool TryGetPcb(out IPCB_Board board)
    {
        board = null!;
        var client = GlobalVars.Client;
        if (client is null) return false;
        var pcb = client.GetServerModuleByName("PCB") as IPCB_ServerInterface;
        if (pcb is null)
        {
            client.StartServer("PCB");
            pcb = client.GetServerModuleByName("PCB") as IPCB_ServerInterface;
        }
        if (pcb is null) return false;
        board = pcb.GetCurrentPCBBoard();
        return board is not null;
    }

    internal static bool IsCurrentBoard(IPCB_Board board)
        => TryGetPcb(out var current) && current.GetState_Window() == board.GetState_Window();

    protected override RT_ClientServerInterface.IServerDocument? NewDocumentInstance(string kind, string fileName) => null;
}

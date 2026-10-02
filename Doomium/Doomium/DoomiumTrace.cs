namespace Doomium;

internal static class DoomiumTrace
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Doomium", "doomium.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [PID {Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

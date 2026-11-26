using System.IO;

namespace WinSolar.App.Services;

/// <summary>A small text log in %AppData%\Win Solar\log.txt so failures are never silent.</summary>
public static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Win Solar", "log.txt");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                {
                    var lines = File.ReadAllLines(Path);
                    File.WriteAllLines(Path, lines[(lines.Length / 2)..]);
                }

                File.AppendAllText(Path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }
}

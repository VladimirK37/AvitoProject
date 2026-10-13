namespace AvitoMonitor.Configuration;

/// <summary>Пишет подробности ошибок в errors.log, чтобы в консоли показывать только короткое сообщение.</summary>
public static class ErrorLog
{
    private static string? _file;

    public static void Init(string file) => _file = file;

    public static void Write(Exception exception, string context)
    {
        if (_file is null)
            return;

        try
        {
            File.AppendAllText(_file,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Журнал ошибок не должен сам ронять программу
        }
    }
}

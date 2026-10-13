using System.ComponentModel;
using System.Diagnostics;

namespace AvitoMonitor.Desktop;

/// <summary>Открыть ссылку в браузере, файл — в связанной программе (Excel), папку — в проводнике.</summary>
public static class SystemShell
{
    public static bool Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

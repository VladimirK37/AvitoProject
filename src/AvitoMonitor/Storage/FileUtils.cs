using System.Text;

namespace AvitoMonitor.Storage;

public static class FileUtils
{
    /// <summary>
    /// Записывает файл «атомарно»: сначала во временный файл, потом подменяет.
    /// Если программу закроют посреди записи, старый файл останется целым.
    /// </summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}

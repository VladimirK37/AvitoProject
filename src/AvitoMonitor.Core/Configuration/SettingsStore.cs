using System.Text.Json;
using AvitoMonitor.Storage;

namespace AvitoMonitor.Configuration;

/// <summary>Читает и сохраняет settings.json.</summary>
public sealed class SettingsStore
{
    private readonly string _file;

    public SettingsStore(string file) => _file = file;

    public AppSettings Load()
    {
        if (!File.Exists(_file))
        {
            // Первый запуск: создаём файл с настройками по умолчанию, чтобы его было видно и можно было поправить руками
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_file);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options) ?? new AppSettings();
    }

    public void Save(AppSettings settings) =>
        FileUtils.WriteAllTextAtomic(_file, JsonSerializer.Serialize(settings, JsonDefaults.Options));
}

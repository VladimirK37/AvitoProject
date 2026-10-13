using AvitoMonitor.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AvitoMonitor.Persistence;

/// <summary>
/// Нужен только команде <c>dotnet ef migrations add ...</c>: подсказывает, как создать AvitoDbContext
/// без запуска всей программы. Для создания миграции сама база не нужна.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AvitoDbContext>
{
    public AvitoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AvitoDbContext>()
            .UseNpgsql(new AppSettings().DatabaseConnectionString)
            .Options;

        return new AvitoDbContext(options);
    }
}

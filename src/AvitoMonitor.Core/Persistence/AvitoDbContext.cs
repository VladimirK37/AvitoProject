using Microsoft.EntityFrameworkCore;

namespace AvitoMonitor.Persistence;

/// <summary>
/// База данных PostgreSQL через EF Core. Структура таблиц меняется миграциями
/// (папка Migrations), при запуске программа применяет их сама.
///
/// Новая миграция после изменения сущностей:
/// <code>dotnet ef migrations add ИмяМиграции --project src/AvitoMonitor.Core --output-dir Persistence/Migrations</code>
/// </summary>
public sealed class AvitoDbContext(DbContextOptions<AvitoDbContext> options) : DbContext(options)
{
    public DbSet<SearchEntity> Searches => Set<SearchEntity>();
    public DbSet<SearchSourceEntity> SearchSources => Set<SearchSourceEntity>();
    public DbSet<ListingEntity> Listings => Set<ListingEntity>();
    public DbSet<SearchQueryEntity> SearchQueries => Set<SearchQueryEntity>();
    public DbSet<ReportEntity> Reports => Set<ReportEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SearchEntity>(entity =>
        {
            entity.ToTable("searches");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).IsRequired();
            entity.HasMany(s => s.Sources)
                .WithOne()
                .HasForeignKey(source => source.SearchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SearchSourceEntity>(entity =>
        {
            entity.ToTable("search_sources");
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => new { s.SearchId, s.Position });
        });

        model.Entity<ListingEntity>(entity =>
        {
            entity.ToTable("listings");
            entity.HasKey(l => new { l.SearchId, l.AvitoId });
            entity.Property(l => l.Price).HasPrecision(14, 2);
            entity.HasOne<SearchEntity>()
                .WithMany()
                .HasForeignKey(l => l.SearchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SearchQueryEntity>(entity =>
        {
            entity.ToTable("search_queries");
            entity.HasKey(q => q.Id);
            entity.HasIndex(q => q.CreatedAt);
            entity.HasIndex(q => q.Text);
        });

        model.Entity<ReportEntity>(entity =>
        {
            entity.ToTable("reports");
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.CreatedAt);
            // Удалили поиск — отчёты остаются (у них просто пропадает ссылка на поиск)
            entity.HasOne<SearchEntity>()
                .WithMany()
                .HasForeignKey(r => r.SearchId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

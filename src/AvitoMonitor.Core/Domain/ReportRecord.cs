namespace AvitoMonitor.Domain;

/// <summary>
/// Сохранённый Excel-отчёт: создаётся после каждого сбора и при нажатии «Открыть в Excel».
/// Сам файл лежит в папке reports, а запись о нём — в базе данных.
/// </summary>
public sealed record ReportRecord(
    long Id,
    // Поиск мог быть удалён — тогда SearchId = null, а название осталось
    string? SearchId,
    string SearchName,
    // Что искали: «airpods 4» · Санкт-Петербург
    string Description,
    DateTime CreatedAt,
    int ListingsCount,
    // Копии и другие модели, не учтённые в рыночной цене
    int ExcludedCount,
    // Коротко о рынке: «Без уточнения — 10 390 ₽; С шумоподавлением (ANC) — 13 390 ₽»
    string Summary,
    string FilePath);

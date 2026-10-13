using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;
using ClosedXML.Excel;

namespace AvitoMonitor.Export;

/// <summary>
/// Excel-отчёт по поиску. Листы:
/// <list type="bullet">
/// <item>«Сводка» — медиана, минимум и максимум по группам, кто продаёт;</item>
/// <item>«Все объявления» — каждое объявление с отклонением от рынка и пометками;</item>
/// <item>«Частники» / «Собственники» — только частные продавцы;</item>
/// <item>«Магазины и перекупы» / «Агентства и агенты» — бизнес, в том числе скрытый.</item>
/// </list>
/// Файл открывается в Excel, LibreOffice или Google Таблицах.
/// </summary>
public sealed class ExcelReportBuilder
{
    private const string MoneyFormat = "#,##0 \"₽\"";
    private const string PercentFormat = "+0%;-0%;0%";

    public void Build(SearchAnalysis analysis, IReadOnlyList<ListingAssessment> rows, string path)
    {
        var search = analysis.Search;
        var isRealEstate = search.Category == SearchCategory.RealEstate;
        var perSquareMeter = analysis.Market.PerSquareMeter;

        using var workbook = new XLWorkbook();

        AddSummary(workbook.AddWorksheet("Сводка"), analysis, rows);
        AddListings(workbook.AddWorksheet("Все объявления"), rows, perSquareMeter);
        AddListings(workbook.AddWorksheet(isRealEstate ? "Собственники" : "Частники"),
            rows.Where(row => !row.Seller.IsBusiness).ToList(), perSquareMeter);
        AddListings(workbook.AddWorksheet(isRealEstate ? "Агентства и агенты" : "Магазины и перекупы"),
            rows.Where(row => row.Seller.IsBusiness).ToList(), perSquareMeter);

        workbook.SaveAs(path);
    }

    private static void AddSummary(IXLWorksheet sheet, SearchAnalysis analysis, IReadOnlyList<ListingAssessment> rows)
    {
        var search = analysis.Search;
        var market = analysis.Market;
        var row = 1;

        void Info(string name, XLCellValue value)
        {
            sheet.Cell(row, 1).Value = name;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = value;
            row++;
        }

        Info("Поиск", search.Name);
        Info("Категория", search.Category.ToRussian());
        Info("Объявлений собрано", rows.Count);
        Info("Сравниваем", market.PerSquareMeter ? "цену за квадратный метр" : "цену");
        Info("Отчёт сформирован", DateTime.Now.ToString("dd.MM.yyyy HH:mm"));

        foreach (var source in search.Sources)
        {
            sheet.Cell(row, 1).Value = $"Ссылка «{source.Label}»";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = source.Url;
            sheet.Cell(row, 2).SetHyperlink(new XLHyperlink(source.Url));
            row++;
        }

        // Таблица по группам
        row++;
        var unit = market.PerSquareMeter ? ", ₽ за м²" : ", ₽";
        WriteHeaderRow(sheet, row, ["Группа", "Объявлений", "Медиана" + unit, "Минимум" + unit, "Максимум" + unit]);
        var firstGroupRow = ++row;
        foreach (var group in GroupKeys.OrderByGroup(market.Groups.Values, g => g.Key))
        {
            sheet.Cell(row, 1).Value = group.Key;
            sheet.Cell(row, 2).Value = group.Count;
            SetMoney(sheet.Cell(row, 3), group.Median);
            SetMoney(sheet.Cell(row, 4), group.Min);
            SetMoney(sheet.Cell(row, 5), group.Max);
            row++;
        }

        if (row > firstGroupRow)
            sheet.Range(firstGroupRow - 1, 1, row - 1, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        // Кто продаёт
        row++;
        WriteHeaderRow(sheet, row, ["Тип продавца", "Объявлений"]);
        row++;
        foreach (var group in rows.GroupBy(r => r.Seller.Title).OrderByDescending(g => g.Count()))
        {
            sheet.Cell(row, 1).Value = group.Key;
            sheet.Cell(row, 2).Value = group.Count();
            row++;
        }

        sheet.Column(1).Width = 42;
        sheet.Column(2).Width = 20;
        sheet.Columns(3, 5).Width = 20;
    }

    private static void AddListings(IXLWorksheet sheet, IReadOnlyList<ListingAssessment> rows, bool perSquareMeter)
    {
        var columns = new List<(string Header, double Width)>
        {
            ("Группа", 18), ("Название", 45), ("Цена", 14),
        };
        if (perSquareMeter)
            columns.Add(("Цена за м²", 14));
        columns.AddRange(
        [
            ("Медиана группы", 16), ("Отклонение от рынка", 12), ("Пометки", 50), ("Продавец", 24),
            ("Тип продавца", 28), ("Почему так решили", 45), ("Адрес", 35), ("Опубликовано", 18),
            ("Впервые замечено", 17), ("Ссылка", 12),
        ]);

        WriteHeaderRow(sheet, 1, columns.Select(c => c.Header).ToList());
        for (var i = 0; i < columns.Count; i++)
            sheet.Column(i + 1).Width = columns[i].Width;

        var row = 2;
        foreach (var a in rows)
        {
            var col = 1;
            sheet.Cell(row, col++).Value = a.GroupKey;
            sheet.Cell(row, col++).Value = a.Listing.Title;
            SetMoney(sheet.Cell(row, col++), a.Listing.Price);
            if (perSquareMeter)
                SetMoney(sheet.Cell(row, col++), a.ComparedValue);
            SetMoney(sheet.Cell(row, col++), a.Reference?.Median);

            var deviationCell = sheet.Cell(row, col++);
            if (a.DeviationPercent is { } deviation)
            {
                deviationCell.Value = deviation / 100.0;
                deviationCell.Style.NumberFormat.Format = PercentFormat;
                if (deviation <= -20)
                    deviationCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                else if (deviation >= 20)
                    deviationCell.Style.Fill.BackgroundColor = XLColor.LightPink;
            }

            var flagsCell = sheet.Cell(row, col++);
            flagsCell.Value = string.Join("; ", a.Flags.Select(f => f.Text));
            if (a.Flags.Any(f => f.Level == FlagLevel.Danger))
                flagsCell.Style.Font.FontColor = XLColor.Red;

            sheet.Cell(row, col++).Value = a.Listing.SellerName ?? "";
            sheet.Cell(row, col++).Value = a.Seller.Title;
            sheet.Cell(row, col++).Value = string.Join("; ", a.Seller.Reasons);
            sheet.Cell(row, col++).Value = a.Listing.Address ?? "";
            sheet.Cell(row, col++).Value = a.Listing.PublishedText ?? "";

            var seenCell = sheet.Cell(row, col++);
            seenCell.Value = a.Listing.FirstSeenAt;
            seenCell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm";

            var linkCell = sheet.Cell(row, col);
            linkCell.Value = "Открыть";
            linkCell.SetHyperlink(new XLHyperlink(a.Listing.Url));

            row++;
        }

        sheet.Range(1, 1, Math.Max(row - 1, 1), columns.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteHeaderRow(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Alignment.WrapText = true;
        }
    }

    private static void SetMoney(IXLCell cell, decimal? value)
    {
        if (value is null)
            return;

        cell.Value = (double)value.Value;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }
}

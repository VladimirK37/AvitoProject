using Spectre.Console;

namespace AvitoMonitor.ConsoleUi;

/// <summary>Простые пронумерованные меню: «введите номер пункта».</summary>
public static class Menu
{
    /// <summary>Показывает пункты и возвращает индекс выбранного (с нуля).</summary>
    public static int Choose(string title, IReadOnlyList<string> options)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold underline]{Markup.Escape(title)}[/]");
        for (var i = 0; i < options.Count; i++)
            AnsiConsole.MarkupLine($"  [yellow]{i + 1}[/]. {Markup.Escape(options[i])}");

        return Ask.Number("Введите номер:", 1, options.Count) - 1;
    }
}

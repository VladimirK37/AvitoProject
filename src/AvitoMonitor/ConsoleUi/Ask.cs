using System.Text;
using Spectre.Console;

namespace AvitoMonitor.ConsoleUi;

/// <summary>
/// Вопросы пользователю на обычном Console.ReadLine.
///
/// Spectre.Console используем только для красивого вывода, а ввод читаем сами:
/// так программа работает в любой консоли — в Windows Terminal, во встроенной консоли Rider
/// и даже когда ввод перенаправлен из файла.
/// </summary>
public static class Ask
{
    /// <summary>Текстовый ответ. Enter — значение по умолчанию (если задано).</summary>
    /// <param name="validate">Проверка: вернуть текст ошибки или null, если всё хорошо.</param>
    public static string Text(string question, string? defaultValue = null, Func<string, string?>? validate = null, bool allowEmpty = false)
    {
        while (true)
        {
            var hint = string.IsNullOrEmpty(defaultValue) ? "" : $" [grey](Enter — {Markup.Escape(defaultValue)})[/]";
            AnsiConsole.Markup($"{Markup.Escape(question)}{hint} ");

            var answer = ReadLine().Trim();
            if (answer.Length == 0 && !string.IsNullOrEmpty(defaultValue))
                answer = defaultValue;

            if (answer.Length == 0 && !allowEmpty)
            {
                AnsiConsole.MarkupLine("[red]Нужно что-нибудь ввести.[/]");
                continue;
            }

            var error = answer.Length == 0 ? null : validate?.Invoke(answer);
            if (error is null)
                return answer;

            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
        }
    }

    /// <summary>Целое число от min до max.</summary>
    public static int Number(string question, int min, int max, int? defaultValue = null)
    {
        var answer = Text(question, defaultValue?.ToString(), input =>
            int.TryParse(input, out var n) && n >= min && n <= max ? null : $"Нужно число от {min} до {max}");

        return int.Parse(answer);
    }

    /// <summary>Да/нет. Принимает «д», «да», «y», «yes» и «н», «нет», «n», «no».</summary>
    public static bool YesNo(string question, bool defaultValue)
    {
        var answer = Text($"{question} (д/н)", defaultValue ? "д" : "н", input =>
            IsYes(input) || IsNo(input) ? null : "Ответьте «д» (да) или «н» (нет)");

        return IsYes(answer);
    }

    /// <summary>Скрытый ввод (звёздочки) — для токена. Если консоль не позволяет, читаем обычной строкой.</summary>
    public static string Secret(string question)
    {
        AnsiConsole.Markup($"{Markup.Escape(question)} ");
        if (Console.IsInputRedirected)
            return ReadLine().Trim();

        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        Console.WriteLine();
        return text.ToString().Trim();
    }

    private static string ReadLine() =>
        Console.ReadLine() ?? throw new EndOfStreamException("Ввод закончился — программа завершает работу.");

    private static bool IsYes(string input) => input.ToLowerInvariant() is "д" or "да" or "y" or "yes";

    private static bool IsNo(string input) => input.ToLowerInvariant() is "н" or "нет" or "n" or "no";
}

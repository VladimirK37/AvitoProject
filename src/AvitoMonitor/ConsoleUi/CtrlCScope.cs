namespace AvitoMonitor.ConsoleUi;

/// <summary>
/// Пока объект жив, Ctrl+C не закрывает программу, а только останавливает текущую операцию
/// (сканирование или слежение) — и вы возвращаетесь в меню.
/// </summary>
/// <example>
/// using var stop = new CtrlCScope();
/// await workflow.RunAsync(..., stop.Token);
/// </example>
public sealed class CtrlCScope : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public CtrlCScope() => Console.CancelKeyPress += OnCancelKeyPress;

    public CancellationToken Token => _cts.Token;

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cts.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // не завершать процесс
        _cts.Cancel();
    }
}

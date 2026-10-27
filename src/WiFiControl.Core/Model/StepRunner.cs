namespace WiFiControl.Core.Model;

// Перенесено из NetSwitch

/// <summary>Чем закончился шаг применения: название и ошибка, понятная человеку, либо её отсутствие.</summary>
public sealed class StepResult(string title, string? error)
{
    public string Title { get; } = title;
    public string? Error { get; } = error;
    public bool Ok => Error == null;
}

/// <summary>
/// Выполняет шаги применения. Исключение шага становится ошибкой в его результате и не мешает
/// выполнить следующие шаги. Отмена — не сбой шага: она уходит наружу.
/// </summary>
public static class StepRunner
{
    public static async Task<StepResult> RunAsync(string title, Func<CancellationToken, Task> step, CancellationToken cancellationToken)
    {
        try
        {
            await step(cancellationToken).ConfigureAwait(false);
            return new StepResult(title, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return new StepResult(title, e.Message);
        }
    }
}

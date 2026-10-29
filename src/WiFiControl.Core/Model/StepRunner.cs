namespace WiFiControl.Core.Model;

// Перенесено из NetSwitch

/// <summary>Чем закончился шаг применения.</summary>
public enum StepOutcome
{
    Done,
    /// <summary>Шаг выполнен, но не всё в порядке: например, шлюз не отвечает. Отката не вызывает.</summary>
    Warning,
    Error,
}

/// <summary>Чем закончился шаг применения: название, исход и пояснение, понятное человеку.</summary>
public sealed class StepResult(string title, StepOutcome outcome, string? message)
{
    /// <summary>Выполнен, если ошибки нет, иначе — ошибка.</summary>
    public StepResult(string title, string? error) : this(title, error == null ? StepOutcome.Done : StepOutcome.Error, error)
    {
    }

    public static StepResult Warning(string title, string message) => new(title, StepOutcome.Warning, message);

    public string Title { get; } = title;
    public StepOutcome Outcome { get; } = outcome;
    /// <summary>Объяснение ошибки или предупреждения; у выполненного шага — необязательное уточнение.</summary>
    public string? Message { get; } = message;
    public string? Error => Outcome == StepOutcome.Error ? Message : null;
    /// <summary>Ошибки нет; предупреждение — не ошибка.</summary>
    public bool Ok => Outcome != StepOutcome.Error;
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
        // Объяснение по-русски система даёт в InvalidOperationException (см. INetworkSystem)
        catch (InvalidOperationException e)
        {
            return new StepResult(title, e.Message);
        }
        // Остальное — ошибка в коде: человеку — русское начало, для разбора — тип и исходный текст
        catch (Exception e)
        {
            return new StepResult(title, "Непредвиденная ошибка (" + e.GetType().Name + "): " + e.Message);
        }
    }
}

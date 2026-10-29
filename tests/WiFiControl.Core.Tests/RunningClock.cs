using Microsoft.Extensions.Time.Testing;

namespace WiFiControl.Core.Tests;

/// <summary>
/// Подменённые часы, которые не ждут: как только код заводит таймер (ожидание, срок), время сразу
/// переводится к его сроку. Так ожидания проходят мгновенно и в том же порядке, что и на настоящих часах.
/// </summary>
internal sealed class RunningClock : FakeTimeProvider
{
    /// <summary>Сколько прошло с начала теста.</summary>
    public TimeSpan Elapsed => GetUtcNow() - Start;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);
        if (dueTime != Timeout.InfiniteTimeSpan) Advance(dueTime);
        return timer;
    }
}

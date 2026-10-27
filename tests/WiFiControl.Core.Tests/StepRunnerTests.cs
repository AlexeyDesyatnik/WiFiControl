using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class StepRunnerTests
{
    [Fact]
    public async Task Failed_step_gives_its_error_and_the_next_step_still_runs()
    {
        bool secondRan = false;

        StepResult first = await StepRunner.RunAsync("Первый", _ => throw new InvalidOperationException("Адаптер «Wi-Fi» не найден."), CancellationToken.None);
        StepResult second = await StepRunner.RunAsync("Второй", _ =>
        {
            secondRan = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.False(first.Ok);
        Assert.Equal("Первый", first.Title);
        Assert.Equal("Адаптер «Wi-Fi» не найден.", first.Error);
        Assert.True(secondRan);
        Assert.True(second.Ok);
        Assert.Null(second.Error);
    }
}

using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public void Only_first_service_acquires_named_mutex()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using SingleInstanceService first = CreateService(suffix);
        using SingleInstanceService second = CreateService(suffix);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void Second_instance_notifies_first_instance()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using SingleInstanceService first = CreateService(suffix);
        using SingleInstanceService second = CreateService(suffix);
        using ManualResetEventSlim activated = new(false);
        first.ActivationRequested += (_, _) => activated.Set();

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
        second.NotifyExistingInstance();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
    }

    private static SingleInstanceService CreateService(string suffix)
    {
        return new SingleInstanceService(
            $@"Local\HorizonRadioOverlay.Tests.Mutex.{suffix}",
            $@"Local\HorizonRadioOverlay.Tests.Activate.{suffix}");
    }
}

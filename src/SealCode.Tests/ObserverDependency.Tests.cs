using Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Models.Configuration;

using Moq;

using SealCode.Hosting;
using SealCode.Observers;

namespace SealCode.Tests;

public sealed class ObserverDependencyTests
{
    [Fact]
    public void HubUsesTheSameObserverSingletonAsHostedDelivery()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"]
        }));
        services.AddSingleton(Mock.Of<IRoomRegistry>());
        services.AddSealCodeSignalR();
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<ObserverSessions>(),
            provider.GetRequiredService<IObserverUpdates>());
    }
}

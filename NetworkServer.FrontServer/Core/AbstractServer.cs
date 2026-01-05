using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Network.Server.Front.Actor;

namespace Network.Server.Front.Core;

public abstract class AbstractServer : IHostedService
{
    protected IActorManager ActorManager { get; }
    protected ILogger Logger { get; }
    protected IServiceProvider ServiceProvider { get; }
    
    protected AbstractServer(
        IActorManager actorManager,
        ILogger logger,
        IServiceProvider serviceProvider)
    {
        ActorManager = actorManager;
        Logger = logger;
        ServiceProvider = serviceProvider;
    }
    
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("{Name} started.", GetType().Name);
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("{Name} stopped.", GetType().Name);
        return Task.CompletedTask;
    }
}
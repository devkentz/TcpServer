using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Network.Server.Front.Core;

namespace Network.Server.Front.Actor;

public class Actor : IActor, IDisposable
{
    private readonly IServiceProvider _rootProvider;
    private readonly QueuedResponseWriter<ActorMessage> _messageQueue;
    private readonly MessageHandler _handler;
    private readonly NetworkSession _session;

    public ushort SequenceId { get; private set; } = 0;
    public long ActorId { get; }

    public Actor(ILogger logger, NetworkSession session, long actorId, IServiceProvider rootProvider)
    {
        _rootProvider = rootProvider;
        ActorId = actorId;
        _session = session;
        _handler = _rootProvider.GetRequiredService<MessageHandler>();
        _messageQueue = new QueuedResponseWriter<ActorMessage>(ProcessMessageAsync, logger);
    }

    private async Task ProcessMessageAsync(ActorMessage actorMessage)
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var response = await _handler.Handling(scope.ServiceProvider, this, actorMessage);
        response.Header.MsgSeq = SequenceId++;
        _session.SendToClient(response.Header, response.Message);
    }

    public void Push(ActorMessage message)
    {
        _messageQueue.Write(message);
    }

    public void Dispose()
    {
        _messageQueue.Dispose();
    }
}
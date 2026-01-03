using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Network.Server.Node.Network;

namespace Network.Server.Node.Core;

public class Actor : IActor
{
    private readonly IServiceProvider _rootProvider;
    private readonly QueuedResponseWriter<ActorMessage> _messageQueue;
    private readonly INodeResponser _responser;
    private readonly MessageHandler _handler;

    public Actor(ILogger logger, long actorId, IServiceProvider rootProvider)
    {
        _rootProvider = rootProvider;
        ActorId = actorId;
        _responser = _rootProvider.GetRequiredService<INodeResponser>();
        _handler = _rootProvider.GetRequiredService<MessageHandler>();
        _messageQueue = new QueuedResponseWriter<ActorMessage>(ProcessMessageAsync, logger);
    }

    private async Task ProcessMessageAsync(ActorMessage actorMessage)
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var response = await _handler.Handling(scope.ServiceProvider, this, actorMessage);
        _responser.Response(actorMessage.Header, response);
    }

    public long ActorId { get; }

    public void Push(ActorMessage message)
    {
        _messageQueue.Write(message);
    }

    public void Response(ActorMessage actorMessage, IMessage response)
    {
        _responser.Response(actorMessage.Header, response);
    }
}
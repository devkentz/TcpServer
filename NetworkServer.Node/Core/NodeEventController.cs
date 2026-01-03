using Microsoft.Extensions.Logging;
using Network.Server.Common.Packets;
using Network.Server.Node.Network;

namespace Network.Server.Node.Core;

public abstract class NodeEventController
{
    protected readonly long CreateActorMsgId;
    private readonly QueuedResponseWriter<ActorMessage> _messageQueue;
    protected readonly ILogger _logger;

    public NodeEventController(long createActorMsgId, ILogger logger)
    {
        CreateActorMsgId = createActorMsgId;
        _logger = logger;
        _messageQueue = new QueuedResponseWriter<ActorMessage>(DispatchAsync, logger);
    }

    private Task DispatchAsync(ActorMessage message)
    {
        return CreateActorAsync(message);
    }

    public void Push(ActorMessage message)
    {
        _messageQueue.Write(message);
    }

    protected virtual Task<IActor> CreateActorAsync(ActorMessage createPacket)
    {
        //TODO : ActorFactory
        throw new NotImplementedException();
    }

    public virtual Task RemoveActorAsync(long actorId)
    {
        throw new NotImplementedException();
    }

    public virtual void OnLeaveNode(RemoteNode remoteNode)
    {
    }

    public virtual void OnJoinNode(RemoteNode remoteNode)
    {
    }

    public virtual void OnPacket(InternalPacket internalPacket)
    {
    }
}
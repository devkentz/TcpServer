using Internal.Protocol;
using Microsoft.Extensions.Logging;
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;
using Network.Server.Node.Network;
using Newtonsoft.Json;

namespace Network.Server.Node.Core;

public class DefaultEventController : NodeEventController
{
    private readonly UniqueIdGenerator _idGenerator;
    private readonly IActorManager _actorManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ActorMessageFactory _factory;

    public DefaultEventController(
        int createActorMsgId,
        UniqueIdGenerator idGenerator,
        ILogger<DefaultEventController> logger,
        IActorManager actorManager,
        IServiceProvider serviceProvider,
        ActorMessageFactory factory) : base(createActorMsgId, logger)
    {
        _idGenerator = idGenerator;
        _actorManager = actorManager;
        _serviceProvider = serviceProvider;
        _factory = factory;
    }

    protected override Task<IActor> CreateActorAsync(ActorMessage createPacket)
    {
        var actor = new Actor(_logger, _idGenerator.NextId(), _serviceProvider);
        _actorManager.AddActor(actor);

        actor.Response(createPacket, new DefaultCreatePacketRes {ActorId = actor.ActorId});
        return Task.FromResult<IActor>(actor);
    }

    public override Task RemoveActorAsync(long actorId)
    {
        _logger.LogInformation("Removed actor {actorId}", actorId);
        _actorManager.RemoveActor(actorId);
        return Task.CompletedTask;
    }

    public override void OnJoinNode(RemoteNode remoteNode)
    {
        _logger.LogInformation("OnNodeNewAsync {remoteNode}", JsonConvert.SerializeObject(remoteNode));
    }

    public override void OnLeaveNode(RemoteNode remoteNode)
    {
        _logger.LogInformation("Removed Node {remoteNode}", JsonConvert.SerializeObject(remoteNode));
    }

    public override void OnPacket(InternalPacket internalPacket)
    {
        using (internalPacket)
        {
            var msg = _factory.Create(internalPacket);
            if (msg == null)
            {
                _logger.LogInformation("message parse failed actorId:{actorId} msgId:{msgId}", internalPacket.ActorId, internalPacket.MsgId);
                return;
            }

            var actor = _actorManager.FindActor(internalPacket.ActorId);
            if (actor == null)
            {
                _logger.LogInformation("Actor not found {actorId}", internalPacket.ActorId);

                if (internalPacket.MsgId == CreateActorMsgId)
                    Push(msg);
                
                return;
            }

            actor.Push(msg);
        }
    }
}
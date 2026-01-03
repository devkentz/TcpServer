using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Network.Server.Common.Packets;
using Network.Server.Node.Network;

namespace Network.Server.Node.Core;

public class StatelessEventController : NodeEventController
{
    private readonly ActorMessageFactory _actorMessageFactory;
    private readonly INodeResponser _responser;
    private readonly IServiceProvider _rootProvider;
    private readonly MessageHandler _messageHandler;

    public StatelessEventController(
        ActorMessageFactory actorMessageFactory,
        ILogger<StatelessEventController> logger,
        MessageHandler messageHandler,
        INodeResponser responser,
        IServiceProvider rootProvider) : base(0, logger)
    {
        _actorMessageFactory = actorMessageFactory;
        _messageHandler = messageHandler;
        _responser = responser;
        _rootProvider = rootProvider;
    }

    public override void OnPacket(InternalPacket internalPacket)
    {
        Task.Run(async () =>
        {
            IMessage? response = null;
            using (internalPacket)
            {
                try
                {
                    var message = _actorMessageFactory.Create(internalPacket);
                    if (message == null)
                    {
                        //TODO : ERROR 처리
                        return;
                    }

                    using (_logger.BeginScope(new Dictionary<string, object>
                           {
                               ["ActorId"] = internalPacket.ActorId,
                               ["Request"] = message.Message.Descriptor.FullName,
                               ["SourceNode"] = internalPacket.Source
                           }))
                    {
                        await using var scope = _rootProvider.CreateAsyncScope();
                        response = await _messageHandler.Handling(scope.ServiceProvider, message);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                }

                if (response != null)
                    _responser.Response(internalPacket.Header, response);
            }
        });
    }
}
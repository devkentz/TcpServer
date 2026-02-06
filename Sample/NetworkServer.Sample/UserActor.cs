using Microsoft.Extensions.Logging;
using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Core;

namespace NetworkServer.Sample;

public class UserActor : Actor
{
    public string ExternalId { get; }
    public UserActor(
        ILogger logger,
        NetworkSession session,
        long actorId,
        string externalId,
        IServiceProvider rootProvider,
        MessageHandler handler)
        : base(logger, session, actorId, rootProvider, handler)
    {
        ExternalId = externalId;
    }
}
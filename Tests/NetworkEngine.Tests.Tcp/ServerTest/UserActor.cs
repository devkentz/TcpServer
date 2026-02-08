using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Core;

namespace NetworkEngine.Tests.Node.ServerTest.Handler;

/// <summary>
/// InGameConnection 요청을 큐로 처리하는 서비스
/// </summary>
public class UserActor : Actor
{
    public readonly string ExternalId;

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
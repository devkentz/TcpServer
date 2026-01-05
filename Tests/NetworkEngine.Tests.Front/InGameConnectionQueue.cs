using Google.Protobuf;
using Network.Server.Common.DistributeLock;
using Network.Server.Front.Actor;
using Network.Server.Front.Core;
using Proto.Test;
using StackExchange.Redis;

namespace NetworkEngine.Tests.Node;

/// <summary>
/// InGameConnection 요청을 큐로 처리하는 서비스
/// </summary>
public class InGameConnectionQueue : IInGameConnectionQueue, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InGameConnectionQueue> _logger;
    private readonly IActorManager _actorManager;
    private readonly TimeProvider _timeProvider;
    private volatile bool _isDisposed = false;

    public InGameConnectionQueue(
        IServiceProvider serviceProvider,
        ILogger<InGameConnectionQueue> logger,
        IActorManager actorManager,
        IDatabase redis,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _actorManager = actorManager;
        _timeProvider = timeProvider;
    }

    public void EnqueueAsync(NetworkSession session, ActorMessage message)
    {
        //fire-and-forget 방식으로 즉시 반환
        _ = ProcessConnectionAsync(session, message);
    }

    public bool IsInGameConnectionPacket(int msgId)
    {
        // PacketType enum에서 정의된 InGame Connection 패킷 타입들
        return msgId == InGameConnectionReq.MsgId;
    }

    private async Task ProcessConnectionAsync(NetworkSession session, ActorMessage message)
    {
        try
        {
            _logger.LogInformation("Processing connection request for session {SessionId}", session.SessionId);

            var connectionReq = (InGameConnectionReq)message.Message;
            
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process connection for session {SessionId}", session.SessionId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            _logger.LogInformation("InGameConnection queue disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during InGameConnection queue disposal");
        }
    }
}
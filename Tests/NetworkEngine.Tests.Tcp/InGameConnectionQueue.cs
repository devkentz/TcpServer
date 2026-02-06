using Microsoft.Extensions.Options;
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;
using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Config;
using Network.Server.Tcp.Core;
using Proto.Test;

namespace NetworkEngine.Tests.Node;

/// <summary>
/// InGameConnection 요청을 큐로 처리하는 서비스
/// </summary>
public class UserActor : Actor
{
    public readonly string ExternalId;

    public UserActor(ILogger logger, NetworkSession session, long actorId, string externalId, IServiceProvider rootProvider)
        : base(logger, session, actorId, rootProvider)
    {
        ExternalId = externalId;
    }
}

public class InGameConnectionQueue : IConnectionHandler, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly UniqueIdGenerator _uniqueIdGenerator;
    private readonly ILogger<InGameConnectionQueue> _logger;
    private readonly IActorManager _actorManager;
    private volatile bool _isDisposed = false;
    private readonly SemaphoreSlim _semaphoreSlim;
    private readonly CancellationTokenSource _cancellationToken;

    public InGameConnectionQueue(
        IServiceProvider serviceProvider,
        UniqueIdGenerator uniqueIdGenerator,
        ILogger<InGameConnectionQueue> logger,
        IActorManager actorManager,
        IOptions<TcpServerConfig> tcpServerConfig)
    {
        _serviceProvider = serviceProvider;
        _uniqueIdGenerator = uniqueIdGenerator;
        _logger = logger;
        _actorManager = actorManager;

        _cancellationToken = new CancellationTokenSource();
        var config = tcpServerConfig.Value;
        _semaphoreSlim = new SemaphoreSlim(config.LoginConcurrentSize, config.LoginConcurrentSize);
    }

    public void EnqueueAsync(NetworkSession session, ActorMessage message)
    {
        //fire-and-forget 방식으로 즉시 반환
        _ = ProcessConnectionAsync(session, message);
    }

    public bool IsInGameConnectionPacket(int msgId)
    {
        return msgId == LoginGameReq.MsgId;
    }

    private async Task ProcessConnectionAsync(NetworkSession session, ActorMessage message)
    {
        try
        {
            await _semaphoreSlim.WaitAsync(_cancellationToken.Token);

            var req = (LoginGameReq) message.Message;

            _logger.LogInformation("Processing connection request for session {SessionId}", session.SessionId);

            var userActor = new UserActor(_logger, session, _uniqueIdGenerator.NextId(), req.ExternalId, _serviceProvider);

            if (_actorManager.FirstOrDefault(e => (((UserActor)e).ExternalId == req.ExternalId)) is UserActor existingActor)
            {
                _actorManager.RemoveActor(existingActor.ActorId);
                existingActor.Session.Disconnect();
            }
            
            _actorManager.TryAddActor(userActor);
            session.SendToClient(new Header(msgId: LoginGameRes.MsgId, msgSeq: session.SequenceId++, requestId: message.Header.RequestId), new LoginGameRes {Success = true});
        }
        catch (OperationCanceledException e)
        {
            _logger.LogInformation(e, "canceled request : {e}", e);
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "error {SessionId}", session.SessionId);
            session.SendToClient(new Header(flags: PacketFlags.HasError, errorCode: (ushort) ErrorCode.ServerError, requestId: message.Header.RequestId), new LoginGameRes());
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _cancellationToken.Cancel();
        _cancellationToken.Dispose();
        _semaphoreSlim.Dispose();
    }
}
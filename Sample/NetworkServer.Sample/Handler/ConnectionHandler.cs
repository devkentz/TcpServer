using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;
using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Config;
using Network.Server.Tcp.Core;
using Proto.Sample;

namespace NetworkServer.Sample.Handler;

public class ConnectionHandler : IConnectionHandler, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly UniqueIdGenerator _uniqueIdGenerator;
    private readonly ILogger<ConnectionHandler> _logger;
    private readonly IActorManager _actorManager;
    private readonly MessageHandler _messageHandler;
    private readonly TimeProvider _timeProvider;
    private volatile bool _isDisposed = false;
    private readonly SemaphoreSlim _semaphoreSlim;
    private readonly CancellationTokenSource _cancellationToken;

    public ConnectionHandler(
        IServiceProvider serviceProvider,
        UniqueIdGenerator uniqueIdGenerator,
        ILogger<ConnectionHandler> logger,
        IActorManager actorManager,
        MessageHandler messageHandler,
        IOptions<TcpServerConfig> tcpServerConfig,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _uniqueIdGenerator = uniqueIdGenerator;
        _logger = logger;
        _actorManager = actorManager;
        _messageHandler = messageHandler;
        _timeProvider = timeProvider;
        // _database = redisConnectionMultiplexer; // Redis Removed

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
            
            // Redis Lock Removed - Simplified for Sample
            // var  database = _database.GetDatabase(); 
            // await using var lockObj = await database.TryAcquireLockAsync(req.ExternalId); ...

            //TODO : REDIS에 유저 세션 객체 저장 & 중복접속 처리

            _logger.LogInformation("Processing connection request for session {SessionId}", session.SessionId);

            
            // TODO : ExternalId 
            if (_actorManager.FirstOrDefault(e => ((UserActor) e).ExternalId == req.ExternalId) is UserActor existingActor)
            {
                existingActor.Session.Disconnect();
                _actorManager.RemoveActor(existingActor.ActorId);
            }

            var userActor = new UserActor(_logger, session, _uniqueIdGenerator.NextId(), req.ExternalId, _serviceProvider, _messageHandler);
            _actorManager.TryAddActor(userActor);
            
            
            session.SendToClient(new Header(msgId: LoginGameRes.MsgId, msgSeq: session.SequenceId++, requestId: message.Header.RequestId), new LoginGameRes {Success = true});
        }
        catch (OperationCanceledException e)
        {
            _logger.LogInformation(e, "Processing connection request for session {SessionId}", session.SessionId);
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "error {SessionId}", session.SessionId);
            session.SendToClient(new Header(flags: PacketFlags.HasError, requestId: message.Header.RequestId, errorCode: (ushort) ErrorCode.ServerError), new LoginGameRes());
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
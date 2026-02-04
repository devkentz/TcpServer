using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Network.Server.Common.DistributeLock;
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;
using Network.Server.Front.Actor;
using Network.Server.Front.Config;
using Network.Server.Front.Core;
using Proto.Test;
using StackExchange.Redis;

namespace NetworkServer.Sample;

/// <summary>
/// InGameConnection 요청을 큐로 처리하는 서비스
/// </summary>
public class UserActor : Actor
{
    public string ExternalId { get; }
    public UserActor(ILogger logger, NetworkSession session, long actorId, string externalId, IServiceProvider rootProvider)
        : base(logger, session, actorId, rootProvider)
    {
        ExternalId = externalId;
    }
}

public class InGameConnectionQueue : IInGameConnectionQueue, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly UniqueIdGenerator _uniqueIdGenerator;
    private readonly IConnectionMultiplexer _database;
    private readonly ILogger<InGameConnectionQueue> _logger;
    private readonly IActorManager _actorManager;
    private readonly TimeProvider _timeProvider;
    private volatile bool _isDisposed = false;
    private readonly SemaphoreSlim _semaphoreSlim;
    private readonly CancellationTokenSource _cancellationToken;

    public InGameConnectionQueue(
        IServiceProvider serviceProvider,
        UniqueIdGenerator uniqueIdGenerator,
        ILogger<InGameConnectionQueue> logger,
        IActorManager actorManager,
        IOptions<FrontServerConfig> frontServerConfig,
        TimeProvider timeProvider,
        [FromKeyedServices("InGameConnectionQueue")]
        IConnectionMultiplexer redisConnectionMultiplexer)
    {
        _serviceProvider = serviceProvider;
        _uniqueIdGenerator = uniqueIdGenerator;
        _logger = logger;
        _actorManager = actorManager;
        _timeProvider = timeProvider;
        _database = redisConnectionMultiplexer;

        _cancellationToken = new CancellationTokenSource();
        var config = frontServerConfig.Value;
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
            
            var  database = _database.GetDatabase();
            //같은 유저에 대한, 로그인 처리가 중복으로 일어나지 않게 하기 위한, 레디스를 이용한 분산락
            await using var lockObj = await database.TryAcquireLockAsync(req.ExternalId);
            if (lockObj == null)
                throw new Exception($"Unable to acquire lock for {req.ExternalId}");

            //TODO : DB처리
            //TODO : REDIS에 유저 세션 객체 저장 & 중복접속 처리

            _logger.LogInformation("Processing connection request for session {SessionId}", session.SessionId);


            if (_actorManager.FirstOrDefault(e => ((UserActor) e).ExternalId == req.ExternalId) is UserActor existingActor)
            {
                existingActor.Session.Disconnect();
                _actorManager.RemoveActor(existingActor.ActorId);
            }

            var userActor = new UserActor(_logger, session, _uniqueIdGenerator.NextId(), req.ExternalId, _serviceProvider);
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
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;
using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Core;
using Proto.Test;

namespace NetworkEngine.Tests.Node.ServerTest.Handler;

public class ConnectionHandler : IConnectionHandler, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly UniqueIdGenerator _uniqueIdGenerator;
    private readonly ILogger<ConnectionHandler> _logger;
    private readonly IActorManager _actorManager;
    private readonly MessageHandler _messageHandler;
    private volatile bool _isDisposed;
    private readonly CancellationTokenSource _cancellationToken;

    public ConnectionHandler(
        IServiceProvider serviceProvider,
        UniqueIdGenerator uniqueIdGenerator,
        ILogger<ConnectionHandler> logger,
        IActorManager actorManager,
        MessageHandler messageHandler)
    {
        _serviceProvider = serviceProvider;
        _uniqueIdGenerator = uniqueIdGenerator;
        _logger = logger;
        _actorManager = actorManager;
        _messageHandler = messageHandler;

        _cancellationToken = new CancellationTokenSource();
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

    private Task ProcessConnectionAsync(NetworkSession session, ActorMessage message)
    {
        try
        {
            var req = (LoginGameReq) message.Message;

            _logger.LogInformation("Processing connection request for session {SessionId}", session.SessionId);

            var userActor = new UserActor(_logger, session, _uniqueIdGenerator.NextId(), req.ExternalId, _serviceProvider, _messageHandler);

            if (_actorManager.FirstOrDefault(e => (((UserActor) e).ExternalId == req.ExternalId)) is UserActor existingActor)
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
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _cancellationToken.Cancel();
        _cancellationToken.Dispose();
    }
}
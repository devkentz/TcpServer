using Google.Protobuf;
using Network.Server.Front.Actor;

namespace Network.Server.Front.Core;

public class MessageHandler
{
    private readonly Dictionary<long, Func<IServiceProvider, IActor, IMessage, Task<Response>>> _actorHandlers = new();

    protected void AddHandler<TRequest>(
        long msgId,
        Func<IServiceProvider, IActor, TRequest, Task<Response>> func)
        where TRequest : class, IMessage, new()
    {
        _actorHandlers[msgId] = async (provider, actor, message) =>
        {
            if (message is not TRequest request)
                throw new InvalidCastException($"Cannot cast {message.GetType().Name} to {typeof(TRequest).Name}");

            return await func(provider, actor, request);
        };
    }

    public Task<Response> Handling(IServiceProvider provider, IActor actor, ActorMessage actorMessage)
    {
        if (_actorHandlers.TryGetValue(actorMessage.Header.MsgId, out var handler))
            return handler.Invoke(provider, actor, actorMessage.Message);

        throw new Exception($"No handler registered for MsgId: {actorMessage.Header.MsgId}");
    }

    public void Initialize()
    {
        LoadHandlers();
    }

    protected virtual void LoadHandlers()
    {
    }
}
using Google.Protobuf;

namespace Network.Server.Node.Core;

public class MessageHandler
{
    private readonly Dictionary<long, Func<IServiceProvider, IMessage, Task<IMessage>>> _handlers = new();
    private readonly Dictionary<long, Func<IServiceProvider, IActor, IMessage, Task<IMessage>>> _actorHandlers = new();

    protected void AddHandler<TRequest, TResponse>(
        long msgId,
        Func<IServiceProvider, TRequest, Task<TResponse>> func)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        _handlers[msgId] = async (provider, message) =>
        {
            if (message is not TRequest request)
                throw new InvalidCastException($"Cannot cast {message.GetType().Name} to {typeof(TRequest).Name}");

            return await func(provider, request);
        };
    }

    protected void AddHandler<TRequest, TResponse>(
        long msgId,
        Func<IServiceProvider, IActor, TRequest, Task<TResponse>> func)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        _actorHandlers[msgId] = async (provider, actor, message) =>
        {
            if (message is not TRequest request)
                throw new InvalidCastException($"Cannot cast {message.GetType().Name} to {typeof(TRequest).Name}");

            return await func(provider, actor, request);
        };
    }

    public Task<IMessage> Handling(IServiceProvider provider, ActorMessage actorMessage)
    {
        if (_handlers.TryGetValue(actorMessage.Header.MsgId, out var handler))
            return handler.Invoke(provider, actorMessage.Message);

        throw new Exception($"No handler registered for MsgId: {actorMessage.Header.MsgId}");
    }

    public Task<IMessage> Handling(IServiceProvider provider, IActor actor, ActorMessage actorMessage)
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
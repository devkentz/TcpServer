using System.Collections.Concurrent;
using Network.Server.Tcp.Core;

namespace Network.Server.Tcp.Actor;

public interface IActorManager
{
    public IActor? GetActor(long actorId);
    void RemoveActor(long actorId);
    IActor? FirstOrDefault(Func<IActor, bool> predicate);
    bool TryAddActor(IActor userActor);
}

public class ActorManager : IActorManager
{
    private readonly ConcurrentDictionary<long, IActor> _actorsById = new();

    public IActor? GetActor(long actorId)
    {
        return _actorsById.GetValueOrDefault(actorId);
    }

    public void RemoveActor(long actorId)
    {
        _actorsById.TryRemove(actorId, out _);
    }

    public IActor? FirstOrDefault(Func<IActor, bool> predicate)
    {
        return _actorsById.Values.FirstOrDefault(predicate);
    }

    public bool TryAddActor(IActor actor)
    {
        return _actorsById.TryAdd(actor.ActorId, actor);
    }
}
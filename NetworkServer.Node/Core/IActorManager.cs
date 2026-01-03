using System.Collections.Concurrent;

namespace Network.Server.Node.Core;

public interface IActorManager
{
    public IActor? FindActor(long actorId);
    void AddActor(IActor actor);
    void RemoveActor(long actorId);
}

public class ActorManager : IActorManager
{
    private readonly ConcurrentDictionary<long, IActor> _actorsById = new ConcurrentDictionary<long, IActor>();

    public IActor? FindActor(long actorId)
    {
        return _actorsById.GetValueOrDefault(actorId);
    }

    public void AddActor(IActor actor)
    {
        _actorsById.TryAdd(actor.ActorId, actor);
    }

    public void RemoveActor(long actorId)
    {
        _actorsById.TryRemove(actorId, out _);
    }
}
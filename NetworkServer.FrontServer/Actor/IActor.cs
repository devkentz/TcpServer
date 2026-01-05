namespace Network.Server.Front.Actor;

public interface IActor
{
    public long ActorId { get; }
    public void Push(ActorMessage message);
}
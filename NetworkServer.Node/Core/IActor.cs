namespace Network.Server.Node.Core;

public interface IActor
{
    public long ActorId { get; }
    public void Push(ActorMessage message);
}
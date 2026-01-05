using Google.Protobuf;
using Network.Server.Common.Packets;

namespace Network.Server.Front.Actor;

public class ActorMessage
{
    public Header Header { get; }
    public IMessage Message { get; }

    public ActorMessage(Header header, IMessage message)
    {
        Header = header;
        Message = message;
    }
}
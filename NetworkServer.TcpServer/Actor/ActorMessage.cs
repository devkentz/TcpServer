using Google.Protobuf;
using Network.Server.Common.Packets;

namespace Network.Server.Tcp.Actor;

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
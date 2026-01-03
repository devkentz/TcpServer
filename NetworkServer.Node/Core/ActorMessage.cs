using Google.Protobuf;
using Network.Server.Common;
using Network.Server.Common.Packets;

namespace Network.Server.Node.Core;

public class ActorMessage
{
    public InternalHeader Header { get; }
    public IMessage Message { get; }

    public ActorMessage(InternalHeader header, IMessage message)
    {
        Header = header;
        Message = message;
    }
}
namespace Network.Server.Front.Core;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ServerControllerAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class PacketHandlerAttribute : Attribute
{
    public long MsgId { get; }
    public PacketHandlerAttribute(long msgId)
    {
        MsgId = msgId;
    }
}
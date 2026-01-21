using Google.Protobuf;

namespace ProtoTestTool.ScriptContract;

public interface IPacket
{
    public IMessage Message { get; }
    public IHeader  Header { get; }
}
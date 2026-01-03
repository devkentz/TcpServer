using Network.Server.Common.Memory;

namespace Network.Server.Common.Packets;

public interface IPacketParser<T>
{
    public IReadOnlyList<T> Parse(ArrayPoolBufferWriter buffer);
}

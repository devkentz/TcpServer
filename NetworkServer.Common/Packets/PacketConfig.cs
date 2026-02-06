namespace Network.Server.Common.Packets;

public class PacketConfig
{
    public int MsgIdLimit { get; set; } = 256;
    public int MaxPacketSize { get; set; } = 1024 * 1024 * 2;
    public int MinPacketSize { get; set; } = 17;
    public int MinCompressionSize { get; set; } = 1500;
}

public static class PacketDefine
{
    public static int MaxPacketSize { get; set; } = 1024 * 1024 * 2;
}
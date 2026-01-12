using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Network.Server.Common.Utils;

namespace Network.Server.Common.Packets;

[Flags]
public enum PacketFlags : byte
{
    None = 0,
    HasError = 1 << 0, // 0000 0001
    Compressed = 1 << 1, // 0000 0010
    Encrypted = 1 << 2, // 0000 0100
}

public static class PacketFlagsExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PacketFlags AddFlag(this PacketFlags current, PacketFlags flag)
        => current | flag;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PacketFlags RemoveFlag(this PacketFlags current, PacketFlags flag)
        => current & ~flag;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasFlagFast(this PacketFlags current, PacketFlags flag)
        => (current & flag) != 0;
}

//[PacketSize(4byte)][flags(1byte)][msgId(string max255)][msgSeq(2byte)][option errorCode(2byte)]
public class Header
{
    public Header(PacketFlags flags = PacketFlags.None,
        int msgId = 0,
        ushort msgSeq = 0,
        ushort errorCode = 0,
        int originalSize = 0)
    {
        Flags = flags;
        MsgId = msgId;
        MsgSeq = msgSeq;
        ErrorCode = errorCode;
        OriginalSize = originalSize;
    }

    public PacketFlags Flags { get; set; }

    public int MsgId { get; set; } // TODO : To int or Ushort

    public ushort MsgSeq { get; set; } //   reconnect hand over

    public ushort ErrorCode { get; set; } //optional

    public int OriginalSize { get; set; } //optional

    // first 4byte => bodySize
    private const int FixedSize = sizeof(int) /*msg_id */ + sizeof(int) /*total_size*/ + sizeof(byte) /*flags*/ + sizeof(ushort) /*msg_seq*/;

    public int GetSize()
    {
        return FixedSize + (HasError ? sizeof(ushort) : 0) + (IsCompressed ? sizeof(int) : 0);
    }

    public override string ToString()
    {
        return $"MsgId: {MsgId}, MsgSeq: {MsgSeq}, ErrorCode: {ErrorCode}, OriginalSize: {OriginalSize}";
    }

    //Flag helpers
    public bool HasError => (Flags & PacketFlags.HasError) != 0;
    public bool IsCompressed => (Flags & PacketFlags.Compressed) != 0;
    public bool IsEncrypted => (Flags & PacketFlags.Encrypted) != 0;
}

public static class PacketExtensions
{
    public static int CalcSize(this (Header header, IMessage message) packet)
    {
        return packet.header.GetSize() + packet.message.CalculateSize();
    }

    public static int WriteToSpan(this (Header header, IMessage message) packet, Span<byte> buffer, int packetSize = 0)
    {
        packetSize = packetSize > 0 ? packetSize : packet.CalcSize();
        if (packetSize > PacketDefine.MaxPacketSize)
            throw new Exception($"packet size is over : {packetSize}");

        int offset = 0;

        // PacketSize
        BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], packetSize);
        offset += 4;

        // flags
        buffer[offset] = (byte) packet.header.Flags;
        offset += 1;

        // msgId
        BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], packet.header.MsgId);
        offset += 4;

        // msgSeq
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], packet.header.MsgSeq);
        offset += 2;
        
        if(packet.header.HasError)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], packet.header.ErrorCode);
            offset += 2;
        }
        
        // payload
        int payloadSize = packetSize - packet.header.GetSize();

        if (packet.header.IsCompressed)
        {
            ProtobufCompressor.Compress(packet.message, buffer.Slice(offset + sizeof(int), payloadSize));
            
            BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], payloadSize);
            offset += 4;
        }
        
        else
        {
            packet.message.WriteTo(buffer.Slice(offset, payloadSize));
            offset += payloadSize;
        }

        return offset;
    }
}
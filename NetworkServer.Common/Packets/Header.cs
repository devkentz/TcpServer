using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Google.Protobuf;

namespace Network.Server;

[Flags]
public enum PacketFlags : byte
{
    None        = 0,
    HasError    = 1 << 0,  // 0000 0001
    Compressed  = 1 << 1,  // 0000 0010
    Encrypted   = 1 << 2,  // 0000 0100
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
public class Header(
    PacketFlags flags = PacketFlags.None,
    int msgId = 0,
    ushort msgSeq = 0,
    ushort errorCode = 0,
    int originalSize = 0)
{
    public PacketFlags Flags { get; set; } = flags;

    public int MsgId { get; set; } = msgId;          // TODO : To int or Ushort

    public ushort MsgSeq { get; set; } = msgSeq;        //   reconnect hand over

    public ushort ErrorCode { get; set; } = errorCode; //optional
    
    public int OriginalSize { get; set; } = originalSize; //optional

    // first 4byte => bodySize
    private const int FixedSize = sizeof(int)/*msg_id */ +  sizeof(int)/*total_size*/ + sizeof(byte) /*flags*/ + sizeof(ushort) /*msg_seq*/;

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
        buffer[offset] = (byte)packet.header.Flags;
        offset += 1;
            
        // msgId
        BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], packet.header.MsgId);
        offset += 4;

        // msgSeq
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], packet.header.MsgSeq);
        offset += 2;
            
        // payload
        int payloadSize = packet.message.CalculateSize();
        packet.message.WriteTo(buffer.Slice(offset, payloadSize));
        offset += payloadSize;
        return offset;
    }
}
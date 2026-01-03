using System.Buffers.Binary;
using NetMQ;
using Network.Server.Common.Memory;
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;

namespace Network.Server.Common;

public class ExternalPacket : IDisposable
{
    public Header Header { get; } = new();
    public Span<byte> Payload => _msg.Slice(_offset, PayloadLength);
    public int PayloadLength => BufferLength - _offset;

    private Msg _msg = new();
    private int _offset = 0;
    private int BufferLength { get; set; }
    public void Dispose()
    {
        if (_msg.IsInitialised)
            _msg.Close();
    }

    public void MoveMsg(ref Msg target)
    {
        target.Move(ref _msg);
    }

    public void InitBodyPool(int payloadSize, bool internalHeader = true)
    {
        var reserve = internalHeader ? InternalHeader.Size : 0;
        _msg.InitPool(reserve + payloadSize);
        _offset = reserve;
        BufferLength = reserve + payloadSize;
    }

    /// <summary>
    /// InternalPacket을 ExternalPacket으로 변환 (API Server → Gateway → Client)
    /// </summary>
    /// <param name="internalPacket">API 서버로부터 받은 내부 패킷</param>
    /// <returns>클라이언트로 전송할 외부 패킷</returns>
    public static ExternalPacket Move(InternalPacket internalPacket)
    {
        var packet = new ExternalPacket();

        // Header 정보 설정
        packet.Header.MsgId = internalPacket.MsgId;
        packet.Header.Flags = PacketFlags.None;
        // MsgSeq는 Gateway에서 세션별로 관리해야 함 (여기서는 기본값)
        packet.Header.MsgSeq = 0;

        // InternalPacket의 Msg를 ExternalPacket으로 Move (복사 없이 소유권 이전)
        internalPacket.MoveMsg(ref packet._msg);

        // InternalHeader를 건너뛰고 Payload만 참조
        packet._offset = InternalHeader.Size;
        packet.BufferLength = internalPacket.Length;  // InternalHeader.Size + Payload.Length

        // Payload는 이미 올바른 위치에 있음 (복사 불필요!)
        // InternalPacket: [InternalHeader(37 bytes)][Payload]
        // ExternalPacket: _offset=37로 설정하여 Payload만 참조
        internalPacket.Dispose();
        return packet;
    }

    public int Size => Header.GetSize() + PayloadLength;

    public int WriteToSpan(Span<byte> span)
    {
        int offset = 0;
            
        // PacketSize
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], Size);
        offset += 4;
            
        // flags
        span[offset] = (byte)Header.Flags;
        offset += 1;
            
        // msgId
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], Header.MsgId);
        offset += 4;

        // msgSeq
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], Header.MsgSeq);
        offset += 2;
        
        Payload.CopyTo(span[offset..]);
        return offset + PayloadLength;
    }
}



 public class PacketParserForClient : IPacketParser<ExternalPacket>
{
    public IReadOnlyList<ExternalPacket> Parse(ArrayPoolBufferWriter buffer)
    {
        var packets = new List<ExternalPacket>();

        while (true)
        {
            // 1. peek total_size
            var span = buffer.WrittenSpan;
            if (span.Length < 4)
                break;

            int totalSize = BinaryPrimitives.ReadInt32BigEndian(span);
            if (buffer.WrittenCount < totalSize)
                break;

            // 3. Consume actual packet using BigEndian reads
            buffer.ReadAdvance(4); 

            var packet = new ExternalPacket();

            packet.Header.Flags = (PacketFlags) buffer.ReadByte();
            packet.Header.MsgId = buffer.ReadInt32LittleEndian();
            packet.Header.MsgSeq = buffer.ReadUInt16LittleEndian();

            if (packet.Header.Flags.HasFlagFast(PacketFlags.HasError))
            {
                packet.Header.ErrorCode = buffer.ReadUInt16LittleEndian();
            }

            // InitBodyPool에서 InternalHeader 공간 확보 (Gateway → API Server 변환 시 복사 없이 Move 가능)
            if (packet.Header.Flags.HasFlagFast(PacketFlags.Compressed))
            {
                packet.Header.OriginalSize = buffer.ReadInt32LittleEndian();
                packet.InitBodyPool(packet.Header.OriginalSize, internalHeader: true);  // ✅ InternalHeader 공간 확보

                var compressSize = totalSize - packet.Header.GetSize();
                Lz4Holder.Instance.Decompress(buffer.WrittenSpan.Slice(0, compressSize), packet.Payload);
                buffer.ReadAdvance(compressSize);
                packets.Add(packet);
            }
            else
            {
                var payloadSize = totalSize - packet.Header.GetSize();
                packet.InitBodyPool(payloadSize, internalHeader: true);  // ✅ InternalHeader 공간 확보
                buffer.CopyTo(packet.Payload);
                buffer.ReadAdvance(payloadSize);
                packets.Add(packet);
            }
        }

        return packets;
    }
}
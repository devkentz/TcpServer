using System;
using System.Buffers.Binary;
using Google.Protobuf;
using Network.Server;
using Network.Server.Common.Packets;

namespace NetworkClient.Network
{
    public static class PacketExtensions
    {
        //public static int CalcSize(this (Header header, IMessage message) packet)
        //{
        //    return packet.header.GetSize() + packet.message.CalculateSize();
        //}
        
        public static int CopyTo(this (Header header, IMessage message) packet, Span<byte> buffer)
        {
            int packetSize = packet.CalcSize();
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
            int payloadSize = packetSize - packet.header.GetSize();
            packet.message.WriteTo(buffer.Slice(offset, payloadSize));
            offset += payloadSize;
            return offset;
        }
    }
}
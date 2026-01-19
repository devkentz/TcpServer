using System;
using System.Buffers;
using ProtoTestTool.ScriptContract;

// ************************************************************
// * Packet Serializer Script
// * 
// * Implement IPacketCodec to handle framing (splitting stream into packets)
// * and encoding/decoding of payloads.
// ************************************************************

public class MyPacketCodec : IPacketCodec
{
    // Common Format: [Length(4 bytes)][PacketID(4 bytes)][Protobuf Payload...]
    
    public bool TryDecode(ref ReadOnlySequence<byte> buffer, out object? message)
    {
        message = null;

        // 1. Check if we have enough bytes for at least the Header (Length = 4 bytes)
        if (buffer.Length < 4) return false;

        // 2. Read Length
        var lengthSpan = buffer.Slice(0, 4);
        Span<byte> lengthBytes = stackalloc byte[4];
        lengthSpan.CopyTo(lengthBytes);
        int packetLength = BitConverter.ToInt32(lengthBytes); // Assuming Little Endian

        // 3. Check if we have the full packet (Header + Body)
        // Note: 'packetLength' here usually refers to the Body size. Adjust if it includes header.
        // Let's assume Length covers the following bytes (MsgID + Payload).
        if (buffer.Length < 4 + packetLength) return false;

        // 4. Extract Body
        var bodySeq = buffer.Slice(4, packetLength);

        // 5. Extract MsgID (first 4 bytes of body)
        if (bodySeq.Length < 4) return false; // Should not happen if packetLength >= 4
        
        var msgIdSpan = bodySeq.Slice(0, 4);
        Span<byte> msgIdBytes = stackalloc byte[4];
        msgIdSpan.CopyTo(msgIdBytes);
        int msgId = BitConverter.ToInt32(msgIdBytes);

        // 6. Decode Protobuf Payload
        var payloadSeq = bodySeq.Slice(4); // Skip MsgID
        byte[] payloadData = payloadSeq.ToArray(); // Protobuf requires byte[] or Stream

        // TODO: Use ScriptGlobals.Registry to find Type for msgId and deserialize
        // For now, return a raw object wrapper
        message = new { MsgId = msgId, Data = payloadData };

        // 7. Advance the buffer
        buffer = buffer.Slice(4 + packetLength);
        
        return true;
    }

    public ReadOnlyMemory<byte> Encode(object message)
    {
        // Example Encode Logic
        // 1. Serialize message to byte[] (Protobuf)
        // 2. Get MsgID from Registry
        // 3. Construct [Length][MsgID][Payload]
        // return array;
        return new byte[0];
    }
}

using System;
using System.Buffers;

namespace ProtoTestTool.ScriptContract
{
    /// <summary>
    /// Responsible for converting between TCP byte streams and Message objects.
    /// This is the SINGLE point of truth for serialization/deserialization.
    /// </summary>
    public interface IPacketCodec
    {
        /// <summary>
        /// Tries to decode a message from the buffer.
        /// </summary>
        /// <param name="buffer">The incoming sequence of bytes.</param>
        /// <param name="message">The decoded message object (e.g. Google.Protobuf.IMessage).</param>
        /// <returns>True if a message was successfully decoded and consumed from the buffer.</returns>
        bool TryDecode(ref ReadOnlySequence<byte> buffer, out object? message);

        /// <summary>
        /// Encodes a message object into a byte array (including framing/length-prefix).
        /// </summary>
        /// <param name="message">The message object to encode.</param>
        /// <returns>The raw bytes to send over the wire.</returns>
        ReadOnlyMemory<byte> Encode(object message);
    } 
}

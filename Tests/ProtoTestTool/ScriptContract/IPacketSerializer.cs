using System;

namespace ProtoTestTool.ScriptContract
{
    /// <summary>
    /// Interface responsible for converting between Objects and Binary data.
    /// This should be implemented by the script to handle specific protocol logic (header parsing, etc.).
    /// </summary>
    public interface IPacketSerializer
    {
        /// <summary>
        /// Serializes a packet object into a byte array (including headers).
        /// </summary>
        /// <param name="packet">The packet object (e.g. IMessage).</param>
        /// <returns>Full binary packet.</returns>
        byte[] Serialize(object packet);

        /// <summary>
        /// Deserializes a byte buffer into a packet object.
        /// </summary>
        /// <param name="buffer">The received binary data.</param>
        /// <returns>The parsed packet object.</returns>
        object Deserialize(byte[] buffer);

        /// <summary>
        /// Returns the fixed size of the packet header, if any.
        /// Used by the network client to determine how many bytes to peek/read initially.
        /// </summary>
        int GetHeaderSize();
        
        /// <summary>
        /// Extracts the body length from the header bytes.
        /// </summary>
        /// <param name="headerBuffer">Buffer containing at least HeaderSize bytes.</param>
        /// <returns>The total size of the packet or body size depending on implementation.</returns>
        int GetTotalLength(byte[] headerBuffer);
    }
}
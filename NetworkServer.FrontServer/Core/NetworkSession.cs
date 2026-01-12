using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NetCoreServer;
using Network.Server.Common;
using Network.Server.Common.Memory;
using Network.Server.Common.Packets;
using Network.Server.Front.Actor;

namespace Network.Server.Front.Core;

/// <summary>
/// 단순화된 NetworkGatewaySession - 직접적인 Actor 참조 사용
/// </summary>
public class NetworkSession(
    long sessionId,
    ClientSocketServer clientSocketServer,
    ILogger<NetworkSession> logger)
    : TcpSession(clientSocketServer)
{
    private readonly ArrayPoolBufferWriter _buffer = new();
    private readonly ProtoPacketParser _packetParser = new();
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Create();
    public event EventHandler<EventArgs>? Connected;
    public event EventHandler<EventArgs>? Disconnected;
    
    internal Action<NetworkSession, ActorMessage>? PacketReceived;

    private IActor? _actor;

    public IActor? Actor
    {
        get => _actor;
        internal set => _actor = value;
    }

    public long SessionId => sessionId;

    public void ClientDisconnect()
    {
        base.Disconnect();
    }

    public void Send(Span<byte> packet)
    {
        base.SendAsync(packet);
    }

    protected override void OnConnecting()
    {
        logger.LogDebug($"TCP gateway OnConnected - [Gid:{SessionId}]");
        Connected?.Invoke(sessionId, EventArgs.Empty);
    }

    protected override void OnDisconnected()
    {
        logger.LogDebug($"TCP gateway OnDisConnected - [Gid:{SessionId}]");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        try
        {
            _buffer.Write(buffer.AsSpan()[(int) offset .. (int) size]);
            var packets = _packetParser.Parse(_buffer);

            foreach (var packet in packets) 
                PacketReceived?.Invoke(this, packet);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error during OnReceived");
            Disconnect();
        }
    }

    protected override void OnError(SocketError error)
    {
        logger.LogError($"socket caught an error - [codeCode:{error}]");
        Disconnect();
    }

    public void SendToClient(Header header, IMessage message)
    {
        int size = (header, message).CalcSize();
        if (size > 4096)
        {
            var heapBuffer = _arrayPool.Rent(size);
            try
            {
                var span = heapBuffer.AsSpan(0, size);
                var length = (header, message).WriteToSpan(span, size);
                base.SendAsync(span[..length]);
            }
            finally
            {
                _arrayPool.Return(heapBuffer, true);
            }
        }
        else
        {
            Span<byte> stackBuffer = stackalloc byte[size];
            var length = (header, message).WriteToSpan(stackBuffer, size);
            base.SendAsync(stackBuffer[..length]);
        }
    }
 
    /// <summary>
    /// 메시지를 Actor에게 전달 - 직접적인 참조 사용 (단순화)
    /// </summary>
    public void Push(ActorMessage message)
    {
        Debug.Assert(_actor != null);
        _actor.Push(message);
    }
}
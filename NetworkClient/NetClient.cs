using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.Pkcs;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Network.Server.Common.Memory;
using Network.Server.Common.Packets;
using NetworkClient.Config;
using NetworkClient.Network;
using NetworkServer.ProtoGenerator;
using TcpClient = NetCoreServer.TcpClient;

namespace NetworkClient
{
    public enum ClientState
    {
        Disconnected,
        Connected,
    }

    internal class TcpClientImplement(string address, int port) : TcpClient(address, port)
    {
        public event Action? OnConnectedHandler;
        public event Action? OnDisconnectedHandler;
        public event Action<SocketError>? OnErrorHandler;
        public event Action<byte[], long, long>? OnReceivedHandler;

        protected override void OnConnected()
        {
            OnConnectedHandler?.Invoke();
        }

        protected override void OnDisconnected()
        {
            OnDisconnectedHandler?.Invoke();
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            OnReceivedHandler?.Invoke(buffer, offset, size);
        }

        protected override void OnError(SocketError error)
        {
            OnErrorHandler?.Invoke(error);
        }
    }

    public sealed class NetClient : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ProtoPacketParser _packetParser;
        private readonly RpcRequestManager _rpcManager;
        private readonly NetClientConfig _config;
        private readonly ArrayPoolBufferWriter _receiveBuffer = new();
        private readonly ConcurrentQueue<NetworkPacket> _recvList = [];

        private readonly MessageHandler _handler;
        private TaskCompletionSource<bool>? _connectTcs;

        private readonly TcpClientImplement _tcpClientImplement;
        private bool _disposed  = false;
        private ushort _msgSeq = 0;

        public ClientState State => _tcpClientImplement.IsConnected ? ClientState.Connected : ClientState.Disconnected;

        public event Action? OnConnectedHandler;
        public event Action? OnDisconnectedHandler;
        public event Action<SocketError>? OnErrorHandler;

        private long Sid { get; set; }

        /// <summary>
        /// NetClient 생성자 (기본 설정 사용)
        /// </summary>
        public NetClient(string address, int port, ILogger logger, MessageHandler handler)
            : this(address, port, logger, handler, new SequentialRequestIdGenerator(), null)
        {
        }

        /// <summary>
        /// NetClient 생성자 (의존성 주입)
        /// </summary>
        public NetClient(
            string address,
            int port,
            ILogger logger,
            MessageHandler handler,
            IRequestIdGenerator? requestIdGenerator = null,
            NetClientConfig? config = null)
        {
            _logger = logger;
            _handler = handler;
            _config = config ?? new NetClientConfig();

            _packetParser = new ProtoPacketParser();
            _rpcManager = new RpcRequestManager(requestIdGenerator ?? new SequentialRequestIdGenerator(), _config.RequestTimeout);

            _tcpClientImplement = new TcpClientImplement(address, port);
            _tcpClientImplement.OptionNoDelay = _config.NoDelay;
            _tcpClientImplement.OptionKeepAlive = _config.KeepAlive;

            _tcpClientImplement.OnErrorHandler += OnError;
            _tcpClientImplement.OnConnectedHandler += OnConnected;
            _tcpClientImplement.OnDisconnectedHandler += OnDisconnected;
            _tcpClientImplement.OnReceivedHandler += OnOnReceived;
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default, int timeoutMs = 15_000)
        {
            _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeoutCts = new CancellationTokenSource(timeoutMs);

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                _tcpClientImplement.ConnectAsync();

                return await _connectTcs.Task
                    .WaitAsync(linkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("Connection timed out after {Timeout}ms", timeoutMs);
                throw new TimeoutException($"Connection timed out after {timeoutMs}ms");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Connection cancelled by user");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection failed");
                throw;
            }
            finally
            {
                _connectTcs = null;
            }
        }

        public void Disconnect()
        {
            _tcpClientImplement.Disconnect();
        }

        public void Send(IMessage packet)
        {
            if (!_tcpClientImplement.IsConnected)
            {
                _logger.LogWarning("Cannot send message - client is disconnected");
                return;
            }

            var msgId = AutoGeneratedParsers.GetIdByInstance(packet);
            if (msgId == -1)
            {
                _logger.LogError("Unknown message type: {MessageType}", packet.GetType().Name);
                return;
            }

            try
            {
                InternalSend(new Header(msgId: msgId, msgSeq: 0, requestId: 0), packet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message - connection may be lost");
            }
        }

        public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TResponse : IMessage
            where TRequest : IMessage
        {
            if (!_tcpClientImplement.IsConnected)
            {
                throw new NetClientException(
                    (ushort) InternalErrorCode.Disconnected,
                    "Cannot send request - client is disconnected",
                    request
                );
            }

            var msgId = AutoGeneratedParsers.GetIdByInstance(request);
            if (msgId == -1)
            {
                _logger.LogError("Unknown message type: {MessageType}", request.GetType().Name);
                throw new NetClientException(
                    (ushort) InternalErrorCode.InvalidMessage,
                    $"Unknown message type: {request.GetType().Name}",
                    request
                );
            }

            try
            {
                return (TResponse) await _rpcManager.SendRequestAsync(
                    requestId => InternalSend(new Header(msgId: msgId, msgSeq: _msgSeq, requestId: requestId), request),
                    cancellationToken
                );
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(
                    "Request timed out - MsgId: {MsgId}, Type: {MessageType}",
                    msgId,
                    request.GetType().Name
                );
                throw new NetClientException(
                    (ushort) InternalErrorCode.RequestTimeout,
                    $"Request timed out after {_config.RequestTimeout.TotalMilliseconds}ms - MsgId: {msgId}",
                    ex,
                    request
                );
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("RequestId collision"))
            {
                _logger.LogError(ex, "RequestId collision occurred");
                throw;
            }
        }

        public void Update()
        {
            if (State == ClientState.Disconnected)
                return;

            ProcessPacket();
        }

        private void ProcessPacket()
        {
            const int maxBatchSize = 100; // 또는 config에서
            int processed = 0;
    
            while (processed < maxBatchSize && _recvList.TryDequeue(out var packet))
            {
                _handler.Handling(packet);
                processed++;
            }
    
            if (_recvList.Count > 0)
            {
                _logger.LogDebug("Processed {Count} packets, {Remaining} remaining", 
                    processed, _recvList.Count);
            }
        }

        private void InternalSend(Header header, IMessage message)
        {
            var size = (header, message).CalcSize();
            var buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                (header, message).WriteToSpan(buffer);
        
                // ✅ SendAsync 실패 시 처리
                if (!_tcpClientImplement.SendAsync(buffer, 0, size))
                {
                    _logger.LogWarning("SendAsync returned false - send buffer may be full");
                    // 재시도 로직 또는 연결 종료 고려
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error writing message to client");
                _tcpClientImplement.Disconnect();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void OnConnected()
        {
            _logger.LogInformation("Client connected - [sid:{Sid}]", Sid);

            Sid = _tcpClientImplement.Socket.Handle.ToInt64();

            

            var tcs = Interlocked.Exchange(ref _connectTcs, null);
            tcs?.SetResult(true);
            OnConnectedHandler?.Invoke();
        }

        private void OnDisconnected()
        {
            _logger.LogInformation("TCP client disconnected - [sid:{Sid}]", Sid);

            var tcs = Interlocked.Exchange(ref _connectTcs, null);
            tcs?.SetCanceled();
            
            _rpcManager.CancelAll();
            OnDisconnectedHandler?.Invoke();
        }

        private void OnError(SocketError error)
        {
            OnErrorHandler?.Invoke(error);
            _logger.LogWarning("Socket error: {Error} [sid:{Sid}]", error, Sid);
        }

        private void OnOnReceived(byte[] buffer, long offset, long size)
        {
            try
            {
                _receiveBuffer.Write(buffer.AsSpan((int) offset, (int) size));
                var packets = _packetParser.Parse(_receiveBuffer);
                if (packets.Count == 0)
                    return;

                foreach (var packet in packets)
                {
                    // RPC 응답 처리
                    if (packet.Header.RequestId > 0)
                    {
                        if (_rpcManager.TryCompleteRequest(packet.Header.RequestId, packet.Message))
                        {
                            continue;
                        }

                        // 타임아웃된 응답 로깅
                        _logger.LogWarning(
                            "Received response for unknown/expired RequestId: {RequestId}, MsgId: {MsgId}",
                            packet.Header.RequestId,
                            packet.Header.MsgId
                        );
                    }
                    
                    _msgSeq = packet.Header.MsgSeq;

                    // 메시지 큐 크기 제한 (DoS 방어)
                    if (_recvList.Count >= _config.MaxQueueSize)
                    {
                        _logger.LogError(
                            "Message queue full ({MaxSize}), dropping packet - MsgId: {MsgId}",
                            _config.MaxQueueSize,
                            packet.Header.MsgId
                        );
                        _tcpClientImplement.Disconnect();
                        return;
                    }

                    _recvList.Enqueue(packet);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnReceived Exception");
                _tcpClientImplement.Disconnect();
            }
        }

        public void Dispose()
        {
            if(_disposed)
                return;
            // 자식 리소스 먼저 정리
            _connectTcs?.TrySetCanceled();
            _rpcManager?.Dispose();
            _receiveBuffer?.Dispose();

            // 부모 리소스 마지막에 정리
            _tcpClientImplement.Dispose();
            
            
            _disposed = true;
        }
    }
}
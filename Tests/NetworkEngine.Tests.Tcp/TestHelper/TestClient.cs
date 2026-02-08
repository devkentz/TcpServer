using System.Net.Sockets;
using Google.Protobuf;
using NetworkClient;
using NetworkClient.Network;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node.TestHelper;

public sealed class TestClient : IDisposable
{
    private readonly NetClient _client;
    private bool _disposed = false;
    private readonly ILogger<TestClient> _logger;

    public TestClient(string address, int port, ITestOutputHelper? output = null)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            if (output != null)
            {
                builder.AddProvider(new XunitLoggerProvider(output)); // ✅ 확장 메서드
            }
            else
            {
                builder.AddConsole();
            }
        });
        
        _logger = loggerFactory.CreateLogger<TestClient>();

        _client = new NetClient(address, port, _logger, new MessageHandler());

        _client.OnConnectedHandler += OnConnected;
        _client.OnDisconnectedHandler += OnOnDisconnected;
        _client.OnErrorHandler += OnError;
    }

    private void OnError(SocketError obj)
    {   
        _logger.LogError(obj.ToString());
    }

    private void OnConnected()
    {
        _logger.LogInformation("Connected");
    }

    private void OnOnDisconnected()
    {
        _logger.LogInformation("Disconnected");
    }

    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage
    {
        return _client.RequestAsync<TRequest, TResponse>(request, cancellationToken);
    }

    public Task<bool> ConnectAsync()
    {
        return _client.ConnectAsync();
    }
    
    public void Disconnect() => _client.Disconnect();

    public void Update() => _client.Update();
    
    public void Dispose()
    {
        if (_disposed)
            return;

        _client.Dispose();
        _disposed = true;
    }
}
using Microsoft.Extensions.Options;
using Network.Server.Tcp.Config;
using Network.Server.Tcp.Extensions;
using NetworkClient.Network;
using NetworkEngine.Tests.Node.ServerTest.Controller;
using NetworkEngine.Tests.Node.ServerTest.Handler;
using Serilog;
using Serilog.Events;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node.TestHelper;

public class TestObjectFactory : IAsyncDisposable
{
    private readonly List<IHost> _hosts = [];
    
    public record TcpConnectionInfo(string Address, int Port);

    public record TestServerRecord(IHost Host, TcpConnectionInfo ConnectionInfo)
    {
        public Task StartAsync() => Host.StartAsync();
        public Task StopAsync() => Host.StopAsync();
    }

    public TestClient CreateTestClient(TcpConnectionInfo connectionInfo, ITestOutputHelper? output = null)
    {
        return new TestClient(connectionInfo.Address, connectionInfo.Port, output);
    }
    
    public async Task<TestServerRecord> CreateNodeAsync(ITestOutputHelper? output = null)
    {

        try
        {
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseServiceProviderFactory(new DefaultServiceProviderFactory(new ServiceProviderOptions
                {
                    ValidateOnBuild = true // 빌드 시 모든 싱글톤 즉시 생성
                }))
                .UseSerilog((_, _, configuration) =>
                {
                    configuration.MinimumLevel.Debug();

                    // xUnit 출력으로 로그 전송
                    if (output != null)
                    {
                        configuration.WriteTo.TestOutput(output, LogEventLevel.Debug,
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    // TcpServer 및 관련 Core 서비스 등록 (확장 메서드 사용)
                    services.AddTcpServer<ConnectionHandler>(context.Configuration);
                    services.AddSingleton<TestController>();

                    services.Configure<TcpServerConfig>(options =>
                    {
                        options.Address = "127.0.0.1";
                        options.Port = 5050;
                    });
                });

            var host = await hostBuilder.StartAsync();
            _hosts.Add(host);
            
            
            var option =  host.Services.GetRequiredService<IOptions<TcpServerConfig>>().Value;
            return new TestServerRecord(host, new TcpConnectionInfo(option.Address, option.Port));
        }
        catch (Exception e)
        {
            output?.WriteLine(e.ToString());
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }

        _hosts.Clear();
    }
}
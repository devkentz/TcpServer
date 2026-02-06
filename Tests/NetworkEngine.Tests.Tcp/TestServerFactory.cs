using Network.Server.Common;
using Network.Server.Common.Utils;
using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Config;
using Network.Server.Tcp.Core;
using Network.Server.Tcp.Extensions;
using Network.Server.Generated;
using NetworkEngine.Tests.Node.Controller;
using Serilog;
using Serilog.Events;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node;

public class TestServerFactory : IAsyncDisposable
{
    private readonly List<IHost> _hosts = [];


    public async Task<IHost> CreateNodeAsync(ITestOutputHelper? output = null)
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
                    services.AddTcpServer<InGameConnectionQueue>(context.Configuration);
                    services.AddSingleton<TestController>();

                    services.Configure<TcpServerConfig>(options =>
                    {
                        options.Address = "127.0.0.1";
                        options.Port = 5050;
                    });
                });

            var host = await hostBuilder.StartAsync();
            //var server = host.Services.GetRequiredService<TcpServer>();
            _hosts.Add(host);

            return host;
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
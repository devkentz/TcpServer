using Internal.Protocol;
using Network.Server.Common;
using Network.Server.Common.Packets;
using NetworkServer.ProtoGenerator;
using Proto.Test;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node;

public class NodeStressTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private NodeTestServerFactory _factory;
    private string _redisConnectionString;

    public NodeStressTests(ITestOutputHelper output)
    {
        _output = output;
        _redisConnectionString = "redis-dev.k8s.home:6379";
        _factory = new NodeTestServerFactory(_redisConnectionString);
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
    
    
    private async Task ClearNodesAsync()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = {_redisConnectionString}, // Redis 서버의 주소와 포트
        });

        await redis.GetDatabase().KeyDeleteAsync("test-servers");
    }


    [Fact]
    public async Task Stress_Test_PingPong_For_Memory_Profiling()
    {
        await ClearNodesAsync();
        // 반복 횟수 설정 (프로파일링을 위해 충분히 길게 설정)
        // dotMemory 실행 시 이 테스트를 선택해서 실행하세요.
        const int iterations = 100_000;

        _output.WriteLine($"Starting Stress Test with {iterations} iterations...");

        // 1. Receiver Node (Node-2) 설정
        var node2 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-2", _output);
        await node2.Node.StartAsync();

        await Task.Delay(2000);
        
        
        // 2. Sender Node (Node-1) 설정
        var node1 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-1", _output);
        await node1.Node.StartAsync();

        // 연결 대기 (Eventual Consistency 고려)

        // 3. 반복 송수신
        var req = new EchoReq() {Message = "StressTestPayload"};

        for (int i = 0; i < iterations; i++)
        {
            try
            {
                var index = i;
                
                _ = Task.Run(async () =>
                {
                    var response = await node1.Sender.RequestApiAsync<EchoReq, EchoRes>("node-2", req);
                    if (index % 1000 == 0)
                    {
                        _output.WriteLine($"Iteration {index}: Processed. Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
                    }

                    if (index % 10000 == 0)
                    {
                        _output.WriteLine(response.Message);
                    }
                
                });
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error at iteration {i}: {ex.Message}");
                // 연결 끊김 등으로 실패하면 잠시 대기 후 재시도
                await Task.Delay(100);
            }
        }

        _output.WriteLine("Stress Test Finished.");

        foreach (var i in Enumerable.Range(0, 50))
        {
            await Task.Delay(1000);
        }
        
        // 마지막으로 GC 수행 후 메모리 상태 확인용 대기
        GC.Collect();
        await Task.Delay(1000);
    }
}
using Internal.Protocol;
using Network.Server.Common;
using Network.Server.Common.Packets;
using Network.Server.Node.Core;
using NetworkServer.ProtoGenerator;
using Proto.Test;
using Xunit;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node;


[NodeController]
public class StopTestController(IApplicationStopper stopper, ILogger<StopTestController> logger)
{
    [PacketHandler(StopReq.MsgId)]
    public Task<StopRes> StopHandler(StopReq req)
    {
        logger.LogInformation("NodeService stopped.");
        stopper.StopApplication();
        
        return Task.FromResult(new StopRes());
    }
}

public class StopTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private NodeTestServerFactory _factory;
    private string _redisConnectionString;

    public StopTests(ITestOutputHelper output)
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

    [Fact]
    public async Task Stress_Test_PingPong_For_Memory_Profiling()
    {
        // 반복 횟수 설정 (프로파일링을 위해 충분히 길게 설정)
        // dotMemory 실행 시 이 테스트를 선택해서 실행하세요.
        const int iterations = 100_000;

        _output.WriteLine($"Starting Stress Test with {iterations} iterations...");

        // 1. Receiver Node (Node-2) 설정
        var node2 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-2", _output);
        await node2.Node.StartAsync();

        await Task.Delay(5000);
        
        
        // 2. Sender Node (Node-1) 설정
        var node1 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-1", _output);
        await node1.Node.StartAsync();



        var res =  await node1.Sender.RequestApiAsync<StopReq, StopRes>(InternalPacket.ServerActorId,  "node-2", new StopReq());
        
        

        _output.WriteLine("Stress Test Finished.");

        // 마지막으로 GC 수행 후 메모리 상태 확인용 대기
        GC.Collect();
        await Task.Delay(1000);
    }
}
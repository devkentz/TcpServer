using Internal.Protocol;
using Network.Server.Common.Packets;
using Proto.Test;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;


namespace NetworkEngine.Tests.Node;

public class NodeBasicTests(ITestOutputHelper output) : IAsyncLifetime
{
    private NodeTestServerFactory _factory = null!;

    private string _redisConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _redisConnectionString = Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION") ?? "redis-dev.k8s.home:6379";
        _factory = new NodeTestServerFactory(_redisConnectionString); // ✅ output 전달

        await Task.CompletedTask;
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
    public async Task Should_Register_Node_Successfully()
    {
        try
        {
            await ClearNodesAsync();

            var node1 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-1", output);
            var node2 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-2", output);
            var node3 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-2", output);
            var node4 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-2", output);


            await node1.Node.StartAsync();
            await node2.Node.StartAsync();
            await node3.Node.StartAsync();
            await node4.Node.StartAsync();


            // 노드 시작 후 잠시 대기
            await Task.Delay(5000);


            output.WriteLine($"apiInfo :  {System.Text.Json.JsonSerializer.Serialize(node1.NodeManager.GetApiInfo("node-2"))}");
            output.WriteLine($"apiInfo :  {System.Text.Json.JsonSerializer.Serialize(node2.NodeManager.GetApiInfo("node-1"))}");

            foreach (var idx in Enumerable.Range(0, 10))
            {
                try
                {
                    var res = await node1.Sender.RequestApiAsync<EchoReq, EchoRes>(
                        InternalPacket.ServerActorId,
                        "node-2", new EchoReq
                        {
                            Message = $"Test:{idx}",
                        });

                    output.WriteLine(res.Message);
                }
                catch (Exception e)
                {
                    output.WriteLine(e.Message);
                }
            }

            var node5 = await _factory.CreateNodeAsync(EServerType.SubApi, "node-2", output: output);

            await node5.Node.StartAsync();

            foreach (var idx in Enumerable.Range(0, 10))
            {
                try
                {
                    var res = await node1.Sender.RequestApiAsync<EchoReq, EchoRes>(
                        InternalPacket.ServerActorId,
                        "node-2", new EchoReq
                        {
                            Message = $"Test:{idx}",
                        });

                    output.WriteLine(res.Message);
                }
                catch (Exception e)
                {
                    output.WriteLine(e.Message);
                }
            }

            node3.Node.Stop();
            await Task.Delay(10_000);

            foreach (var idx in Enumerable.Range(0, 10))
            {
                var res = await node1.Sender.RequestApiAsync<EchoReq, EchoRes>(
                    InternalPacket.ServerActorId,
                    "node-2", new EchoReq
                    {
                        Message = $"Test:{idx}",
                    });

                output.WriteLine(res.Message);
            }

            output.WriteLine("wait Start");

            foreach (var _ in Enumerable.Range(0, 10))
            {
                await Task.Delay(1_000);
            }


            output.WriteLine("Dispose Start");

            node1.Dispose();
            node2.Dispose();
            node3.Dispose();
            node4.Dispose();
            node5.Dispose();
        }
        catch (Exception e)
        {
            output.WriteLine(e.ToString());
        }
    }
}
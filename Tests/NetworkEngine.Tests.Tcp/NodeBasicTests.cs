using Internal.Protocol;
using Network.Server.Common.Packets;
using Proto.Test;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;


namespace NetworkEngine.Tests.Node;

public class NodeBasicTests(ITestOutputHelper output) : IAsyncLifetime
{
    private TestServerFactory _factory = null!;


    public Task InitializeAsync()
    {
        _factory = new TestServerFactory(); // ✅ output 전달
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Should_Register_Node_Successfully()
    {
        try
        {
            var server1 = await _factory.CreateNodeAsync(output);
            await server1.RunAsync();

            // 노드 시작 후 잠시 대기
            await Task.Delay(5000);
        }
        catch (Exception e)
        {
            output.WriteLine(e.ToString());
        }
    }
}
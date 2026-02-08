using NetworkEngine.Tests.Node.TestHelper;
using Proto.Test;
using Xunit;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node.ServerTest;

public class ServerBasicTests(ITestOutputHelper output) : IAsyncLifetime
{
    private TestObjectFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new TestObjectFactory(); // ✅ output 전달
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task DefaultEchoTest()
    {
        var server = await _factory.CreateNodeAsync(output);
        await server.StartAsync();

        var test = _factory.CreateTestClient(server.ConnectionInfo, output);

        async Task RunUpdateLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    test.Update();
                    await Task.Delay(16, token);
                }
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
        }

        var cts = new CancellationTokenSource();
        var updateTask = RunUpdateLoop(cts.Token);


        await test.ConnectAsync();

        var loginGameRes = await test.RequestAsync<LoginGameReq, LoginGameRes>(new LoginGameReq
        {
            ExternalId = "Test"
        }, cts.Token);


        if (!loginGameRes.Success)
            throw new Exception("Test failed");

        foreach (var i in Enumerable.Range(0, 10))
        {
            var echoRes = await test.RequestAsync<EchoReq, EchoRes>(new EchoReq
            {
                Message = $"echo-echo-echo {i+1}"
            }, cts.Token);

            output.WriteLine($"EchoRes : {echoRes.Message}");
        }

        await cts.CancelAsync();
        await updateTask;
        await server.StopAsync();
    }
}


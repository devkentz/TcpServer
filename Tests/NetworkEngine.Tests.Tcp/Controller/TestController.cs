using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Core;
using Proto.Test;

namespace NetworkEngine.Tests.Node.Controller
{
    [ServerController]
    public class TestController(ILogger<TestController> logger)
    {
        [PacketHandler(EchoReq.MsgId)]
        public Task<Response> EchoHandler(IActor actor, EchoReq req)
        {
            logger.LogWarning($"Echo req: {req}");

            return Task.FromResult(Response.Ok(new EchoRes
            {
                Message = req.Message + "_" + "ANSWER",
                Timestamp = 0
            }));
        }
    }
}
using Microsoft.Extensions.Logging;
using Network.Server.Front.Actor;
using Network.Server.Front.Core;
using Proto.Test;

namespace NetworkServer.Sample.Controllers;

/// <summary>
/// 샘플 패킷 핸들러 컨트롤러
/// </summary>
[ServerController]
public class SampleController(ILogger<SampleController> logger)
{
    /// <summary>
    /// Echo 메시지 핸들러
    /// 클라이언트로부터 받은 메시지를 그대로 응답
    /// </summary>
    [PacketHandler(EchoReq.MsgId)]
    public Task<Response> EchoHandler(IActor actor, EchoReq req)
    {
        logger.LogInformation("Echo 요청 받음: {Message}", req.Message);

        return Task.FromResult(Response.Ok(new EchoRes
        {
            Message = $"{req.Message} (서버 응답)",
            Timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));
    }

    /// <summary>
    /// 클라이언트 연결 시 호출되는 핸들러 (예시)
    /// </summary>
    public void OnClientConnected(IActor actor)
    {
        logger.LogInformation("클라이언트 연결됨: Actor ID = {ActorId}", actor.ActorId);
    }

    /// <summary>
    /// 클라이언트 연결 해제 시 호출되는 핸들러 (예시)
    /// </summary>
    public void OnClientDisconnected(IActor actor)
    {
        logger.LogInformation("클라이언트 연결 해제됨: Actor ID = {ActorId}", actor.ActorId);
    }
}

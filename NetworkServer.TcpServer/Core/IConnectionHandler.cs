using Network.Server.Tcp.Actor;

namespace Network.Server.Tcp.Core;

/// <summary>
/// 인게임 접속 요청을 큐에 추가하고 비동기로 처리하는 인터페이스
/// </summary>
public interface IConnectionHandler
{
    /// <summary>
    /// 인게임 접속 요청을 큐에 추가합니다
    /// </summary>
    /// <param name="session">네트워크 세션</param>
    /// <param name="message">접속 패킷</param>
    /// <returns>큐 추가 성공 여부</returns>
    void EnqueueAsync(NetworkSession session, ActorMessage message);
    
    /// <summary>
    /// 인게임 접속 패킷 타입인지 확인합니다
    /// </summary>
    /// <param name="msgId">메시지 ID</param>
    /// <returns>인게임 접속 패킷 여부</returns>
    bool IsInGameConnectionPacket(int msgId);
}
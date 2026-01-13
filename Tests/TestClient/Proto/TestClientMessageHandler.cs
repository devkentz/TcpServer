using Google.Protobuf;
using NetworkClient.Network;

namespace TestClient.Proto;

public class TestClientMessageHandler : MessageHandler
{
	public Action<IMessage>? OnMessageReceived { get; set; }

	protected override void LoadHandlers()
	{
		// TestClient는 모든 메시지를 범용으로 처리
		// 필요시 특정 메시지 타입별 핸들러 추가 가능
	}

	public new void Handling(NetworkPacket packet)
	{
		OnMessageReceived?.Invoke(packet.Message);
	}
	
}

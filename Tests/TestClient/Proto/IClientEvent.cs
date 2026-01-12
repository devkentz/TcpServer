namespace TestClient.Proto;

public interface IClientEvent 
{
	void PrintLog(string message);
	void OnConnect(bool result);
	void OnDisconnect();
	void SetUserInfo(object o);
}
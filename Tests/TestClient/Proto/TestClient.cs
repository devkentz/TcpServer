using Google.Protobuf;
using Newtonsoft.Json;
using PlayHouseConnector;
using Proto;

namespace TestClient.Proto;

public class TestClient
{
	private readonly IClientEvent _clientEvent;
	private readonly Connector _connector = new();

	public TestClient(IClientEvent clientEvent)
	{
		_clientEvent = clientEvent;
		_connector.OnReceive += OnReceive;
		_connector.OnError += OnError;
		_connector.OnConnect += OnConnect;
		_connector.OnDisconnect += OnDisconnect;
	}

	private void OnDisconnect()
	{
		_clientEvent.OnDisconnect();
	}

	private void OnConnect(bool result)
	{
		_clientEvent.OnConnect(result);
	}

	private void OnError(ushort serviceId, ushort errorCode, IMessage packet)
	{
		_clientEvent.PrintLog($"Error:{errorCode} {JsonConvert.SerializeObject(packet, Formatting.Indented)}");
	}

	public async Task<bool> ConnectAsync(string ip, int port)
	{
		_connector.Init(new ConnectorConfig
		{
			RequestTimeoutMs = 30000,
			Host = ip,
			Port = port,
			HeartBeatIntervalMs = 0,
			ConnectionIdleTimeoutMs = 30000
		});

		return await _connector.ConnectAsync();
	}


	private void OnReceive(ushort serviceId, IMessage packet)
	{
		var multilineText = JsonConvert.SerializeObject(packet, Formatting.Indented);
		_clientEvent.PrintLog($"{packet.Descriptor.Name} {Environment.NewLine} {multilineText}");
	}

	public void Send(ushort serviceId, IMessage message)
	{
		_connector.Send(serviceId, message);
	}


	public void Update()
	{
		_connector.MainThreadAction();
	}

	public bool IsConnected() => _connector.IsConnect();


	public async Task<TResponse> RequestAsync<TResponse>(ushort serviceId, IMessage request, long stageId = 0)
		where TResponse : IMessage, new()
	{
 
		return await _connector.RequestAsync<TResponse>(serviceId, request, stageId);
	}

	public async Task<IMessage> RequestAsync(ushort serviceId, IMessage request, long stageId = 0)
	{
		return await _connector.RequestAsync(serviceId, request, stageId);
	}


	public async Task<bool> LoginAsync(string platformUid, EAccountPlatform platform, string token, ERegionCode regionCode)
	{
		int retryCount = 0;
		const int maxRetries = 10;
		const int retryIntervalMilliseconds = 1000; // 3초 간격

		while (true)
		{
			var accessQueueStatusCheckRes = await _connector.RequestAsync<Session_AccessQueueStatusCheckRes>(0,
				new Session_AccessQueueStatusCheckReq
				{
					PlatformUid = platformUid,
					RegionCode = regionCode,
				}
			);


			if (accessQueueStatusCheckRes.Result == EAccessQueueStatusResult.Ok)
			{
				_clientEvent.PrintLog($"accessQueueStatusCheck complete - [uuid:{platformUid}]");
				break;
			}


			retryCount++;

			if (retryCount >= maxRetries)
			{
				_clientEvent.PrintLog($"accessQueueStatusCheck Fail after {retryCount} retries - [uuid:{platformUid}]");
				return false;
			}

			_clientEvent.PrintLog( $"accessQueueStatusCheck not ready. Retrying {retryCount}/{maxRetries} - [uuid:{platformUid}]");
			await Task.Delay(retryIntervalMilliseconds);
		}


		var authenticateRes = await _connector.AuthenticateAsync<Account_AuthenticateRes>(1, (
			new Account_AuthenticateReq
			{
				//  Version = 1.0f,
				PlatformUid = platformUid,
				Token = string.Empty,
				PlatformCode = platform,
				AppMarket = EAppMarket.Google,
				OsType = EPlatform.Editor,
				Lang = "ko",
				Country = "KR", 
				RegionCode = regionCode,
			}
		));


		if (authenticateRes.Result != EAuthenticateResult.Ok)
		{
			_clientEvent.PrintLog( $"authenticate Fail - [uuid:{platformUid} Reason :{authenticateRes.Result}");
			return false;
		}

		_clientEvent.PrintLog($"authenticate complete - [uuid:{platformUid},accountId:{authenticateRes.AccountId}]");
		var playerData = await _connector.RequestAsync<Player_DataRes>(1, new Player_DataReq());

		_clientEvent.SetUserInfo(new { UID = playerData.PlayerData.Player.AccountId, NickName = playerData.PlayerData.Player.Nickname });
		return true;
	}

	public void Disconnect() => _connector.Disconnect();
}
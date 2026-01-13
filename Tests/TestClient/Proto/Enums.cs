namespace Proto;

public enum EAccountPlatform
{
	None = 0,
	Google = 1,
	Apple = 2,
	Facebook = 3,
	Guest = 4,
}

public enum ERegionCode
{
	None = 0,
	KR = 1,
	US = 2,
	JP = 3,
	CN = 4,
}

public enum EAppMarket
{
	None = 0,
	Google = 1,
	Apple = 2,
}

public enum EPlatform
{
	None = 0,
	Editor = 1,
	Android = 2,
	iOS = 3,
	Windows = 4,
}

public enum EAuthenticateResult
{
	Ok = 0,
	TokenInvalid = 1,
	AccountBanned = 2,
	ServerMaintenance = 3,
	InternalError = 4,
}

public enum EAccessQueueStatusResult
{
	Ok = 0,
	Waiting = 1,
	Failed = 2,
}

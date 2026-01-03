using Internal.Protocol;

namespace Network.Server.Node.Network;

public class RemoteNode(ServerInfo serverInfo)
{
    public ServerInfo ServerInfo => serverInfo;
    public EServerType ServerType => ServerInfo.Type;
    public long Identity => ServerInfo.RemoteId;
    public byte[] IdentityBytes { get; } = serverInfo.IdentityBytes.Span.ToArray();
    public string Address => serverInfo.Address;
    public int Port => serverInfo.Port;
    
    
    /// <summary>
    /// Api 이름 (예: "Chat", "Game", "User")
    /// </summary>
    public string? SubApiName => serverInfo.SubApiName;

    /// <summary>
    /// SubApi의 Sticky 타입 (SubApi 서버만 해당)
    /// </summary>
    public SubApiStickyType StickyType => ServerInfo.StickyType;

    /// <summary>
    /// 서비스 식별자 (Gateway/MainApi는 타입명, SubApi는 SubApiName)
    /// </summary>
    public string ApiName => ServerType switch
    {
        EServerType.Gateway => "Gateway",
        EServerType.MainApi => "MainApi",
        EServerType.SubApi => SubApiName ?? "Unknown",
        _ => "Unknown"
    };

    public void ConnectionClosed() => IsClose =  true;

    public bool IsClose { get; private set; }
}
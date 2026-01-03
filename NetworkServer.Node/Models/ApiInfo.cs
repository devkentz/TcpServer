using Internal.Protocol;

namespace Network.Server.Node.Models;

public class ApiInfo
{
    public string ApiName { get; set; } = string.Empty;
    public EServerType ServerType { get; set; }
    public SubApiStickyType StickyType { get; set; }
}
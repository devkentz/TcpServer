namespace Network.Server.Front.Config;

public class FrontServerConfig
{
    public int OptionAcceptorBacklog { get; set; }
    public bool OptionKeepAlive { get; set; }
    public bool OptionNoDelay { get; set; }
    public bool OptionReuseAddress { get; set; }
    public int OptionSendBufferSize { get; set; }
    public int OptionReceiveBufferSize { get; set; }

    public int LoginConcurrentSize { get; set; } = 500;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
}
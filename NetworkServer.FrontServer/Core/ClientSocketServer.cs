using NetCoreServer;
using Network.Server.Front.Config;

namespace Network.Server.Front.Core;

public class ClientSocketServer : TcpServer
{ 
    private readonly Func<ClientSocketServer, TcpSession> _createFactory;

    public ClientSocketServer(Func<ClientSocketServer, TcpSession> createFactory, FrontServerConfig config)
        : base(config.Address, config.Port)
    {
        _createFactory = createFactory;

        OptionAcceptorBacklog = config.OptionAcceptorBacklog;
        OptionKeepAlive = config.OptionKeepAlive;
        OptionNoDelay = config.OptionNoDelay;
        OptionReuseAddress = config.OptionReuseAddress;
        OptionReceiveBufferSize = config.OptionReceiveBufferSize;
        OptionSendBufferSize = config.OptionSendBufferSize;
    }
    
    protected override TcpSession CreateSession()
    {
        return _createFactory(this);
    }
}
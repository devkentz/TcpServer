using Network.Server.Common;
using Network.Server.Common.Packets;
using Network.Server.Node.Core;
using Network.Server.Node.Utils;

namespace Network.Server.Node.Network;

public class NodePacketHandler
{
    private readonly RequestCache<InternalPacket> _requestCache;
    private readonly NodeEventController _controller;

    public NodePacketHandler(RequestCache<InternalPacket> requestCache, NodeEventController controller, NodeCommunicator nodeCommunicator)
    {
        _requestCache = requestCache;
        _controller = controller;
        
        nodeCommunicator.OnProcessPacket = OnProcessPacket;
        nodeCommunicator.OnSendFailed = OnSendFailed;
    }

    public void OnProcessPacket(InternalPacket packet)
    {
        if (packet.IsReply)
        {
            _requestCache.TryReply(packet.RequestKey, packet);
        }
        else
        {
            _controller.OnPacket(packet);
        }
    }

    public void OnSendFailed(int requestKey, Exception ex) =>
        _requestCache.TryFail(requestKey, ex);
}
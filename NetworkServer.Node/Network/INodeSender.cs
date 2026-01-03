using Google.Protobuf;
using Network.Server.Common.Packets;
using Network.Server.Node.Core;

namespace Network.Server.Node.Network;

public interface INodeSender
{
    public Task<TResponse> RequestApiAsync<TRequest, TResponse>(long actorId, string apiName,
        TRequest request)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : IMessage<TResponse>, new();

    public Task<TResponse> RequestApiAsync<TRequest, TResponse>(string apiName,
        TRequest request)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : IMessage<TResponse>, new();
    
    public Task<TResponse> RequestAsync<TRequest, TResponse>(long actorId, long nodeId, TRequest request)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : IMessage<TResponse>, new();
}

public interface INodeResponser
{
    public void Response(InternalHeader requestHeader, IMessage responseMessage);
}
                        

public class NodeResponser(INodeManager nodeManager, NodeCommunicator nodeCommunicator) : INodeResponser
{
    public void Response(InternalHeader requestHeader, IMessage responseMessage)
    {
        var response = InternalPacket.CreateResponse(requestHeader.ActorId, requestHeader, responseMessage);

        if (nodeManager.FindRemoteKey(response.Dest) is not { } remoteKey)
            throw new Exception($"Remote key not found for nodeId: {response.Dest}");

        nodeCommunicator.Send(remoteKey, response);
    }
}

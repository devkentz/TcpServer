using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Network.Server.Common;
using Network.Server.Common.Packets;
using Network.Server.Common.Utils;
using Network.Server.Node.Cluster;
using Network.Server.Node.Config;
using Network.Server.Node.Core;
using Network.Server.Node.Network;
using Network.Server.Node.Utils;
using StackExchange.Redis;

namespace Network.Server.Node.Extensions;

/// <summary>
/// Node 관련 확장 메서드
/// </summary> 
public static class NodeExtensions
{
    /// <summary>
    /// Node 서비스를 등록합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configuration">설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection UseNode(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NodeConfig>(configuration.GetSection("Node"));
        
        // 새로운 Cluster Registry 추상화 등록
        services.AddSingleton<IClusterRegistry, RedisClusterRegistry>();
        
        // 애플리케이션 종료 처리기 등록
        services.AddSingleton<IApplicationStopper, HostApplicationStopper>();
        
        services.AddSingleton<INodeManager, NodeManager>();
        services.AddSingleton<NodeService>();
        services.AddSingleton<UniqueIdGenerator>();
        services.AddSingleton<NodePacketHandler>();
        services.AddSingleton(sp => 
        {
            var config = sp.GetRequiredService<IOptions<NodeConfig>>().Value;
            return new RequestCache<InternalPacket>(config.RequestTimeoutMs);
        });
        services.AddSingleton<INodeSender, NodeSender>();
 
        return services;
    }
    
    public static IServiceCollection AddNodeController<T>(this IServiceCollection services) where T : class
    {
        services.AddScoped<T>();
        return services;
    }
}
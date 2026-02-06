using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Network.Server.Common;
using Network.Server.Common.Utils;
using Network.Server.Tcp.Actor;
using Network.Server.Tcp.Config;
using Network.Server.Tcp.Core;

namespace Network.Server.Tcp.Extensions;

public static class TcpServerExtensions
{
    public static IServiceCollection AddTcpServer<TConnectionHandler>(this IServiceCollection services, IConfiguration configuration)
        where TConnectionHandler : class, IConnectionHandler
    {
        services.Configure<TcpServerConfig>(configuration.GetSection("TcpServerConfig"));
        
        // Infrastructure Services
        services.AddSingleton<UniqueIdGenerator>(_ => new UniqueIdGenerator(Guid.NewGuid()));
        services.AddSingleton<IApplicationStopper, HostApplicationStopper>();
        services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        
        // Core Services
        services.AddSingleton<IActorManager, ActorManager>();
        services.AddSingleton<IConnectionHandler, TConnectionHandler>();
        
        // Hosted Service
        services.AddHostedService<TcpServer>();
        
        // Auto-register Generated MessageHandler
        RegisterGeneratedMessageHandler(services);
        
        return services;
    }

    private static void RegisterGeneratedMessageHandler(IServiceCollection services)
    {
        // AppDomain의 모든 로드된 어셈블리에서 검색 (테스트 환경 호환성 확보)
        var handlerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => 
            {
                try 
                { 
                    // 동적 어셈블리나 접근 불가능한 타입 제외
                    return a.GetTypes(); 
                } 
                catch 
                { 
                    return Type.EmptyTypes; 
                }
            })
            .FirstOrDefault(t => t.FullName == "Network.Server.Generated.GeneratedMessageHandler");

        if (handlerType != null)
        {
            services.AddSingleton(typeof(MessageHandler), provider =>
            {
                var handler = Activator.CreateInstance(handlerType);
                if (handler == null) throw new InvalidOperationException("Failed to create GeneratedMessageHandler");
                
                // Initialize() 호출
                var initMethod = handlerType.GetMethod("Initialize");
                initMethod?.Invoke(handler, null);
                
                return handler;
            });
        }
        else
        {
            // 경고: 핸들러를 찾을 수 없음. 
            // (라이브러리에서 Console 로그는 권장되지 않으나, 초기화 단계의 중요 오류이므로 출력)
            Console.WriteLine("[Warning] 'Network.Server.Generated.GeneratedMessageHandler' not found. Packet handlers are not registered.");
        }
    }
}

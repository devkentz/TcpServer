using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Network.Server.Common.Utils;
using Network.Server.Front.Actor;
using Network.Server.Front.Config;

namespace Network.Server.Front.Core
{
    public class FrontServer : AbstractServer
    {
        private readonly ClientSocketServer _clientSocketServer;
        private readonly UniqueIdGenerator _idGenerator;
        private readonly IInGameConnectionQueue _inGameConnectionQueue;

        public FrontServer(
            IOptions<FrontServerConfig> serverConfig,
            IActorManager actorManager,
            ILogger<FrontServer> logger,
            IServiceProvider serviceProvider,
            UniqueIdGenerator idGenerator,
            IInGameConnectionQueue inGameConnectionQueue)
            : base(actorManager, logger, serviceProvider)
        {
            _idGenerator = idGenerator;
            _inGameConnectionQueue = inGameConnectionQueue;
            _clientSocketServer = new ClientSocketServer(CreateSession, serverConfig.Value);
        }

        private NetworkSession CreateSession(ClientSocketServer clientSocketServer)
        {
            var sessionLogger = ServiceProvider.GetRequiredService<ILogger<NetworkSession>>();
            return new NetworkSession(_idGenerator.NextId(), clientSocketServer, sessionLogger) {PacketReceived = PacketFromClient };
        }

        /// <summary>
        /// Handles incoming packets from a client session.
        /// </summary>
        /// <param name="session">The network session from which the packet was received.</param>
        /// <param name="message">The packet received from the client.</param>
        private void PacketFromClient(NetworkSession session, ActorMessage message)
        {
            if (session.Actor == null)
            {
                if (!_inGameConnectionQueue.IsInGameConnectionPacket(message.Header.MsgId))
                {
                    Logger.LogWarning("Unauthorized packet received from session {SessionId}, MsgId: {MsgId}",
                        session.SessionId, message.Header.MsgId);

                    session.Disconnect();
                    return;
                }

                _inGameConnectionQueue.EnqueueAsync(session, message);
                return;
            }

            session.Push(message);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _clientSocketServer.Start();
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _clientSocketServer.Stop();
            await base.StopAsync(cancellationToken);
        }
    }
}
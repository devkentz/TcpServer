using Internal.Protocol;
using Network.Server.Common.Utils;

namespace Network.Server.Node.Config
{
    public class NodeConfig
    {
        public NodeConfig()
        {
            // 생성자에서 초기 NodeId 계산
            _nodeId = HashHelper.XxHash64(_nodeGuid.ToByteArray());
        }
        
        public string RedisConnectionString { get; set; } = string.Empty;
        public string ServerRegistryKey { get; set; } = string.Empty;
        public EServerType ServerType { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string? SubApiName { get; set; }
        public SubApiStickyType StickyType { get; set; } = SubApiStickyType.NoneSticky;

        // Timeout & Interval Settings (Default values)
        public int HeartBeatIntervalSeconds { get; set; } = 5;
        public int HeartBeatTtlSeconds { get; set; } = 15;
        public int IdentityExchangeDelayMs { get; set; } = 50;
        public int HandShakeTimeoutMs { get; set; } = 5000;
        public int MaxHandShakeRetries { get; set; } = 3;
        public int RequestTimeoutMs { get; set; } = 5000;

        public Guid NodeGuid
        {
            get => _nodeGuid;
            set
            {
                _nodeGuid = value;
                _nodeId = HashHelper.XxHash64(value.ToByteArray());
            }
        }

        private Guid _nodeGuid;
        
        public long NodeId => _nodeId;
        private long _nodeId = 0;
    }
}
# NetworkEngine 클래스 다이어그램

## 전체 아키텍처 개요

```mermaid
graph TB
    subgraph "Application Layer"
        App[Application/Host]
    end

    subgraph "Core Layer - NetworkServer.Node"
        NS[NodeService]
        NC[NodeCommunicator]
        NPH[NodePacketHandler]
        NEC[NodeEventController]
        AM[ActorManager]
        Actor[Actor]
    end

    subgraph "Infrastructure Layer - NetworkServer.Common"
        IP[InternalPacket]
        EP[ExternalPacket]
        APBW[ArrayPoolBufferWriter]
        UIG[UniqueIdGenerator]
    end

    subgraph "External Services"
        Redis[(Redis Cluster)]
        NetMQ[NetMQ Router Socket]
    end

    App --> NS
    NS --> NC
    NS --> NEC
    NC --> NetMQ
    NC --> NPH
    NPH --> NEC
    NEC --> AM
    AM --> Actor
    Actor --> IP
    NS --> Redis
```

## 1. Infrastructure Layer - NetworkServer.Common

### Packet System

```mermaid
classDiagram
    class ExternalPacket {
        +Header Header
        +ArrayPoolBufferWriter Payload
        +int PayloadLength
        +Move() ArrayPoolBufferWriter
        +WriteToSpan(Span~byte~)
        +InitBodyPool()$ void
    }

    class InternalPacket {
        +InternalHeader InternalHeader
        +IMessage Msg
        +long Dest
        +long Source
        +long ActorId
        +int RequestKey
        +bool IsReply
        +Create(...)$ InternalPacket
        +CreateResponse(...)$ InternalPacket
        +MoveMsg() IMessage
    }

    class Header {
        +PacketFlags Flags
        +ushort MsgId
        +uint MsgSeq
        +int ErrorCode
        +uint OriginalSize
        +bool HasError
        +bool Compressed
        +bool Encrypted
    }

    class InternalHeader {
        +long Dest
        +long Source
        +long ActorId
        +ushort MsgId
        +bool IsReply
        +int RequestKey
    }

    class IPacketParser~T~ {
        <<interface>>
        +Parse(ArrayPoolBufferWriter) T
    }

    class PacketParserForClient {
        +Parse(ArrayPoolBufferWriter) ExternalPacket
    }

    ExternalPacket --> Header : contains
    InternalPacket --> InternalHeader : contains
    InternalPacket --> IMessage : contains
    IPacketParser~T~ <|.. PacketParserForClient : implements
```

### Memory & Utilities

```mermaid
classDiagram
    class ArrayPoolBufferWriter {
        +int Position
        +int Capacity
        +int WrittenCount
        +ReadOnlySpan~byte~ WrittenSpan
        +GetMemory(int) Memory~byte~
        +Advance(int)
        +ReadAdvance(int)
        +Write~T~(T)
        +Read~T~() T
        +Clear()
        +Dispose()
    }

    class UniqueIdGenerator {
        +int NodeId
        +NextId() long
        +NextIdNonBlocking() long
        +ExtractTimestamp(long)$ long
        +ExtractNodeId(long)$ int
    }

    class IApplicationStopper {
        <<interface>>
        +StopAsync() Task
    }

    class HostApplicationStopper {
        +StopAsync() Task
    }

    IApplicationStopper <|.. HostApplicationStopper : implements
```

## 2. Core Layer - NetworkServer.Node

### Node Service & Configuration

```mermaid
classDiagram
    class NodeService {
        -NodeConfig _config
        -INodeManager _nodeManager
        -IClusterRegistry _clusterRegistry
        -NodeEventController _eventController
        -NodeCommunicator _communicator
        +StartAsync(CancellationToken) Task
        -JoinClusterAsync() Task
        -HeartBeatLoopAsync() Task
        -CheckClusterStateLoopAsync() Task
        -OnJoinNode(long)
        -OnLeaveNode(long)
    }

    class NodeConfig {
        +string RedisConnectionString
        +string ServerRegistryKey
        +string Host
        +int Port
        +Guid NodeGuid
        +long NodeId
        +ServerType ServerType
        +string SubApiName
        +StickyType StickyType
        +int HeartBeatIntervalSeconds
        +int HeartBeatTtlSeconds
        +int HandShakeTimeoutMs
        +int RequestTimeoutMs
    }

    class ApiInfo {
        +string ApiName
        +ServerType ServerType
        +StickyType StickyType
    }

    NodeService --> NodeConfig : uses
    NodeService --> INodeManager : uses
    NodeService --> IClusterRegistry : uses
    NodeService --> NodeEventController : uses
    NodeService --> NodeCommunicator : uses
```

### Actor System

```mermaid
classDiagram
    class IActor {
        <<interface>>
        +long ActorId
        +Push(ActorMessage)
    }

    class Actor {
        +long ActorId
        -QueuedResponseWriter~ActorMessage~ _messageQueue
        -IServiceProvider _serviceProvider
        -INodeResponser _nodeResponser
        -ActorMessageHandler _handler
        +Push(ActorMessage)
        -ProcessMessageAsync() Task
        +Response(InternalPacket)
    }

    class IActorManager {
        <<interface>>
        +FindActor(long) IActor?
        +AddActor(IActor) bool
        +RemoveActor(long) bool
    }

    class ActorManager {
        -ConcurrentDictionary~long, IActor~ _actors
        +FindActor(long) IActor?
        +AddActor(IActor) bool
        +RemoveActor(long) bool
    }

    class ActorMessage {
        +InternalHeader InternalHeader
        +IMessage Msg
    }

    class ActorMessageFactory {
        +Create(InternalPacket) ActorMessage
    }

    class ActorMessageHandler {
        <<partial>>
        +AddHandler~TRequest, TResponse~(Func)
        +Handling(ActorMessage) Task
        +Initialize()
    }

    IActor <|.. Actor : implements
    IActorManager <|.. ActorManager : implements
    Actor --> ActorMessage : processes
    Actor --> ActorMessageHandler : uses
    ActorMessageFactory --> ActorMessage : creates
    ActorManager --> Actor : manages
```

### Node Management

```mermaid
classDiagram
    class INodeManager {
        <<interface>>
        +FindNode(long) RemoteNode?
        +RoundRobinByApiName(string) RemoteNode?
        +TryAdd(RemoteNode) bool
        +TryRemove(long) bool
        +GetApiInfo(string) ApiInfo?
    }

    class NodeManager {
        -ConcurrentDictionary~long, RemoteNode~ _nodesById
        -ConcurrentDictionary~string, ConcurrentDictionary~ _nodesByApiName
        -ConcurrentDictionary~string, AtomicCounter~ _roundRobinCounters
        -ConcurrentDictionary~string, ApiInfo~ _apiInfos
        +FindNode(long) RemoteNode?
        +RoundRobinByApiName(string) RemoteNode?
        +TryAdd(RemoteNode) bool
        +TryRemove(long) bool
    }

    class RemoteNode {
        +ServerInfo ServerInfo
        +long Identity
        +string Address
        +int Port
        +string ApiName
        +ServerType ServerType
        +StickyType StickyType
        +bool IsClose
        +ConnectionClosed()
    }

    INodeManager <|.. NodeManager : implements
    NodeManager --> RemoteNode : manages
```

### Network Layer

```mermaid
classDiagram
    class NodeCommunicator {
        -RouterSocket _socket
        -NetMQPoller _poller
        -NetMQQueue~InternalPacket~ _sendQueue
        -NodeConfig _config
        +event Action~InternalPacket~ OnProcessPacket
        +event Action~long~ OnJoinNode
        +event Action~InternalPacket~ OnSendFailed
        +Start()
        +ConnectAsync(RemoteNode) Task
        +Send(InternalPacket) bool
        -OnReceiveReady(object, NetMQSocketEventArgs)
        -OnSendReady(object, NetMQQueueEventArgs)
    }

    class INodeSender {
        <<interface>>
        +RequestApiAsync~TRequest, TResponse~(...) Task~TResponse~
        +RequestAsync~TRequest, TResponse~(...) Task~TResponse~
    }

    class NodeSender {
        -INodeManager _nodeManager
        -RequestCache~InternalPacket~ _requestCache
        -NodeCommunicator _communicator
        +RequestApiAsync~TRequest, TResponse~(...) Task~TResponse~
        +RequestAsync~TRequest, TResponse~(...) Task~TResponse~
    }

    class INodeResponser {
        <<interface>>
        +Response(ActorMessage)
        +Response(InternalPacket)
    }

    class NodePacketHandler {
        -RequestCache~InternalPacket~ _requestCache
        -NodeEventController _eventController
        +OnProcessPacket(InternalPacket)
        +OnSendFailed(InternalPacket)
    }

    INodeSender <|.. NodeSender : implements
    NodeSender --> NodeCommunicator : uses
    NodeSender --> INodeManager : uses
    NodePacketHandler --> NodeEventController : uses
```

### Event System

```mermaid
classDiagram
    class NodeEventController {
        <<abstract>>
        #long CreateActorMsgId
        #QueuedResponseWriter~InternalPacket~ _messageQueue
        +CreateActorAsync(long)* Task~IActor~
        +RemoveActorAsync(long)* Task
        +OnJoinNode(long)* Task
        +OnLeaveNode(long)* Task
        +OnPacket(InternalPacket)* Task
    }

    class DefaultEventController {
        -IActorManager _actorManager
        -ActorMessageFactory _actorMessageFactory
        -UniqueIdGenerator _idGenerator
        +CreateActorAsync(long) Task~IActor~
        +RemoveActorAsync(long) Task
        +OnJoinNode(long) Task
        +OnLeaveNode(long) Task
        +OnPacket(InternalPacket) Task
    }

    NodeEventController <|-- DefaultEventController : extends
    DefaultEventController --> IActorManager : uses
    DefaultEventController --> ActorMessageFactory : uses
    DefaultEventController --> UniqueIdGenerator : uses
```

### Cluster Registry

```mermaid
classDiagram
    class IClusterRegistry {
        <<interface>>
        +RegisterAndGetOtherNodesAsync(...) Task~List~ServerInfo~~
        +GetOtherLiveNodesAsync(...) Task~List~ServerInfo~~
        +RegisterSelfAsync(...) Task
        +UpdateHeartbeatAsync(...) Task
        +GetLiveNodeIdsAsync(...) Task~List~long~~
        +GetNodeInfoAsync(long) Task~ServerInfo?~
        +UnregisterSelfAsync(...) Task
    }

    class RedisClusterRegistry {
        -IConnectionMultiplexer _redis
        -IDatabase _db
        +RegisterAndGetOtherNodesAsync(...) Task~List~ServerInfo~~
        +GetOtherLiveNodesAsync(...) Task~List~ServerInfo~~
        +RegisterSelfAsync(...) Task
        +UpdateHeartbeatAsync(...) Task
    }

    IClusterRegistry <|.. RedisClusterRegistry : implements
```

### Utilities

```mermaid
classDiagram
    class RequestCache~T~ {
        -ConcurrentDictionary~int, TaskCompletionSource~T~~ _cache
        -int _requestKey
        +GetRequestKey() int
        +PendingAsync(int, CancellationToken) Task~T~
        +TryReply(int, T) bool
        +TryFail(int, Exception) bool
        +GetStats() (int, int)
    }

    class QueuedResponseWriter~T~ {
        -Channel~T~ _channel
        +Writer ChannelWriter~T~
        +WriteAsync(T) ValueTask
        +StartProcessing(Func~T, Task~) Task
    }

    RequestCache~T~ --> TaskCompletionSource : manages
    QueuedResponseWriter~T~ --> Channel : uses
```

## 3. 전체 시스템 상호작용 다이어그램

```mermaid
sequenceDiagram
    participant App as Application
    participant NS as NodeService
    participant NC as NodeCommunicator
    participant NPH as NodePacketHandler
    participant NEC as NodeEventController
    participant AM as ActorManager
    participant Actor as Actor
    participant Redis as Redis Cluster

    App->>NS: StartAsync()
    NS->>Redis: RegisterAndGetOtherNodesAsync()
    Redis-->>NS: List<ServerInfo>

    loop For each remote node
        NS->>NC: ConnectAsync(RemoteNode)
        NC-->>NS: Connected
    end

    NS->>NS: HeartBeatLoopAsync()
    NS->>NS: CheckClusterStateLoopAsync()

    Note over NC: Remote Node sends packet
    NC->>NPH: OnProcessPacket(InternalPacket)

    alt Is Reply
        NPH->>RequestCache: TryReply()
    else Is New Request
        NPH->>NEC: OnPacket(InternalPacket)
        NEC->>AM: FindActor(ActorId)

        alt Actor exists
            AM-->>NEC: Actor
        else Actor not found
            NEC->>NEC: CreateActorAsync(ActorId)
            NEC->>AM: AddActor(Actor)
        end

        NEC->>Actor: Push(ActorMessage)
        Actor->>ActorMessageHandler: Handling(ActorMessage)
        ActorMessageHandler-->>Actor: Response
        Actor->>NC: Send(InternalPacket)
    end
```

## 4. 패키지 의존성 다이어그램

```mermaid
graph TB
    subgraph "Tests Layer"
        Tests[NetworkEngine.Tests.Node]
    end

    subgraph "Application Layer"
        Node[NetworkServer.Node]
    end

    subgraph "Code Generation"
        ProtoGen[NetworkServer.ProtoGenerator]
        ParserGen[PacketParserGenerator]
    end

    subgraph "Infrastructure Layer"
        Common[NetworkServer.Common]
        Protocol[Protocol - Protobuf]
    end

    subgraph "External Dependencies"
        NetMQ[NetMQ]
        Redis[StackExchange.Redis]
        Google[Google.Protobuf]
    end

    Tests --> Node
    Tests --> Common

    Node --> Common
    Node --> Protocol
    Node --> ProtoGen
    Node --> ParserGen
    Node --> NetMQ
    Node --> Redis

    Common --> Protocol
    Common --> Google

    ProtoGen --> Protocol
    ParserGen --> Protocol
```

## 5. 주요 디자인 패턴

### Actor Model Pattern
```mermaid
graph LR
    M1[Message 1] -->|Queue| Actor
    M2[Message 2] -->|Queue| Actor
    M3[Message 3] -->|Queue| Actor
    Actor -->|Sequential Processing| Handler
    Handler -->|Response| Output
```

### Request/Response Pattern
```mermaid
sequenceDiagram
    participant Client as NodeSender
    participant Cache as RequestCache
    participant Comm as NodeCommunicator
    participant Remote as Remote Node

    Client->>Cache: GetRequestKey()
    Cache-->>Client: requestKey

    Client->>Cache: PendingAsync(requestKey)
    Client->>Comm: Send(InternalPacket)
    Comm->>Remote: Send packet

    Remote-->>Comm: Response packet
    Comm->>NPH: OnProcessPacket(response)
    NPH->>Cache: TryReply(requestKey, response)
    Cache-->>Client: Task completed
```

### Service Discovery Pattern
```mermaid
graph TB
    Node1[Node 1] -->|Register + Heartbeat| Redis
    Node2[Node 2] -->|Register + Heartbeat| Redis
    Node3[Node 3] -->|Register + Heartbeat| Redis

    NewNode[New Node] -->|GetOtherLiveNodesAsync| Redis
    Redis -->|List of active nodes| NewNode

    Redis -->|TTL Expiry| DeadNode[Dead Node Cleanup]
```

## 6. 핵심 컴포넌트별 책임

| 컴포넌트 | 책임 | 주요 기능 |
|---------|------|----------|
| **NodeService** | 노드 생명주기 관리 | 클러스터 참가, 하트비트, 노드 상태 감지 |
| **NodeCommunicator** | 네트워크 통신 | NetMQ 기반 메시지 송수신 |
| **NodeEventController** | 이벤트 처리 | 패킷 라우팅, 액터 생성 |
| **Actor** | 메시지 처리 | 순차적 메시지 처리, 상태 관리 |
| **ActorManager** | 액터 생명주기 관리 | 액터 등록/조회/제거 |
| **NodeManager** | 원격 노드 관리 | 노드 레지스트리, 로드 밸런싱 |
| **RedisClusterRegistry** | 서비스 디스커버리 | 노드 등록, 조회, TTL 관리 |
| **RequestCache** | 요청/응답 상관관계 | 타임아웃, 응답 매칭 |
| **InternalPacket** | 내부 통신 포맷 | 노드 간 메시지 전송 |
| **ExternalPacket** | 외부 통신 포맷 | 클라이언트 통신 |

## 7. 성능 최적화 기법

```mermaid
graph TB
    subgraph "Object Pooling"
        AP[ArrayPool&lt;byte&gt;] --> APBW[ArrayPoolBufferWriter]
    end

    subgraph "Lock-Free Operations"
        CD[ConcurrentDictionary] --> AM[ActorManager]
        CD --> NM[NodeManager]
        IL[Interlocked] --> UIG[UniqueIdGenerator]
    end

    subgraph "Single-Threaded Actors"
        CH[Channel] --> QRW[QueuedResponseWriter]
        QRW --> Actor
    end

    subgraph "Non-Blocking I/O"
        NMQPoller[NetMQPoller] --> NC[NodeCommunicator]
    end

    subgraph "Zero-Copy Messaging"
        NetMQMsg[NetMQ Msg] --> Move[Msg.Move]
    end
```

## 8. 시스템 특징 요약

### Distributed System Capabilities
- ✅ Redis 기반 서비스 디스커버리
- ✅ API별 라운드 로빈 로드 밸런싱
- ✅ 자동 장애 노드 감지 및 제거
- ✅ Zero-Copy 메시징 (NetMQ Msg 소유권 이전)
- ✅ 타임아웃 기반 비동기 요청/응답

### Performance Optimizations
- ✅ Object Pooling (ArrayPool)
- ✅ Lock-Free Operations (ConcurrentDictionary, Interlocked)
- ✅ Single-Threaded Actors (락 경합 제거)
- ✅ Non-Blocking I/O (NetMQ 이벤트 드리븐 폴러)
- ✅ 효율적인 직렬화 (Protobuf)

### Reliability Features
- ✅ Graceful Shutdown (IApplicationStopper)
- ✅ 재시도 로직 (핸드셰이크 지수 백오프)
- ✅ 타임아웃 처리 (CancellationToken)
- ✅ Self-Healing (하트비트 실패 시 자동 노드 제거)

---

**이 아키텍처는 NetMQ 기반의 분산 액터 시스템으로, Redis 서비스 디스커버리를 활용하여 마이크로서비스 환경에서 고성능 노드 간 통신을 지원합니다.**

using System;

namespace NetworkClient.Config;

/// <summary>
/// NetClient 설정
/// </summary>
public class NetClientConfig
{
    /// <summary>
    /// RPC 요청 타임아웃 (기본: 15초)
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 수신 메시지 큐 최대 크기 (기본: 1000)
    /// </summary>
    public int MaxQueueSize { get; init; } = 1000;

    /// <summary>
    /// TCP_NODELAY 옵션 (기본: true)
    /// </summary>
    public bool NoDelay { get; init; } = true;

    /// <summary>
    /// TCP Keep-Alive 옵션 (기본: true)
    /// </summary>
    public bool KeepAlive { get; init; } = true;

    /// <summary>
    /// 연결 타임아웃 (기본: 15초)
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

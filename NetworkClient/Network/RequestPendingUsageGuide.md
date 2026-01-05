# RequestPending 사용 가이드 및 마이그레이션

## 🚨 기존 코드의 문제점

기존 `RequestPending<T>.Request()` 메소드에는 다음과 같은 심각한 문제점들이 있습니다:

### 1. 요청 등록 누락
```csharp
// ❌ 문제: _pendingRequests에 tcs를 등록하지 않음
var tcs = new TaskCompletionSource<PendingElement<TElement>>();
var requestId = GetSequence();
// pendingRequests.TryAdd(requestId, tcs) <- 이 부분이 없음!
```

### 2. 타임아웃 로직 중복
```csharp
// ❌ 두 번의 타임아웃 예외 설정
pendingTcs.TrySetException(new TimeoutException($"Request {requestId} timeout"));
tcs.TrySetException(new TimeoutException("Connection timeout"));
```

### 3. 메모리 누수
- `CancellationTokenSource` 정리 누락
- Task 리소스 해제 미흡

---

## ✅ 개선된 RequestPending 사용법

### 기본 사용 예제

```csharp
using Microsoft.Extensions.Logging;

// 1. RequestPending 인스턴스 생성
var timeProvider = TimeProvider.System;
var logger = loggerFactory.CreateLogger<RequestPending<string>>();
var requestPending = new RequestPending<string>(
    timeProvider: timeProvider,
    logger: logger,
    timeoutMs: 5000,              // 5초 타임아웃
    enableLoggingResponseTime: true // 응답 시간 로깅 활성화
);

// 2. 요청 보내기
try 
{
    var responseTask = requestPending.RequestAsync();
    
    // 실제 네트워크 요청 보내기 (별도 구현)
    await SendNetworkRequestAsync(requestId: 1);
    
    // 응답 대기
    var response = await responseTask;
    Console.WriteLine($"Response: {response.Element}");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Request timed out: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"Request failed: {ex.Message}");
}

// 3. 응답 처리 (다른 스레드에서)
public void HandleNetworkResponse(int requestId, string data)
{
    var pendingElement = new PendingElement<string>
    {
        RegisterId = requestId,
        Element = data
    };
    
    bool completed = requestPending.TryCompleteRequest(pendingElement);
    if (!completed)
    {
        logger.LogWarning("Failed to complete request {RequestId}", requestId);
    }
}
```

### 고급 사용 예제

```csharp
public class NetworkClient
{
    private readonly RequestPending<NetworkResponse> _requestPending;
    private readonly ILogger<NetworkClient> _logger;

    public NetworkClient(ILogger<NetworkClient> logger)
    {
        _logger = logger;
        _requestPending = new RequestPending<NetworkResponse>(
            TimeProvider.System,
            logger,
            timeoutMs: 10000,
            enableLoggingResponseTime: true
        );
    }

    public async Task<NetworkResponse> SendRequestAsync(NetworkRequest request)
    {
        // 1. 요청 등록 및 ID 생성
        var requestTask = _requestPending.RequestAsync();
        var requestId = _requestPending.GetPendingRequestIds().LastOrDefault();
        
        _logger.LogDebug("Sending request {RequestId}: {RequestType}", 
            requestId, request.GetType().Name);

        try
        {
            // 2. 실제 네트워크 전송
            request.RequestId = requestId;
            await _networkSocket.SendAsync(request);

            // 3. 응답 대기
            var response = await requestTask;
            
            _logger.LogDebug("Request {RequestId} completed successfully", requestId);
            return response.Element;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Request {RequestId} timed out", requestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request {RequestId} failed", requestId);
            throw;
        }
    }

    public void HandleIncomingResponse(NetworkResponse response)
    {
        var pendingElement = new PendingElement<NetworkResponse>
        {
            RegisterId = response.RequestId,
            Element = response
        };

        if (!_requestPending.TryCompleteRequest(pendingElement))
        {
            _logger.LogWarning("Received response for unknown request {RequestId}", 
                response.RequestId);
        }
    }

    public void Dispose()
    {
        _requestPending.CancelAllRequests("NetworkClient disposing");
    }
}
```

---

## 🔄 마이그레이션 가이드

### 1. 기존 코드에서 새 코드로 변경

```csharp
// ❌ 기존 코드
var response = await requestPending.Request();
requestPending.OnReply(pendingElement);
requestPending.Clear();

// ✅ 새 코드
var response = await requestPending.RequestAsync();
requestPending.TryCompleteRequest(pendingElement);
requestPending.CancelAllRequests("Shutting down");
```

### 2. 에러 처리 개선

```csharp
// ❌ 기존: 예외 처리 부족
try 
{
    var result = await requestPending.Request();
    // 성공 처리
}
catch (TimeoutException)
{
    // 타임아웃 처리만
}

// ✅ 새 코드: 포괄적 에러 처리
try 
{
    var result = await requestPending.RequestAsync();
    // 성공 처리
}
catch (TimeoutException ex)
{
    logger.LogWarning("Request timed out: {Message}", ex.Message);
    // 타임아웃 특별 처리
}
catch (OperationCanceledException ex)
{
    logger.LogInformation("Request cancelled: {Message}", ex.Message);
    // 취소 처리
}
catch (Exception ex)
{
    logger.LogError(ex, "Request failed unexpectedly");
    // 일반 오류 처리
}
```

### 3. 리소스 관리 개선

```csharp
// ✅ IDisposable 패턴 구현
public class MyNetworkService : IDisposable
{
    private readonly RequestPending<MyResponse> _requestPending;
    private volatile bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 모든 대기 중인 요청 취소
        _requestPending.CancelAllRequests("Service disposing");
    }

    public async Task<MyResponse> SendRequestAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MyNetworkService));
        
        return await _requestPending.RequestAsync();
    }
}
```

---

## 📊 성능 및 모니터링

### 1. 성능 메트릭 수집

```csharp
// 응답 시간 로깅 활성화
var requestPending = new RequestPending<MyData>(
    timeProvider,
    logger,
    timeoutMs: 5000,
    enableLoggingResponseTime: true // 이 옵션으로 성능 로그 확인 가능
);

// 대기 중인 요청 수 모니터링
var pendingCount = requestPending.PendingCount;
if (pendingCount > 100)
{
    logger.LogWarning("High number of pending requests: {Count}", pendingCount);
}
```

### 2. 헬스체크 통합

```csharp
public class NetworkHealthCheck : IHealthCheck
{
    private readonly RequestPending<PingResponse> _requestPending;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pendingCount = _requestPending.PendingCount;
        
        if (pendingCount > 50)
        {
            return HealthCheckResult.Degraded($"High pending requests: {pendingCount}");
        }
        
        if (pendingCount > 100)
        {
            return HealthCheckResult.Unhealthy($"Too many pending requests: {pendingCount}");
        }
        
        return HealthCheckResult.Healthy($"Pending requests: {pendingCount}");
    }
}
```

---

## ⚠️ 주의사항

1. **타임아웃 설정**: 적절한 타임아웃 값 설정 (너무 짧으면 불필요한 재시도, 너무 길면 리소스 낭비)
2. **동시 요청 제한**: 너무 많은 동시 요청은 메모리 사용량 증가
3. **응답 ID 매칭**: 네트워크 응답의 RequestId가 정확히 매칭되는지 확인
4. **리소스 정리**: 서비스 종료 시 반드시 `CancelAllRequests()` 호출

---

## 🧪 테스트 예제

```csharp
[Fact]
public async Task NetworkRequest_Success_ReturnsResponse()
{
    // Arrange
    var timeProvider = new FakeTimeProvider();
    var logger = new TestLogger<RequestPending<string>>();
    var requestPending = new RequestPending<string>(timeProvider, logger, 5000);

    // Act
    var requestTask = requestPending.RequestAsync();
    
    // 응답 시뮬레이션
    var response = new PendingElement<string> 
    { 
        RegisterId = 1, 
        Element = "success" 
    };
    requestPending.TryCompleteRequest(response);

    var result = await requestTask;

    // Assert
    Assert.Equal("success", result.Element);
    Assert.Equal(0, requestPending.PendingCount);
}
```

이 가이드를 따라 기존 코드를 안전하게 마이그레이션하고 더 안정적인 Request-Response 패턴을 구현할 수 있습니다.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NetworkClient.Network
{
    public class PendingElement<TElement> 
    {
        public int RegisterId { get; set; }
        public required TElement Element { get; set; }
    }

    /// <summary>
    /// Request-Response 패턴을 위한 개선된 요청 대기 관리 클래스
    /// </summary>
    public class RequestPending<TElement>(
        TimeProvider timeProvider,
        ILogger logger,
        int timeoutMs,
        bool enableLoggingResponseTime = false) where TElement : class
    {
        private readonly ConcurrentDictionary<int, PendingRequest> _pendingRequests = new();
        private volatile int _currentSequence;
        private readonly TimeProvider _timeProvider = timeProvider;
        private readonly ILogger _logger = logger;
        private readonly int _timeoutMs = timeoutMs;

        private sealed class PendingRequest : IDisposable
        {
            public TaskCompletionSource<PendingElement<TElement>> TaskCompletionSource { get; }
            public CancellationTokenSource TimeoutTokenSource { get; }
            public long StartTimestamp { get; }
            private volatile bool _disposed;

            public PendingRequest(long startTimestamp)
            {
                TaskCompletionSource = new TaskCompletionSource<PendingElement<TElement>>();
                TimeoutTokenSource = new CancellationTokenSource();
                StartTimestamp = startTimestamp;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                TimeoutTokenSource?.Cancel();
                TimeoutTokenSource?.Dispose();
            }
        }

        public void SetSequence(int sequence)
        {
            _currentSequence = sequence;
        }

        private int GetNextSequence() => Interlocked.Increment(ref _currentSequence);

        /// <summary>
        /// 새로운 요청을 등록하고 응답을 대기합니다
        /// </summary>
        public async Task<PendingElement<TElement>> RequestAsync(int registerId)
        {
            var requestId = GetNextSequence();
            var startTime = _timeProvider.GetTimestamp();
            var pendingRequest = new PendingRequest(startTime);

            // 1. 요청 등록
            if (!_pendingRequests.TryAdd(requestId, pendingRequest))
            {
                pendingRequest.Dispose();
                throw new InvalidOperationException($"Duplicate request ID: {requestId}");
            }

            try
            {
                // 2. 타임아웃 설정
                _ = SetupTimeoutAsync(requestId, pendingRequest);

                // 3. 응답 대기
                var result = await pendingRequest.TaskCompletionSource.Task;
                
                // 4. 응답 시간 로깅
                if (enableLoggingResponseTime)
                {
                    var elapsed = _timeProvider.GetElapsedTime(startTime);
                    _logger.LogDebug("Request {RequestId} completed in {ElapsedMs}ms", 
                        requestId, elapsed.TotalMilliseconds);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Request {RequestId} failed", requestId);
                throw;
            }
            finally
            {
                // 5. 정리
                if (_pendingRequests.TryRemove(requestId, out var removedRequest))
                {
                    removedRequest.Dispose();
                }
            }
        }

        /// <summary>
        /// 응답을 처리합니다
        /// </summary>
        public bool TryCompleteRequest(PendingElement<TElement> pendingElement)
        {
            if (_pendingRequests.TryGetValue(pendingElement.RegisterId, out var pendingRequest))
            {
                var completed = pendingRequest.TaskCompletionSource.TrySetResult(pendingElement);
                
                if (completed && enableLoggingResponseTime)
                {
                    var elapsed = _timeProvider.GetElapsedTime(pendingRequest.StartTimestamp);
                    _logger.LogDebug("Request {RequestId} response received in {ElapsedMs}ms", 
                        pendingElement.RegisterId, elapsed.TotalMilliseconds);
                }

                return completed;
            }

            _logger.LogWarning("Received response for unknown request ID: {RequestId}", 
                pendingElement.RegisterId);
            return false;
        }

        /// <summary>
        /// 특정 요청을 오류로 완료합니다
        /// </summary>
        public bool TryCompleteWithError(int requestId, Exception exception)
        {
            if (_pendingRequests.TryGetValue(requestId, out var pendingRequest))
            {
                return pendingRequest.TaskCompletionSource.TrySetException(exception);
            }
            return false;
        }

        /// <summary>
        /// 모든 대기 중인 요청을 취소합니다
        /// </summary>
        public void CancelAllRequests(string reason = "RequestPending cleared")
        {
            var requests = _pendingRequests.ToArray();
            _pendingRequests.Clear();

            var cancellationException = new OperationCanceledException(reason);

            foreach (var kvp in requests)
            {
                var pendingRequest = kvp.Value;
                pendingRequest.TaskCompletionSource.TrySetException(cancellationException);
                pendingRequest.Dispose();
            }

            _logger.LogInformation("Cancelled {Count} pending requests: {Reason}", 
                requests.Length, reason);
        }

        /// <summary>
        /// 현재 대기 중인 요청 수를 반환합니다
        /// </summary>
        public int PendingCount => _pendingRequests.Count;

        /// <summary>
        /// 대기 중인 요청 ID 목록을 반환합니다 (디버깅용)
        /// </summary>
        public int[] GetPendingRequestIds() => [.. _pendingRequests.Keys];

        private async Task SetupTimeoutAsync(int requestId, PendingRequest pendingRequest)
        {
            try
            {
                await Task.Delay(_timeoutMs, pendingRequest.TimeoutTokenSource.Token);
                
                // 타임아웃 발생
                if (_pendingRequests.TryRemove(requestId, out var timedOutRequest))
                {
                    var timeoutException = new TimeoutException(
                        $"Request {requestId} timed out after {_timeoutMs}ms");
                    
                    timedOutRequest.TaskCompletionSource.TrySetException(timeoutException);
                    _logger.LogWarning("Request {RequestId} timed out after {TimeoutMs}ms", 
                        requestId, _timeoutMs);
                }
            }
            catch (OperationCanceledException)
            {
                // 정상: 응답이 타임아웃 전에 도착함
            }
        }
    }
}
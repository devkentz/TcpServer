using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Network.Server.Tcp.Core;

public class QueuedResponseWriter<T> : IDisposable
{
    private readonly Channel<T> _channel;
    private readonly Task _consumerTask;
    private readonly Func<T, Task> _func;
    private readonly ILogger _logger;

    public QueuedResponseWriter(Func<T, Task> func, ILogger logger)
    {
        _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });

        _func = func;
        _logger = logger;

        _consumerTask = ConsumeQueueAsync();
    }

    public void Write(in T value)
    {
        _channel.Writer.TryWrite(value);
    }

    async Task ConsumeQueueAsync()
    {
        try
        {
            var reader = _channel.Reader;

            do
            {
                while (reader.TryRead(out var item))
                    await _func(item).ConfigureAwait(false);
                
            } while (await reader.WaitToReadAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while writing to client");
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        
        try
        {
            _consumerTask.Wait(TimeSpan.FromSeconds(5)); // 타임아웃 추가
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Consumer task did not complete gracefully");
        }
    }
}
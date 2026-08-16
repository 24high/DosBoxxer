namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Enforces a provider's documented usage limits before each request. Two limits are supported
/// and both are honoured at once:
/// <list type="bullet">
/// <item>a minimum interval between consecutive requests (e.g. "no more than one per second"),</item>
/// <item>a maximum number of requests within a rolling time window (e.g. "360 per hour").</item>
/// </list>
///
/// Requests are fully serialised: only one caller passes the gate at a time and it holds the gate
/// while waiting, so the spacing and the window count can never be exceeded even under concurrent
/// callers. The clock and the delay are injectable so the behaviour can be unit-tested without
/// real waiting.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly TimeSpan _minInterval;
    private readonly int _maxPerWindow;
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recent = new();

    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <param name="minInterval">Minimum time between two requests. <see cref="TimeSpan.Zero"/> disables it.</param>
    /// <param name="maxPerWindow">Maximum requests per <paramref name="window"/>. Zero or less disables the window limit.</param>
    /// <param name="window">The rolling window for <paramref name="maxPerWindow"/>.</param>
    /// <param name="now">Clock, defaults to <see cref="DateTimeOffset.UtcNow"/>. Injected for tests.</param>
    /// <param name="delay">Delay function, defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>. Injected for tests.</param>
    public RateLimiter(
        TimeSpan minInterval,
        int maxPerWindow = 0,
        TimeSpan? window = null,
        Func<DateTimeOffset>? now = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _minInterval = minInterval < TimeSpan.Zero ? TimeSpan.Zero : minInterval;
        _maxPerWindow = maxPerWindow;
        _window = window ?? TimeSpan.FromHours(1);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    /// <summary>
    /// Blocks until issuing a request now would respect both limits, then records the request.
    /// Call once immediately before each outgoing request.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = _now();
                var wait = TimeSpan.Zero;

                // 1) Minimum interval since the previous request.
                if (_minInterval > TimeSpan.Zero)
                {
                    var sinceLast = now - _last;
                    if (sinceLast < _minInterval)
                    {
                        wait = _minInterval - sinceLast;
                    }
                }

                // 2) Rolling window count.
                if (_maxPerWindow > 0)
                {
                    while (_recent.Count > 0 && now - _recent.Peek() >= _window)
                    {
                        _recent.Dequeue();
                    }

                    if (_recent.Count >= _maxPerWindow)
                    {
                        var windowWait = _window - (now - _recent.Peek());
                        if (windowWait > wait)
                        {
                            wait = windowWait;
                        }
                    }
                }

                if (wait <= TimeSpan.Zero)
                {
                    break;
                }

                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }

            var stamp = _now();
            _last = stamp;

            if (_maxPerWindow > 0)
            {
                _recent.Enqueue(stamp);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

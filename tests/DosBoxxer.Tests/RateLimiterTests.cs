using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Helpers;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// Deterministic tests for the rate limiter using a controllable clock: the fake delay advances
/// the virtual clock instead of really waiting, so the enforced spacing and window limits can be
/// asserted precisely and instantly.
/// </summary>
public sealed class RateLimiterTests
{
    private sealed class VirtualClock
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public readonly List<TimeSpan> Delays = new();

        public DateTimeOffset GetNow() => Now;

        public Task Delay(TimeSpan d, CancellationToken _)
        {
            Delays.Add(d);
            Now += d;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FirstRequest_DoesNotWait()
    {
        var clock = new VirtualClock();
        var limiter = new RateLimiter(TimeSpan.FromSeconds(10), now: clock.GetNow, delay: clock.Delay);

        await limiter.WaitAsync();

        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task SecondRequest_WaitsTheMinimumInterval()
    {
        var clock = new VirtualClock();
        var limiter = new RateLimiter(TimeSpan.FromSeconds(10), now: clock.GetNow, delay: clock.Delay);

        await limiter.WaitAsync();
        await limiter.WaitAsync();

        Assert.Single(clock.Delays);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Delays[0]);
    }

    [Fact]
    public async Task NoWait_WhenEnoughTimeHasPassed()
    {
        var clock = new VirtualClock();
        var limiter = new RateLimiter(TimeSpan.FromSeconds(10), now: clock.GetNow, delay: clock.Delay);

        await limiter.WaitAsync();
        clock.Now += TimeSpan.FromSeconds(12); // more than the interval elapses on its own
        await limiter.WaitAsync();

        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task WindowLimit_BlocksTheRequestThatWouldExceedIt()
    {
        var clock = new VirtualClock();

        // 3 requests per 10-second rolling window, no minimum interval.
        var limiter = new RateLimiter(TimeSpan.Zero, maxPerWindow: 3, window: TimeSpan.FromSeconds(10),
            now: clock.GetNow, delay: clock.Delay);

        await limiter.WaitAsync();
        await limiter.WaitAsync();
        await limiter.WaitAsync();
        Assert.Empty(clock.Delays);

        // The fourth must wait until the oldest of the three leaves the window.
        await limiter.WaitAsync();

        Assert.Single(clock.Delays);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Delays[0]);
    }

    [Fact]
    public async Task WindowLimit_AllowsSteadyRateOnceWarm()
    {
        var clock = new VirtualClock();
        var limiter = new RateLimiter(TimeSpan.Zero, maxPerWindow: 4, window: TimeSpan.FromSeconds(1),
            now: clock.GetNow, delay: clock.Delay);

        // Fire 12 requests; with 4 per second the limiter must insert waits so the total virtual
        // time spans at least 2 seconds (12 requests / 4 per second - 1 window).
        for (var i = 0; i < 12; i++)
        {
            await limiter.WaitAsync();
        }

        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(clock.Now - start >= TimeSpan.FromSeconds(2),
            $"12 requests at 4/s should span >= 2s, spanned {(clock.Now - start).TotalSeconds:F2}s");
    }

    [Fact]
    public async Task BothLimits_TheStricterOneWins()
    {
        var clock = new VirtualClock();

        // 1s minimum interval AND 5 per 10s. The interval dominates for the first requests.
        var limiter = new RateLimiter(TimeSpan.FromSeconds(1), maxPerWindow: 5, window: TimeSpan.FromSeconds(10),
            now: clock.GetNow, delay: clock.Delay);

        for (var i = 0; i < 5; i++)
        {
            await limiter.WaitAsync();
        }

        // Four 1-second interval waits between the five requests.
        Assert.Equal(4, clock.Delays.Count);
        Assert.All(clock.Delays, d => Assert.Equal(TimeSpan.FromSeconds(1), d));

        // The sixth request hits the window cap (5 in 10s) — it must wait until the first ages out.
        clock.Delays.Clear();
        await limiter.WaitAsync();

        Assert.Single(clock.Delays);
        // First request was at t=0, now is t=4s (four 1s waits); it leaves the window at t=10s.
        Assert.Equal(TimeSpan.FromSeconds(6), clock.Delays[0]);
    }

    [Fact]
    public async Task SerialisesConcurrentCallers()
    {
        // With real (tiny) delays, concurrent callers must still be spaced by the interval.
        var limiter = new RateLimiter(TimeSpan.FromMilliseconds(40));

        var start = DateTimeOffset.UtcNow;
        await Task.WhenAll(
            limiter.WaitAsync(),
            limiter.WaitAsync(),
            limiter.WaitAsync());

        // Three requests spaced by >=40ms means >=80ms total for the last two gaps.
        Assert.True(DateTimeOffset.UtcNow - start >= TimeSpan.FromMilliseconds(70));
    }
}

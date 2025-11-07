using System.Collections.Concurrent;
using FlowControl.EnumerableControl;
using FluentAssertions;

namespace FlowControl.Test.EnumerableControl;

public class ParallelControlAsyncTests
{
    [Fact]
    public async Task ParallelAsync_ProcessesAllItems_AndHonorsMaxParallel()
    {
        var items = Enumerable.Range(1, 100).ToArray();
        var current = 0;
        var maxObserved = 0;
        var processed = 0;

        await items.ParallelAsync(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedExtensions.Max(ref maxObserved, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref current);
            Interlocked.Increment(ref processed);
        }, maxParallel: 8);

        processed.Should().Be(items.Length);
        maxObserved.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public async Task ParallelAsyncByKey_RespectsPerKeyLimit()
    {
        // Create 60 items across 3 keys (A,B,C)
        var items = Enumerable.Range(0, 60).Select(i => (Key: (char)('A' + (i % 3)), Value: i)).ToArray();

        var perKeyCurrent = new ConcurrentDictionary<char, int>();
        var perKeyMax = new ConcurrentDictionary<char, int>();
        var totalCurrent = 0;
        var totalMax = 0;

        await items.ParallelAsyncByKey(
            keySelector: it => it.Key,
            body: async (it, ct) =>
            {
                // Track per-key concurrency
                var cur = perKeyCurrent.AddOrUpdate(it.Key, 1, (_, v) => v + 1);
                perKeyMax.AddOrUpdate(it.Key, cur, (_, v) => Math.Max(v, cur));

                // Track total concurrency
                var total = Interlocked.Increment(ref totalCurrent);
                InterlockedExtensions.Max(ref totalMax, total);

                await Task.Delay(10, ct);

                perKeyCurrent.AddOrUpdate(it.Key, 0, (_, v) => v - 1);
                Interlocked.Decrement(ref totalCurrent);
            },
            maxTotalParallel: 12,
            maxPerKey: 3);

        // Verify per-key limit
        perKeyMax.Values.Should().OnlyContain(v => v <= 3);

        // Verify total parallel limit
        totalMax.Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public async Task ParallelAsync_ReturnsResults()
    {
        var items = Enumerable.Range(1, 20).ToList();
        var results = await items.ParallelAsync(async (x, ct) =>
        {
            await Task.Delay(5, ct);
            return x * x;
        }, maxParallel: 4);

        results.Should().HaveCount(20);
        results.Should().BeEquivalentTo(items.Select(i => i * i));
    }

    [Fact]
    public async Task ParallelAsync_CanBeCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var items = Enumerable.Range(0, 1000);
        Func<Task> act = async () =>
        {
            await items.ParallelAsync(async (_, ct) =>
            {
                await Task.Delay(10, ct);
            }, maxParallel: 32, ct: cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ParallelAsync_AggregatesExceptions()
    {
        var items = Enumerable.Range(1, 20);
        Func<Task> act = async () =>
        {
            await items.ParallelAsync(async (i, ct) =>
            {
                await Task.Delay(1, ct);
                if (i % 5 == 0) throw new InvalidOperationException("boom");
            }, maxParallel: 8);
        };

        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Should().Match<Exception>(e => e is AggregateException || e is InvalidOperationException);
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int target, int value)
    {
        int initial, computed;
        do
        {
            initial = target;
            computed = Math.Max(initial, value);
        } while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
    }
}

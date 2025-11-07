using System.Collections.Concurrent;
using FluentAssertions;
using ForEach.AsyncEnumerable;

namespace Foreach.Test.AsyncEnumerable;

public class AsyncEnumerableExtensionsTest
{
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }

    [Fact]
    public async Task ForEachParallelAsync_ProcessesAllItems_AndHonorsMaxParallel()
    {
        var items = System.Linq.Enumerable.Range(1, 100).ToArray();
        var asyncItems = ToAsyncEnumerable(items);
        var current = 0;
        var maxObserved = 0;
        var processed = 0;

        await asyncItems.ForEachParallelAsync(async (_, ct) =>
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
    public async Task ForEachParallelByKeyAsync_RespectsPerKeyLimit()
    {
        // Create 60 items across 3 keys (A,B,C)
        var items = System.Linq.Enumerable.Range(0, 60).Select(i => (Key: (char)('A' + (i % 3)), Value: i)).ToArray();
        var asyncItems = ToAsyncEnumerable(items);

        var perKeyCurrent = new ConcurrentDictionary<char, int>();
        var perKeyMax = new ConcurrentDictionary<char, int>();
        var totalCurrent = 0;
        var totalMax = 0;

        await asyncItems.ForEachParallelByKeyAsync(
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
    public async Task ForEachParallelAsync_ReturnsResults()
    {
        var items = System.Linq.Enumerable.Range(1, 20).ToList();
        var asyncItems = ToAsyncEnumerable(items);

        var results = await asyncItems.ForEachParallelAsync(async (x, ct) =>
        {
            await Task.Delay(5, ct);
            return x * x;
        }, maxParallel: 4);

        results.Should().HaveCount(20);
        results.Should().BeEquivalentTo(items.Select(i => i * i));
    }

    [Fact]
    public async Task ForEachParallelAsync_CanBeCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var items = System.Linq.Enumerable.Range(0, 1000);
        var asyncItems = ToAsyncEnumerable(items);

        Func<Task> act = async () =>
        {
            await asyncItems.ForEachParallelAsync(async (_, ct) =>
            {
                await Task.Delay(10, ct);
            }, maxParallel: 32, ct: cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ForEachParallelAsync_AggregatesExceptions()
    {
        var items = System.Linq.Enumerable.Range(1, 20);
        var asyncItems = ToAsyncEnumerable(items);

        Func<Task> act = async () =>
        {
            await asyncItems.ForEachParallelAsync(async (i, ct) =>
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
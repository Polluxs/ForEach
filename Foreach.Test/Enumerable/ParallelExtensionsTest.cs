using System.Collections.Concurrent;
using FluentAssertions;
using ForEach.Enumerable;

namespace Foreach.Test.Enumerable;

public partial class AsyncEnumerableExtensionsTests
{
    [Fact]
    public async Task ForEachParallelAsync_ProcessesAllItems_AndHonorsMaxParallel()
    {
        var items = System.Linq.Enumerable.Range(1, 100).ToArray();
        var current = 0;
        var maxObserved = 0;
        var processed = 0;

        await items.ForEachParallelAsync(async (_, ct) =>
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
    public async Task ForEachKeyParallelAsync_RespectsPerKeyLimit()
    {
        // Create 60 items across 3 keys (A,B,C)
        var items = System.Linq.Enumerable.Range(0, 60).Select(i => (Key: (char)('A' + (i % 3)), Value: i)).ToArray();

        var perKeyCurrent = new ConcurrentDictionary<char, int>();
        var perKeyMax = new ConcurrentDictionary<char, int>();
        var totalCurrent = 0;
        var totalMax = 0;

        await items.ForEachKeyParallelAsync(
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
            maxConcurrent: 12,
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
        var results = await items.ForEachParallelAsync(async (x, ct) =>
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
        Func<Task> act = async () =>
        {
            await items.ForEachParallelAsync(async (_, ct) =>
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
        Func<Task> act = async () =>
        {
            await items.ForEachParallelAsync(async (i, ct) =>
            {
                await Task.Delay(1, ct);
                if (i % 5 == 0) throw new InvalidOperationException("boom");
            }, maxParallel: 8);
        };

        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Should().Match<Exception>(e => e is AggregateException || e is InvalidOperationException);
    }

    [Fact]
    public async Task ForEachBatchParallelAsync_ProcessesBatchesCorrectly()
    {
        var items = System.Linq.Enumerable.Range(1, 100).ToArray();
        var processedBatches = new ConcurrentBag<List<int>>();
        var maxConcurrentBatches = 0;
        var currentConcurrentBatches = 0;

        await items.ForEachBatchParallelAsync(async (batch, ct) =>
        {
            var current = Interlocked.Increment(ref currentConcurrentBatches);
            InterlockedExtensions.Max(ref maxConcurrentBatches, current);

            processedBatches.Add(batch);
            await Task.Delay(10, ct);

            Interlocked.Decrement(ref currentConcurrentBatches);
        }, maxPerBatch: 10, maxConcurrent: 4);

        // Verify all items were processed
        var allProcessedItems = processedBatches.SelectMany(b => b).OrderBy(x => x).ToList();
        allProcessedItems.Should().BeEquivalentTo(items);

        // Verify batches are correct size (except possibly the last one)
        var batchSizes = processedBatches.Select(b => b.Count).OrderByDescending(x => x).ToList();
        batchSizes.Take(batchSizes.Count - 1).Should().OnlyContain(size => size == 10);
        batchSizes.Last().Should().BeLessThanOrEqualTo(10);

        // Verify maxConcurrent was respected
        maxConcurrentBatches.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task ForEachBatchParallelAsync_HandlesPartialBatch()
    {
        var items = System.Linq.Enumerable.Range(1, 25).ToArray();
        var processedBatches = new ConcurrentBag<List<int>>();

        await items.ForEachBatchParallelAsync(async (batch, ct) =>
        {
            processedBatches.Add(batch);
            await Task.Delay(1, ct);
        }, maxPerBatch: 10);

        // Should have 3 batches: 10, 10, 5
        processedBatches.Should().HaveCount(3);
        var batchSizes = processedBatches.Select(b => b.Count).OrderByDescending(x => x).ToList();
        batchSizes.Should().ContainInOrder(10, 10, 5);
    }

    [Fact]
    public async Task ForEachBatchParallelAsync_CanBeCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var items = System.Linq.Enumerable.Range(0, 1000);

        Func<Task> act = async () =>
        {
            await items.ForEachBatchParallelAsync(async (batch, ct) =>
            {
                await Task.Delay(10, ct);
            }, maxPerBatch: 10, maxConcurrent: 4, ct: cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
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

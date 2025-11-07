using System.Collections.Concurrent;
using System.Threading.Channels;
using FlowControl.FlowChannel;
using FluentAssertions;

namespace FlowControl.Test.FlowChannel;

public class ChannelExtensionsTest
{
    [Fact]
    public async Task ReadAllAsync_ReadsAllItemsFromChannel()
    {
        var channel = Channel.CreateUnbounded<int>();

        // Write items
        await channel.Writer.WriteAsync(1);
        await channel.Writer.WriteAsync(2);
        await channel.Writer.WriteAsync(3);
        channel.Writer.Complete();

        // Read all items
        var items = new List<int>();
        await foreach (var item in channel.ReadAllAsync())
        {
            items.Add(item);
        }

        items.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ForEachAsync_ProcessesItemsSequentially()
    {
        var channel = Channel.CreateUnbounded<int>();
        var processed = new List<int>();
        var currentlyProcessing = 0;

        // Write items
        for (int i = 0; i < 10; i++)
        {
            await channel.Writer.WriteAsync(i);
        }
        channel.Writer.Complete();

        // Process sequentially
        await channel.ForEachAsync(async (item, ct) =>
        {
            var concurrent = Interlocked.Increment(ref currentlyProcessing);
            concurrent.Should().Be(1, "ForEachAsync should be sequential");

            await Task.Delay(5, ct);
            processed.Add(item);

            Interlocked.Decrement(ref currentlyProcessing);
        });

        processed.Should().HaveCount(10);
    }

    [Fact]
    public async Task ParallelAsync_ProcessesItemsInParallel()
    {
        var channel = Channel.CreateUnbounded<int>();
        var current = 0;
        var maxObserved = 0;
        var processed = 0;

        // Write items
        for (int i = 0; i < 50; i++)
        {
            await channel.Writer.WriteAsync(i);
        }
        channel.Writer.Complete();

        await channel.ParallelAsync(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedExtensions.Max(ref maxObserved, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref current);
            Interlocked.Increment(ref processed);
        }, maxParallel: 8);

        processed.Should().Be(50);
        maxObserved.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public async Task ParallelByKeyAsync_RespectsPerKeyLimit()
    {
        var channel = Channel.CreateUnbounded<(char Key, int Value)>();

        // Write 60 items across 3 keys (A, B, C)
        for (int i = 0; i < 60; i++)
        {
            await channel.Writer.WriteAsync(((char)('A' + (i % 3)), i));
        }
        channel.Writer.Complete();

        var perKeyCurrent = new ConcurrentDictionary<char, int>();
        var perKeyMax = new ConcurrentDictionary<char, int>();
        var totalCurrent = 0;
        var totalMax = 0;

        await channel.ParallelByKeyAsync(
            keySelector: it => it.Key,
            handler: async (it, ct) =>
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
            maxParallel: 12,
            maxPerKey: 3);

        // Verify per-key limit
        perKeyMax.Values.Should().OnlyContain(v => v <= 3);

        // Verify total parallel limit
        totalMax.Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public async Task WriteAllAsync_FromEnumerable_WritesAllItems()
    {
        var channel = Channel.CreateUnbounded<int>();
        var source = Enumerable.Range(1, 100);

        await channel.WriteAllAsync(source);

        // Must manually complete the channel
        channel.Writer.Complete();

        var items = new List<int>();
        await foreach (var item in channel.ReadAllAsync())
        {
            items.Add(item);
        }

        items.Should().HaveCount(100);
        items.Should().BeEquivalentTo(source);
    }

    [Fact]
    public async Task WriteAllAsync_FromAsyncEnumerable_WritesAllItems()
    {
        var channel = Channel.CreateUnbounded<int>();

        async IAsyncEnumerable<int> GetItemsAsync()
        {
            for (int i = 1; i <= 50; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }

        await channel.WriteAllAsync(GetItemsAsync());

        // Must manually complete the channel
        channel.Writer.Complete();

        var items = new List<int>();
        await foreach (var item in channel.ReadAllAsync())
        {
            items.Add(item);
        }

        items.Should().HaveCount(50);
        items.Should().BeEquivalentTo(Enumerable.Range(1, 50));
    }

    [Fact]
    public async Task WriteAllAsync_CanBeCancelled()
    {
        var channel = Channel.CreateUnbounded<int>();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        async IAsyncEnumerable<int> GetItemsAsync()
        {
            for (int i = 0; i < 10000; i++)
            {
                await Task.Delay(10);
                yield return i;
            }
        }

        Func<Task> act = async () => await channel.WriteAllAsync(GetItemsAsync(), cts.Token);

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
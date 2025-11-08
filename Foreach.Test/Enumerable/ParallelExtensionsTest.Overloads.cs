using System.Collections.Concurrent;
using FluentAssertions;
using ForEach.Enumerable;

namespace Foreach.Test.Enumerable;

/// <summary>
/// Tests for convenience overloads that don't require CancellationToken in the body.
/// </summary>
public partial class AsyncEnumerableExtensionsTests
{
    /// <summary>
    /// Test that the simpler overload (without CT in the body) exists and works.
    /// Why: Many users don't need CT in their lambda body - they just want to process items.
    /// This overload makes the API cleaner when CT isn't needed in the work itself.
    /// </summary>
    [Fact]
    public async Task ForEachParallelAsync_WorksWithoutCancellationTokenInBody()
    {
        var items = System.Linq.Enumerable.Range(1, 10).ToArray();
        var processed = new ConcurrentBag<int>();

        // No need to include 'ct' parameter in lambda when you don't use it
        await items.ForEachParallelAsync(async item =>
        {
            await Task.Delay(5);
            processed.Add(item);
        }, maxParallel: 4);

        processed.Should().HaveCount(10);
        processed.Should().BeEquivalentTo(items);
    }

    /// <summary>
    /// Test that the map/selector overload without CT exists and works.
    /// Why: Cleaner API when the transformation doesn't need explicit cancellation handling.
    /// </summary>
    [Fact]
    public async Task ForEachParallelAsync_MapWithoutCancellationTokenInBody()
    {
        var items = System.Linq.Enumerable.Range(1, 10);

        // Simpler syntax when you don't need CT in the transformation
        var results = await items.ForEachParallelAsync(async x =>
        {
            await Task.Delay(5);
            return x * 2;
        }, maxParallel: 4);

        results.Should().HaveCount(10);
        results.Should().BeEquivalentTo(items.Select(i => i * 2));
    }

    /// <summary>
    /// Test that ForEachKeyParallelAsync overload without CT exists and works.
    /// Why: Per-key throttling logic often doesn't need CT in the body - cleaner code.
    /// </summary>
    [Fact]
    public async Task ForEachKeyParallelAsync_WorksWithoutCancellationTokenInBody()
    {
        var items = System.Linq.Enumerable.Range(0, 20)
            .Select(i => (Key: i % 3, Value: i))
            .ToArray();
        var processed = new ConcurrentBag<int>();

        // Cleaner syntax - CT not needed in the body for simple processing
        await items.ForEachKeyParallelAsync(
            keySelector: it => it.Key,
            body: async it =>
            {
                await Task.Delay(5);
                processed.Add(it.Value);
            },
            maxConcurrent: 10,
            maxPerKey: 2);

        processed.Should().HaveCount(20);
        processed.Should().BeEquivalentTo(items.Select(i => i.Value));
    }

    /// <summary>
    /// Test that ForEachBatchParallelAsync overload without CT exists and works.
    /// Why: Batch processing logic often doesn't need CT in the body - cleaner code.
    /// </summary>
    [Fact]
    public async Task ForEachBatchParallelAsync_WorksWithoutCancellationTokenInBody()
    {
        var items = System.Linq.Enumerable.Range(1, 30).ToArray();
        var processedBatches = new ConcurrentBag<List<int>>();

        // Cleaner syntax - CT not needed in the body for simple batch processing
        await items.ForEachBatchParallelAsync(async batch =>
        {
            processedBatches.Add(batch);
            await Task.Delay(1);
        }, maxPerBatch: 10);

        var allProcessedItems = processedBatches.SelectMany(b => b).OrderBy(x => x).ToList();
        allProcessedItems.Should().BeEquivalentTo(items);
    }
}

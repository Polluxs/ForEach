using System.Collections.Concurrent;
using FluentAssertions;
using ForEach.Channel;

namespace Foreach.Test.Channel;

/// <summary>
/// Tests for convenience overloads that don't require CancellationToken in the body.
/// </summary>
public partial class ChannelsExtensionsTest
{
    /// <summary>
    /// Test that ForEachAsync overload without CT in body exists and works.
    /// Why: Sequential processing often doesn't need CT in the body - cleaner code.
    /// </summary>
    [Fact]
    public async Task ForEachAsync_WorksWithoutCancellationTokenInBody()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<int>();
        var processed = new List<int>();

        for (int i = 0; i < 10; i++)
            await channel.Writer.WriteAsync(i);
        channel.Writer.Complete();

        // Simpler syntax - no need for CT parameter when you don't use it
        await channel.ForEachAsync(async item =>
        {
            await Task.Delay(5);
            processed.Add(item);
        });

        processed.Should().HaveCount(10);
    }

    /// <summary>
    /// Test that ForEachParallelAsync overload without CT in body exists and works.
    /// Why: Parallel processing often doesn't need CT in the body - cleaner syntax.
    /// </summary>
    [Fact]
    public async Task ForEachParallelAsync_WorksWithoutCancellationTokenInBody()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<int>();
        var processed = new ConcurrentBag<int>();

        for (int i = 0; i < 20; i++)
            await channel.Writer.WriteAsync(i);
        channel.Writer.Complete();

        // No CT parameter needed in the body - cleaner API
        await channel.ForEachParallelAsync(async item =>
        {
            await Task.Delay(5);
            processed.Add(item);
        }, maxParallel: 4);

        processed.Should().HaveCount(20);
    }

    /// <summary>
    /// Test that ForEachParallelByKeyAsync overload without CT exists and works.
    /// Why: Per-key throttling logic often doesn't need CT in the body - simpler code.
    /// </summary>
    [Fact]
    public async Task ForEachParallelByKeyAsync_WorksWithoutCancellationTokenInBody()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<(char Key, int Value)>();
        var processed = new ConcurrentBag<int>();

        for (int i = 0; i < 20; i++)
            await channel.Writer.WriteAsync(((char)('A' + (i % 3)), i));
        channel.Writer.Complete();

        // Cleaner syntax when CT isn't needed in the processing logic
        await channel.ForEachParallelByKeyAsync(
            keySelector: it => it.Key,
            handler: async it =>
            {
                await Task.Delay(5);
                processed.Add(it.Value);
            },
            maxParallel: 10,
            maxPerKey: 2);

        processed.Should().HaveCount(20);
    }
}

using System.Threading.Channels;
using ForEach.AsyncEnumerable;

namespace ForEach.Channel;

public static class ChannelsExtensions
{
    /// <summary>
    /// Read all items from the channel as an async enumerable stream.
    /// </summary>
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this Channel<T> channel,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        while (reader.TryRead(out var item))
            yield return item;
    }

    /// <summary>
    /// Process each item from the channel sequentially (one at a time).
    /// </summary>
    public static Task ForEachAsync<T>(
        this Channel<T> channel,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            await foreach (var item in channel.ReadAllAsync(ct).ConfigureAwait(false))
                await handler(item, ct).ConfigureAwait(false);
        }, ct);
    }

    /// <summary>
    /// Process items from the channel in parallel with a global concurrency limit.
    /// </summary>
    public static Task ForEachParallelAsync<T>(
        this Channel<T> channel,
        Func<T, CancellationToken, ValueTask> handler,
        int maxParallel = 32,
        CancellationToken ct = default)
    {
        return channel.ReadAllAsync(ct).ForEachParallelAsync(handler, maxParallel, ct);
    }

    /// <summary>
    /// Process items from the channel in parallel with both global and per-key concurrency limits.
    /// </summary>
    public static Task ForEachParallelByKeyAsync<T, TKey>(
        this Channel<T> channel,
        Func<T, TKey> keySelector,
        Func<T, CancellationToken, ValueTask> handler,
        int maxParallel = 32,
        int maxPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        return channel.ReadAllAsync(ct)
            .ForEachParallelByKeyAsync(keySelector, handler, maxParallel, maxPerKey, ct);
    }

    /// <summary>
    /// Write all items from an async enumerable source to the channel.
    /// </summary>
    public static async Task WriteAllAsync<T>(
        this Channel<T> channel,
        IAsyncEnumerable<T> source,
        CancellationToken ct = default)
    {
        var writer = channel.Writer;
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            await writer.WriteAsync(item, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Write all items from an enumerable source to the channel.
    /// </summary>
    public static async Task WriteAllAsync<T>(
        this Channel<T> channel,
        IEnumerable<T> source,
        CancellationToken ct = default)
    {
        var writer = channel.Writer;
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteAsync(item, ct).ConfigureAwait(false);
        }
    }
}
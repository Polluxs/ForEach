using System.Threading.Channels;

namespace ForEach.Channel;

/// <summary>
/// Convenience overloads for <see cref="ChannelsExtensions"/> that don't require CancellationToken in the body.
/// </summary>
public static partial class ChannelsExtensions
{
    /// <summary>
    /// Process each item from the channel sequentially (one at a time).
    /// </summary>
    public static Task ForEachAsync<T>(
        this Channel<T> channel,
        Func<T, ValueTask> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return channel.ForEachAsync((item, _) => handler(item), ct);
    }

    /// <summary>
    /// Process items from the channel in parallel with a global concurrency limit.
    /// </summary>
    public static Task ForEachParallelAsync<T>(
        this Channel<T> channel,
        Func<T, ValueTask> handler,
        int maxParallel = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return channel.ForEachParallelAsync((item, _) => handler(item), maxParallel, ct);
    }

    /// <summary>
    /// Process items from the channel in parallel with both global and per-key concurrency limits.
    /// </summary>
    public static Task ForEachKeyParallelAsync<T, TKey>(
        this Channel<T> channel,
        Func<T, TKey> keySelector,
        Func<T, ValueTask> handler,
        int maxConcurrent = 32,
        int maxPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return channel.ForEachKeyParallelAsync(keySelector, (item, _) => handler(item), maxConcurrent, maxPerKey, ct);
    }

    /// <summary>
    /// Process items from the channel in batches with parallel batch processing.
    /// Items are grouped into batches of up to maxPerBatch items, and up to maxConcurrent batches are processed in parallel.
    /// </summary>
    public static Task ForEachBatchParallelAsync<T>(
        this Channel<T> channel,
        Func<List<T>, ValueTask> handler,
        int maxPerBatch,
        int maxConcurrent = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return channel.ForEachBatchParallelAsync((batch, _) => handler(batch), maxPerBatch, maxConcurrent, ct);
    }
}

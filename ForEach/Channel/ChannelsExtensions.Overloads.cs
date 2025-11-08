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
        Func<T, ValueTask> @delegate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@delegate);
        return channel.ForEachAsync((item, _) => @delegate(item), ct);
    }

    /// <summary>
    /// Process items from the channel in parallel with a global concurrency limit.
    /// </summary>
    public static Task ForEachParallelAsync<T>(
        this Channel<T> channel,
        Func<T, ValueTask> @delegate,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@delegate);
        return channel.ForEachParallelAsync((item, _) => @delegate(item), maxConcurrency, ct);
    }

    /// <summary>
    /// Process items from the channel in parallel with both global and per-key concurrency limits.
    /// </summary>
    public static Task ForEachKeyParallelAsync<T, TKey>(
        this Channel<T> channel,
        Func<T, TKey> @keySelector,
        Func<T, ValueTask> @delegate,
        int maxConcurrency = 32,
        int maxConcurrencyPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(@delegate);
        return channel.ForEachKeyParallelAsync(@keySelector, (item, _) => @delegate(item), maxConcurrency, maxConcurrencyPerKey, ct);
    }

    /// <summary>
    /// Process items from the channel in batches with parallel batch processing.
    /// Items are grouped into batches of up to batchSize items, and up to maxConcurrency batches are processed in parallel.
    /// </summary>
    public static Task ForEachBatchParallelAsync<T>(
        this Channel<T> channel,
        Func<List<T>, ValueTask> @delegate,
        int batchSize,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@delegate);
        return channel.ForEachBatchParallelAsync((batch, _) => @delegate(batch), batchSize, maxConcurrency, ct);
    }
}

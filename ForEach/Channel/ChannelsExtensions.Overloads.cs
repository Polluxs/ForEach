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
    public static Task ForEachParallelByKeyAsync<T, TKey>(
        this Channel<T> channel,
        Func<T, TKey> keySelector,
        Func<T, ValueTask> handler,
        int maxParallel = 32,
        int maxPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return channel.ForEachParallelByKeyAsync(keySelector, (item, _) => handler(item), maxParallel, maxPerKey, ct);
    }
}

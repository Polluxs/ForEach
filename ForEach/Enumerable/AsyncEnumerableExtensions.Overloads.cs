namespace ForEach.FeEnumerable;

/// <summary>
/// Convenience overloads for <see cref="AsyncEnumerableExtensions"/> that don't require CancellationToken in the body.
/// </summary>
public static partial class AsyncEnumerableExtensions
{
    /// <summary>
    /// Run asynchronous work for each item with a global concurrency cap.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">The items to process.</param>
    /// <param name="body">The async delegate to run per item.</param>
    /// <param name="maxParallel">Maximum number of concurrent operations.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task ForEachParallelAsync<T>(
        this IEnumerable<T> source,
        Func<T, ValueTask> body,
        int maxParallel = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return source.ForEachParallelAsync((item, _) => body(item), maxParallel, ct);
    }

    /// <summary>
    /// Parallel map: run asynchronous work for each item and collect results.
    /// Results are returned in arbitrary order.
    /// </summary>
    /// <typeparam name="T">Input item type.</typeparam>
    /// <typeparam name="TResult">Result item type.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="selector">Async transform that produces a result per item.</param>
    /// <param name="maxParallel">Maximum concurrency.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<List<TResult>> ForEachParallelAsync<T, TResult>(
        this IEnumerable<T> source,
        Func<T, ValueTask<TResult>> selector,
        int maxParallel = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return source.ForEachParallelAsync((item, _) => selector(item), maxParallel, ct);
    }

    /// <summary>
    /// Run asynchronous work with both a global cap and an additional per-key cap.
    /// Useful to prevent contention when multiple items map to the same key (e.g., account, tenant, host).
    /// </summary>
    /// <typeparam name="T">Input item type.</typeparam>
    /// <typeparam name="TKey">Key type for per-key throttling.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="keySelector">Selects the throttling key for an item.</param>
    /// <param name="body">The async delegate to run per item.</param>
    /// <param name="maxTotalParallel">Maximum number of items being processed concurrently across all keys.</param>
    /// <param name="maxPerKey">Maximum number of items being processed concurrently per key.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task ForEachParallelByKeyAsync<T, TKey>(
        this IEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, ValueTask> body,
        int maxTotalParallel = 32,
        int maxPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(body);
        return source.ForEachParallelByKeyAsync(keySelector, (item, _) => body(item), maxTotalParallel, maxPerKey, ct);
    }
}

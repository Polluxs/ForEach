namespace ForEach.AsyncEnumerable;

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
    /// <param name="maxConcurrency">Maximum number of concurrent operations.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task ForEachParallelAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<T, ValueTask> body,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return source.ForEachParallelAsync((item, _) => body(item), maxConcurrency, ct);
    }

    /// <summary>
    /// Parallel map: run asynchronous work for each item and collect results.
    /// Results are returned in arbitrary order.
    /// </summary>
    /// <typeparam name="T">Input item type.</typeparam>
    /// <typeparam name="TResult">Result item type.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="selector">Async transform that produces a result per item.</param>
    /// <param name="maxConcurrency">Maximum concurrency.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<List<TResult>> ForEachParallelAsync<T, TResult>(
        this IAsyncEnumerable<T> source,
        Func<T, ValueTask<TResult>> selector,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return source.ForEachParallelAsync((item, _) => selector(item), maxConcurrency, ct);
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
    /// <param name="maxConcurrency">Maximum number of items being processed concurrently across all keys.</param>
    /// <param name="maxConcurrencyPerKey">Maximum number of items being processed concurrently per key.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task ForEachKeyParallelAsync<T, TKey>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, ValueTask> body,
        int maxConcurrency = 32,
        int maxConcurrencyPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(body);
        return source.ForEachKeyParallelAsync(keySelector, (item, _) => body(item), maxConcurrency, maxConcurrencyPerKey, ct);
    }

    /// <summary>
    /// Process items in batches with parallel batch processing.
    /// Items are grouped into batches of up to batchSize items, and up to maxConcurrency batches are processed in parallel.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="body">The async delegate to run per batch. Receives a list of items in the batch.</param>
    /// <param name="batchSize">Maximum number of items per batch.</param>
    /// <param name="maxConcurrency">Maximum number of batches being processed concurrently.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task ForEachBatchParallelAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<List<T>, ValueTask> body,
        int batchSize,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return source.ForEachBatchParallelAsync((batch, _) => body(batch), batchSize, maxConcurrency, ct);
    }
}

using System.Collections.Concurrent;

namespace ForEach.AsyncEnumerable;

/// <summary>
/// Parallel helpers for running asynchronous work over <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
public static partial class AsyncEnumerableExtensions
{
    /// <summary>
    /// Run asynchronous work for each item with a global concurrency cap.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">The items to process.</param>
    /// <param name="delegate">The async delegate to run per item.</param>
    /// <param name="maxConcurrency">Maximum number of concurrent operations.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ForEachParallelAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<T, CancellationToken, ValueTask> @delegate,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(@delegate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrency, 0);

        var channel = System.Threading.Channels.Channel.CreateBounded<T>(maxConcurrency);

        // Producer: feed items from async enumerable into channel
        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        // Consumers: process items from channel
        var consumers = System.Linq.Enumerable.Range(0, maxConcurrency)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await @delegate(item, ct).ConfigureAwait(false);
                }
            }, ct))
            .ToArray();

        await Task.WhenAll(consumers.Append(producer));
    }

    /// <summary>
    /// Parallel map: run asynchronous work for each item and collect results.
    /// Results are returned in arbitrary order.
    /// </summary>
    /// <typeparam name="T">Input item type.</typeparam>
    /// <typeparam name="TResult">Result item type.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="delegate">Async transform that produces a result per item.</param>
    /// <param name="maxConcurrency">Maximum concurrency.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<List<TResult>> ForEachParallelAsync<T, TResult>(
        this IAsyncEnumerable<T> source,
        Func<T, CancellationToken, ValueTask<TResult>> @delegate,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(@delegate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrency, 0);

        var bag = new ConcurrentBag<TResult>();

        await source.ForEachParallelAsync(
            async (item, token) =>
            {
                var r = await @delegate(item, token);
                bag.Add(r);
            },
            maxConcurrency,
            ct);

        return bag.ToList();
    }

    /// <summary>
    /// Run asynchronous work with both a global cap and an additional per-key cap.
    /// Useful to prevent contention when multiple items map to the same key (e.g., account, tenant, host).
    /// </summary>
    /// <typeparam name="T">Input item type.</typeparam>
    /// <typeparam name="TKey">Key type for per-key throttling.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="keySelector">Selects the throttling key for an item.</param>
    /// <param name="delegate">The async delegate to run per item.</param>
    /// <param name="maxConcurrency">Maximum number of items being processed concurrently across all keys.</param>
    /// <param name="maxConcurrencyPerKey">Maximum number of items being processed concurrently per key.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ForEachKeyParallelAsync<T, TKey>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, CancellationToken, ValueTask> @delegate,
        int maxConcurrency = 32,
        int maxConcurrencyPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(@delegate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrency, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrencyPerKey, 0);

        var channel = System.Threading.Channels.Channel.CreateBounded<(T Item, TKey Key)>(maxConcurrency);
        var perKeyGates = new ConcurrentDictionary<TKey, SemaphoreSlim>();

        // Producer: feed items into channel with per-key guard
        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    var key = keySelector(item);

                    // Wait for available slot for this key
                    var gate = perKeyGates.GetOrAdd(key, _ => new SemaphoreSlim(maxConcurrencyPerKey, maxConcurrencyPerKey));
                    await gate.WaitAsync(ct);

                    // Push to channel (blocks if channel full = natural backpressure)
                    await channel.Writer.WriteAsync((item, key), ct);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        // Consumers: pull from channel, process, release key slot
        var consumers = System.Linq.Enumerable.Range(0, maxConcurrency)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var (item, key) in channel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        await @delegate(item, ct);
                    }
                    finally
                    {
                        // Release slot for key X
                        if (perKeyGates.TryGetValue(key, out var gate))
                        {
                            gate.Release();
                        }
                    }
                }
            }, ct))
            .ToArray();

        try
        {
            await Task.WhenAll(consumers.Append(producer));
        }
        finally
        {
            foreach (var gate in perKeyGates.Values)
            {
                gate.Dispose();
            }
        }
    }

    /// <summary>
    /// Process items in batches with parallel batch processing.
    /// Items are grouped into batches of up to batchSize items, and up to maxConcurrency batches are processed in parallel.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Items to process.</param>
    /// <param name="delegate">The async delegate to run per batch. Receives a list of items in the batch.</param>
    /// <param name="batchSize">Maximum number of items per batch.</param>
    /// <param name="maxConcurrency">Maximum number of batches being processed concurrently.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ForEachBatchParallelAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<List<T>, CancellationToken, ValueTask> @delegate,
        int batchSize,
        int maxConcurrency = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(@delegate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrency, 0);

        var batchChannel = System.Threading.Channels.Channel.CreateBounded<List<T>>(maxConcurrency);

        // Producer: read items and create batches
        var producer = Task.Run(async () =>
        {
            try
            {
                var currentBatch = new List<T>(batchSize);

                await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
                {
                    currentBatch.Add(item);

                    if (currentBatch.Count >= batchSize)
                    {
                        await batchChannel.Writer.WriteAsync(currentBatch, ct).ConfigureAwait(false);
                        currentBatch = new List<T>(batchSize);
                    }
                }

                // Write any remaining items as the final batch
                if (currentBatch.Count > 0)
                {
                    await batchChannel.Writer.WriteAsync(currentBatch, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                batchChannel.Writer.Complete();
            }
        }, ct);

        // Consumers: process batches in parallel
        var consumers = System.Linq.Enumerable.Range(0, maxConcurrency)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var batch in batchChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await @delegate(batch, ct).ConfigureAwait(false);
                }
            }, ct))
            .ToArray();

        await Task.WhenAll(consumers.Append(producer));
    }
}
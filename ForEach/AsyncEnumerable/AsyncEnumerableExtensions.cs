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
    /// <param name="body">The async delegate to run per item.</param>
    /// <param name="maxParallel">Maximum number of concurrent operations.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ForEachParallelAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<T, CancellationToken, ValueTask> body,
        int maxParallel = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxParallel, 0);

        var channel = System.Threading.Channels.Channel.CreateBounded<T>(maxParallel);

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
        var consumers = Enumerable.Range(0, maxParallel)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await body(item, ct).ConfigureAwait(false);
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
    /// <param name="selector">Async transform that produces a result per item.</param>
    /// <param name="maxParallel">Maximum concurrency.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<List<TResult>> ForEachParallelAsync<T, TResult>(
        this IAsyncEnumerable<T> source,
        Func<T, CancellationToken, ValueTask<TResult>> selector,
        int maxParallel = 32,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxParallel, 0);

        var bag = new ConcurrentBag<TResult>();

        await source.ForEachParallelAsync(
            async (item, token) =>
            {
                var r = await selector(item, token);
                bag.Add(r);
            },
            maxParallel,
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
    /// <param name="body">The async delegate to run per item.</param>
    /// <param name="maxTotalParallel">Maximum number of items being processed concurrently across all keys.</param>
    /// <param name="maxPerKey">Maximum number of items being processed concurrently per key.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ForEachParallelByKeyAsync<T, TKey>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, CancellationToken, ValueTask> body,
        int maxTotalParallel = 32,
        int maxPerKey = 4,
        CancellationToken ct = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxTotalParallel, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPerKey, 0);

        var channel = System.Threading.Channels.Channel.CreateBounded<(T Item, TKey Key)>(maxTotalParallel);
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
                    var gate = perKeyGates.GetOrAdd(key, _ => new SemaphoreSlim(maxPerKey, maxPerKey));
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
        var consumers = Enumerable.Range(0, maxTotalParallel)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var (item, key) in channel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        await body(item, ct);
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
}
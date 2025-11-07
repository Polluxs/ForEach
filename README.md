# ForEach

Extension methods that add parallel `ForEach` iterations to `IEnumerable<T>`, `IAsyncEnumerable<T>`, and `Channel<T>`.

```csharp
using ForEach.Enumerable;

await files.ForEachParallelAsync(async (file, ct) =>
    await ProcessAsync(file, ct), maxParallel: 10);
```

## Methods

**For `IEnumerable<T>`, `IAsyncEnumerable<T>`, and `Channel<T>`:**

| Method | Purpose |
|:--|:--|
| [`ForEachParallelAsync`](#foreachparallelasync) | Process items concurrently with a global limit |
| [`ForEachParallelAsync<T,TResult>`](#foreachparallelasyncttresult) | Process items concurrently and collect results |
| [`ForEachParallelByKeyAsync`](#foreachparallelbykeysync) | Process items with both global and per-key concurrency limits |

**For `Channel<T>` only:**

| Method | Purpose |
|:--|:--|
| [`ForEachAsync`](#foreachasync) | Process items sequentially |
| [`ReadAllAsync`](#readallasync) | Convert channel to `IAsyncEnumerable<T>` |
| [`WriteAllAsync`](#writeallasync) | Write items from `IEnumerable<T>` or `IAsyncEnumerable<T>` into channel |


---
## Examples

### ForEachParallelAsync

Run async operations for an enumerable with a concurrency limit.

```csharp
using ForEach.Enumerable; // or ForEach.AsyncEnumerable

await files.ForEachParallelAsync(async (path, ct) =>
{
    var content = await File.ReadAllTextAsync(path, ct);
    var upper = content.ToUpperInvariant();
    await File.WriteAllTextAsync($"{path}.out", upper, ct);
}, maxParallel: 8);
```

- Global cap via `maxParallel`
- Honors cancellation
- Exception behavior:
  - `IEnumerable<T>`: Aggregates via `Parallel.ForEachAsync` → `AggregateException`
  - `IAsyncEnumerable<T>`: Aggregates via `Task.WhenAll` → `AggregateException`


### ForEachParallelAsync (with results)

Process items concurrently and collect results.

```csharp
var results = await urls.ForEachParallelAsync(async (url, ct) =>
{
    using var r = await http.GetAsync(url, ct);
    return (url, r.StatusCode);
}, maxParallel: 16);

foreach (var (url, code) in results)
    Console.WriteLine($"{url} → {code}");
```

- Output order is arbitrary (not guaranteed)
- Works with any return type
- Uses `ConcurrentBag` under the hood
- Exception aggregation same as `ForEachParallelAsync` (inherits from `Parallel.ForEachAsync`)


### `ForEachParallelByKeyAsync`

Limit concurrency globally AND per key (e.g., by user, account, or host).

```csharp
await jobs.ForEachParallelByKeyAsync(
    keySelector: j => j.AccountId,
    body: async (job, ct) =>
    {
        await HandleJobAsync(job, ct);
    },
    maxTotalParallel: 64,
    maxPerKey: 2);
```

- Global cap = `maxTotalParallel` (actual items being processed concurrently)
- Per-key cap = `maxPerKey` (items per key being processed concurrently)
- Effective per-key limit = `min(maxTotalParallel, maxPerKey)` - if `maxPerKey > maxTotalParallel`, the global limit wins
- Uses bounded channel + per-key semaphores for efficient throttling
- Enumerates source only once (no materialization required)
- Aggregates exceptions via `Task.WhenAll` - multiple failures collected into an `AggregateException`


---

## In-depth: Additional Methods

### Channel Helper Methods

#### ReadAllAsync() - Read items as async stream
```csharp
using ForEach.Channel;

// Without: verbose channel reader access
while (await channel.Reader.WaitToReadAsync())
while (channel.Reader.TryRead(out var item))
    Process(item);

// With: simple foreach
await foreach (var item in channel.ReadAllAsync())
    Process(item);
```

#### WriteAllAsync() - Write all items from a source
```csharp
var channel = Channel.CreateUnbounded<int>();

// From IEnumerable<T>
await channel.WriteAllAsync(Enumerable.Range(1, 100));

// From IAsyncEnumerable<T>
await channel.WriteAllAsync(FetchDataAsync());

// Note: You must manually complete the channel when done writing
channel.Writer.Complete();
```

#### ForEachAsync() - Process items sequentially
```csharp
// Process each item one at a time (sequential)
await channel.ForEachAsync(async (item, ct) =>
{
    // Items processed in order, one after another
    await ProcessAsync(item, ct);
});
```

---

## License

MIT = copy, use, modify, ignore.

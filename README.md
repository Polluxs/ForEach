# FlowControl

Helpers for running async work in parallel.
.NET 8 only, use at own risk, just sugar syntax for fast fun to get things done.

## Why This Exists

I love .NET to make things "quick and dirty" which is maybe ironic as .NET is typically known as a rather "enterprise" heavy language. Anyway from that perspective I love .NET to do things quick. Hence I am making a fun package for myself to build things "faster". This is what I am just "collecting" here. Feel free to take a look!

Also having fun with AI building this, so it might not be up to standards. I want to get an idea first of what I want and build it "quick" - planning to only fix things as I go. It's more for personal "get it done quick" than to share.

## 1-shot Concurrency

One-off parallel processing over collections. Run once, get results, done.

**Works with both:**
- `IEnumerable<T>` - Regular collections (use `FlowControl.FlowEnumerable`)
- `IAsyncEnumerable<T>` - Async streams (use `FlowControl.FlowAsyncEnumerable`)

**Key difference:**
- `IEnumerable<T>` uses `Parallel.ForEachAsync` under the hood - best for in-memory collections
- `IAsyncEnumerable<T>` uses channels for producer-consumer pattern - best for streaming data (database queries, HTTP responses, file reads)

| Method | Purpose                                              |
|:--|:-----------------------------------------------------|
| [`ParallelAsync`](#parallelasync) | Run concurrently                                     |
| [`ParallelAsync<T,TResult>`](#parallelasyncttresult) | Run concurrently and collect results                 |
| [`ParallelByKeyAsync`](#parallelasyncbykey) | Limit concurrent items per key, for example: limit requests per host |

## Continuous Concurrency

Ongoing stream processing with `Channel<T>`. Use channels for long-running pipelines, producer-consumer patterns, or
when you need backpressure control.

**All operations work on `Channel<T>` directly** - no need to type `.Reader` or `.Writer` every time.

**Basic Operations:**

| Operation                                                                                          | Purpose                                                                           |
|:---------------------------------------------------------------------------------------------------|:----------------------------------------------------------------------------------|
| [`channel.WriteAllAsync()`](#writeallasync---write-all-items-from-a-source)                  | Fill from source                                                                  |
| [`channel.ReadAllAsync()`](#readallasync---read-items-as-async-stream) | Process sequentially |
| [`channel.ForEachAsync()`](#foreachasync---process-items-sequentially)          | Process sequentially |

**Concurrency Operations:**

| Operation                                                                                       | Purpose |
|:------------------------------------------------------------------------------------------------|:--|
| [`channel.ParallelAsync()`](#parallelasync---process-items-in-parallel)            | Process concurrently |
| [`channel.ParallelByKeyAsync()`](#parallelasyncbykey---per-key-concurrency-limits) | Limit per key |

**Note:** `channel.LinkTo(otherChannel, filter)` was considered for routing items between channels,
but would require a different channel implementation to avoid infinite read loops when items are
written back to the source. May explore this in a future version.


---
## In depth: 1-shot
### `ParallelAsync`

Run async operations for an enumerable with a concurrency limit.

```csharp
using FlowControl.Enumerable; // or FlowControl.AsyncEnumerable

await files.ParallelAsync(async (path, ct) =>
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


### `ParallelAsync<T,TResult>`

Same, but returns results.

```csharp
var results = await urls.ParallelAsync(async (url, ct) =>
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
- Exception aggregation same as `ParallelAsync` (inherits from `Parallel.ForEachAsync`)


### `ParallelByKeyAsync`

Limit concurrency globally AND per key (e.g., by user, account, or host).

```csharp
await jobs.ParallelByKeyAsync(
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

## In-depth: Continuous Concurrency

### Basic Operations

#### ReadAllAsync() - Read items as async stream
```csharp
using FlowControl.Channel;

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

### Concurrency Operations

#### ForEachAsync() - Process items sequentially
```csharp
// Process each item one at a time (sequential)
await channel.ForEachAsync(async (item, ct) =>
{
    // Items processed in order, one after another
    await ProcessAsync(item, ct);
});
```

#### ParallelAsync() - Process items in parallel
```csharp
// Process multiple items concurrently with a global cap
await channel.ParallelAsync(async (item, ct) =>
{
    // Up to 8 items being processed at the same time
    await ProcessAsync(item, ct);
}, maxParallel: 8);
```

#### ParallelByKeyAsync() - Per-key concurrency limits
```csharp
// Limit concurrent requests per host
await channel.ParallelByKeyAsync(
    keySelector: item => item.Host,
    handler: async (item, ct) =>
    {
        // Global limit: max 64 requests at once
        // Per-host limit: max 2 requests per host at once
        await SendRequestAsync(item, ct);
    },
    maxParallel: 64,
    maxPerKey: 2);
```

---

## License

MIT — copy, use, modify, ignore.

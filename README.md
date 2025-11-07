# FlowControl

Helpers for running async work in parallel.
.NET 8 only, use at own risk, just sugar syntax for fast fun to get things done.

---

## Why This Exists

I love .NET to make things "quick and dirty" which is maybe ironic as .NET is typically known as a rather "enterprise" heavy language. Anyway from that perspective I love .NET to do things quick. Hence I am making a fun package for myself to build things "faster". This is what I am just "collecting" here. Feel free to take a look!

Also having fun with AI building this, so it might not be up to standards. I want to get an idea first of what I want and build it "quick" - planning to only fix things as I go. It's more for personal "get it done quick" than to share.

---

## 1-shot Concurrency

One-off parallel processing over collections. Run once, get results, done.

**Works with both:**
- `IEnumerable<T>` - Regular collections (use `FlowControl.FlowEnumerable`)
- `IAsyncEnumerable<T>` - Async streams (use `FlowControl.FlowAsyncEnumerable`)

**Key difference:**
- `IEnumerable<T>` uses `Parallel.ForEachAsync` under the hood - best for in-memory collections
- `IAsyncEnumerable<T>` uses channels for producer-consumer pattern - best for streaming data (database queries, HTTP responses, file reads)

| Method | Purpose |
|:--|:--|
| [`ParallelAsync`](#parallelasync) | Run async work concurrently with a global cap |
| [`ParallelAsync<T,TResult>`](#parallelasyncttresult) | Run async work and collect results |
| [`ParallelAsyncByKey`](#parallelasyncbykey) | Run async work with per-key concurrency limits |

---

## `ParallelAsync`

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

---

## `ParallelAsync<T,TResult>`

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

---

## `ParallelAsyncByKey`

Limit concurrency globally AND per key (e.g., by user, account, or host).

```csharp
await jobs.ParallelAsyncByKey(
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

## Continuous Concurrency

Ongoing stream processing with `Channel<T>`. Use channels for long-running pipelines, producer-consumer patterns, or when you need backpressure control.

**All operations work on `Channel<T>` directly** - no need to type `.Reader` or `.Writer` every time.

| Operation | Purpose |
|:--|:--|
| [`ReadAllAsync()`](#readallasynch---read-all-items-as-iasyncenumerable) | Read items as async stream |
| [`ForEachAsync()`](#foreachasync---process-items-sequentially) | Process sequentially |
| [`ParallelAsync()`](#parallelasync---process-items-in-parallel) | Process in parallel |
| [`ParallelAsyncByKey()`](#parallelasyncbykey---per-key-concurrency-limits) | Per-key concurrency |
| [`LinkTo()`](#linkto---pipe-items-to-another-channel) | Pipe to another channel |
| [`WriteAllAsync()`](#writeallasync---write-all-items-from-a-source) | Write from source |

---

## Channel Extensions

### Reading Operations

**ReadAllAsync() - Read all items as IAsyncEnumerable**
```csharp
using FlowControl.Channel;

var channel = Channel.CreateUnbounded<int>();

// Read all items from channel as async stream
await foreach (var item in channel.ReadAllAsync())
{
    Console.WriteLine(item);
}
```

**ForEachAsync() - Process items sequentially**
```csharp
// Process each item one at a time (sequential)
await channel.ForEachAsync(async (item, ct) =>
{
    // Items processed in order, one after another
    await ProcessAsync(item, ct);
});
```

**ParallelAsync() - Process items in parallel**
```csharp
// Process multiple items concurrently with a global cap
await channel.ParallelAsync(async (item, ct) =>
{
    // Up to 8 items being processed at the same time
    await ProcessAsync(item, ct);
}, maxParallel: 8);
```

**ParallelAsyncByKey() - Per-key concurrency limits**
```csharp
// Limit concurrency globally AND per key (e.g., per account, user, or host)
await channel.ParallelAsyncByKey(
    keySelector: item => item.AccountId,
    handler: async (item, ct) =>
    {
        // Global limit: max 64 items processing at once
        // Per-key limit: max 2 items per account at once
        await ProcessAsync(item, ct);
    },
    maxParallel: 64,
    maxPerKey: 2);
```

**LinkTo() - Pipe items to another channel**
```csharp
var sourceChannel = Channel.CreateUnbounded<int>();
var targetChannel = Channel.CreateUnbounded<int>();

// Pipe all items to target channel
await sourceChannel.LinkTo(targetChannel.Writer);
// Target channel is automatically completed when source is done

// Or with filtering
await sourceChannel.LinkTo(
    targetChannel.Writer,
    filter: x => x > 10  // Only items > 10 are forwarded
);
// Target is still auto-completed after filtered items
```

### Writing Operations

**WriteAllAsync() - Write all items from a source**
```csharp
var channel = Channel.CreateUnbounded<int>();

// From IEnumerable<T>
await channel.WriteAllAsync(Enumerable.Range(1, 100));

// From IAsyncEnumerable<T>
await channel.WriteAllAsync(FetchDataAsync());

// Note: You must manually complete the channel when done writing
channel.Writer.Complete();
```

---

## License

MIT — copy, use, modify, ignore.

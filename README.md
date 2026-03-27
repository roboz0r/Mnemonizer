# Mnemonizer

A .NET library that maps opaque system identifiers (GUIDs, database keys, etc.) to memorable, deterministic aliases in the form **adjective-adjective-noun** (e.g. `bright-quick-falcon`).

Designed for LLM tool-call pipelines where raw IDs waste context tokens and confuse the model.

## Installation

```shell
dotnet add package Mnemonizer
```

Targets **netstandard2.0** and **net10.0**.

## Quick start

```csharp
var mapper = new MnemonicDictionary<string>();

// Map an ID to a readable alias
string alias = mapper.GetOrAddAlias("usr_8f3a2b1c");
// e.g. "bright-quick-falcon"

// Same ID always returns the same alias
mapper.GetOrAddAlias("usr_8f3a2b1c"); // "bright-quick-falcon"

// Reverse lookup
if (mapper.TryGetId(alias, out var id))
{
    // id == "usr_8f3a2b1c"
}
```

```fsharp
let mapper = MnemonicDictionary<string>()

let alias = mapper.GetOrAddAlias("usr_8f3a2b1c")

let mutable id = Unchecked.defaultof<string>
if mapper.TryGetId(alias, &id) then
    printfn $"Resolved: {id}"
```

## How it works

1. The identifier's hash code is bit-packed into three indices (11 + 11 + 10 = 32 bits) selecting from **2048 adjectives** and **1024 nouns**, yielding ~4 billion unique aliases.
2. Hash collisions are resolved via linear probing.
3. Aliases are cached after first generation -- repeated lookups are zero-allocation.
4. Reverse lookups (`TryGetId`) parse the alias back into indices and reconstruct the internal key, which is then used to look up the original ID in the internal dictionary in O(1) time.

## Thread safety

`MnemonicDictionary<T>` is fully thread-safe. All mutable state is managed through `ConcurrentDictionary` with lock-free atomic operations. Multiple threads may call `GetOrAddAlias` and `TryGetId` concurrently without external synchronization.

## Performance

Both hot paths are **zero-allocation** on .NET 9+:

- **`GetOrAddAlias`** (existing ID): returns a cached string reference, ~15ns per call.
- **`TryGetId`**: parses the alias using `ReadOnlySpan<char>` slicing and `FrozenDictionary.AlternateLookup` -- no string splits, no intermediate allocations, ~40ns per call.

On netstandard2.0, `GetOrAddAlias` is still zero-allocation for cached lookups. `TryGetId` falls back to `string.Split` which allocates.

## Important: aliases are ephemeral

Aliases are **scoped to a single `MnemonicDictionary<T>` instance**. The same identifier may produce a different alias:

- **Across processes** -- .NET randomizes `string.GetHashCode()` by default, so hash codes differ between runs.
- **Within the same process** -- if identifiers are inserted in a different order, linear probing for hash collisions may assign different slots.

Do not persist, serialize, or transmit aliases. They are designed for in-memory, single-session use (e.g. the lifetime of an LLM conversation).

> **Note:** For `int` keys, aliases are fully deterministic — `int.GetHashCode()` is the identity function, so there are no collisions and no insertion-order dependence. Other types with deterministic hash codes (`long`, `Guid`, etc.) will also produce stable aliases across instances, provided there are no hash collisions. The randomization caveat is primarily about `string` on .NET Core+.

## Custom word lists and hashing

The parameterless constructor uses built-in word lists embedded in the assembly and hashing with `EqualityComparer<T>.Default`. To supply your own:

```csharp
string[] adjectives = ...; // exactly 2048 entries
string[] nouns = ...;      // exactly 1024 entries
IEqualityComparer<T> keyComparer = ...

var mapper = new MnemonicDictionary<T>(adjectives, nouns, keyComparer);
```

Word lookups are case-insensitive.

## License

See [LICENSE](LICENSE) for details.

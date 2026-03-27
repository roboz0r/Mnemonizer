namespace Mnemonizer.Benchmarks

open System
open BenchmarkDotNet.Attributes
open Mnemonizer

[<MemoryDiagnoser>]
type GetOrAddAliasBenchmarks() =

    let mutable mapper = Unchecked.defaultof<MnemonicDictionary<string>>
    let mutable existingIds = Array.empty<string>
    let mutable newIds = Array.empty<string>

    [<Params(1, 100, 1000, 100_000)>]
    member val IdCount = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        mapper <- MnemonicDictionary<string>()
        existingIds <- [| for i in 1 .. this.IdCount -> $"preloaded-{i}" |]
        newIds <- [| for i in 1 .. this.IdCount -> $"new-{i}" |]
        // Pre-register all ids so we can benchmark the lookup-hit path
        for id in existingIds do
            mapper.GetOrAddAlias(id) |> ignore

    /// Measures the cost of looking up an already-registered id (steady-state hot path).
    [<Benchmark(Baseline = true)>]
    member _.LookupExisting() =
        let mutable last = ""

        for id in existingIds do
            last <- mapper.GetOrAddAlias(id)

        last

    /// Measures the cost of registering a new id (cold path, includes ConcurrentDictionary insert).
    [<Benchmark>]
    member this.InsertNew() =
        // Fresh mapper each time to measure insert cost
        let fresh = MnemonicDictionary<string>()
        let mutable last = ""

        for i in 0 .. this.IdCount - 1 do
            last <- fresh.GetOrAddAlias(newIds.[i])

        last

[<MemoryDiagnoser>]
type TryGetIdBenchmarks() =

    let mutable mapper = Unchecked.defaultof<MnemonicDictionary<string>>
    let mutable aliases = Array.empty<string>
    let mutable upperAliases = Array.empty<string>

    [<Params(1, 100, 1000, 100_000)>]
    member val IdCount = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        mapper <- MnemonicDictionary<string>()
        let ids = [| for i in 1 .. this.IdCount -> $"id-{i}" |]
        aliases <- ids |> Array.map mapper.GetOrAddAlias
        upperAliases <- aliases |> Array.map (fun a -> a.ToUpperInvariant())

    /// Measures reverse lookup of known aliases (exact case).
    [<Benchmark(Baseline = true)>]
    member _.LookupExact() =
        let mutable found = false
        let mutable result = Unchecked.defaultof<string>

        for alias in aliases do
            found <- mapper.TryGetId(alias, &result)

        found

    /// Measures reverse lookup with case mismatch (exercises case-insensitive comparison).
    [<Benchmark>]
    member _.LookupCaseInsensitive() =
        let mutable found = false
        let mutable result = Unchecked.defaultof<string>

        for alias in upperAliases do
            found <- mapper.TryGetId(alias, &result)

        found

    /// Measures reverse lookup of an alias that isn't registered.
    [<Benchmark>]
    member _.LookupMiss() =
        let mutable found = false
        let mutable result = Unchecked.defaultof<string>

        for _ in 1 .. aliases.Length do
            found <- mapper.TryGetId("unknown-fake-alias", &result)

        found

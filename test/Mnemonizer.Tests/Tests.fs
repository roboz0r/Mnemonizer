module Mnemonizer.Tests.Tests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Expecto
open Mnemonizer

[<Tests>]
let constructorTests =
    testList
        "Constructor"
        [
            test "Default constructor loads embedded word lists" {
                let mapper = MnemonicDictionary<string>()
                let alias = mapper.GetOrAddAlias("test-id")
                Expect.isNotEmpty alias "Should produce a non-empty alias"
            }

            test "Rejects adjectives array with wrong length" {
                Expect.throws
                    (fun () ->
                        MnemonicDictionary<string>(Array.zeroCreate 100, Array.zeroCreate 1024)
                        |> ignore
                    )
                    "Should throw for wrong adjective count"
            }

            test "Rejects nouns array with wrong length" {
                Expect.throws
                    (fun () ->
                        MnemonicDictionary<string>(Array.zeroCreate 2048, Array.zeroCreate 100)
                        |> ignore
                    )
                    "Should throw for wrong noun count"
            }
        ]

[<Tests>]
let aliasTests =
    testList
        "GetOrAddAlias"
        [
            test "Returns three-part hyphenated alias" {
                let mapper = MnemonicDictionary<string>()
                let alias = mapper.GetOrAddAlias("my-id")
                let parts = alias.Split('-')
                Expect.equal parts.Length 3 "Alias should have exactly 3 parts"
            }

            test "Same id returns same alias" {
                let mapper = MnemonicDictionary<string>()
                let a1 = mapper.GetOrAddAlias("id-1")
                let a2 = mapper.GetOrAddAlias("id-1")
                Expect.equal a1 a2 "Same id should always produce the same alias"
            }

            test "Different ids return different aliases" {
                let mapper = MnemonicDictionary<string>()
                let a1 = mapper.GetOrAddAlias("id-1")
                let a2 = mapper.GetOrAddAlias("id-2")
                Expect.notEqual a1 a2 "Different ids should produce different aliases"
            }
        ]

[<Tests>]
let tryGetIdTests =
    testList
        "TryGetId"
        [
            test "Round-trips an id through alias and back" {
                let mapper = MnemonicDictionary<string>()
                let alias = mapper.GetOrAddAlias("round-trip")
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId(alias, &result)
                Expect.isTrue found "Should find the id from its alias"
                Expect.equal result "round-trip" "Should return the original id"
            }

            test "Returns false for unknown alias" {
                let mapper = MnemonicDictionary<string>()
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId("nonexistent-fake-alias", &result)
                Expect.isFalse found "Should not find an id for an unknown alias"
            }

            test "Returns false for null" {
                let mapper = MnemonicDictionary<string>()
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId(null, &result)
                Expect.isFalse found "Should return false for null"
            }

            test "Returns false for empty string" {
                let mapper = MnemonicDictionary<string>()
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId("", &result)
                Expect.isFalse found "Should return false for empty string"
            }

            test "Returns false for malformed alias (no hyphens)" {
                let mapper = MnemonicDictionary<string>()
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId("nohyphens", &result)
                Expect.isFalse found "Should return false for malformed alias"
            }

            test "Returns false for malformed alias (one hyphen)" {
                let mapper = MnemonicDictionary<string>()
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId("one-hyphen", &result)
                Expect.isFalse found "Should return false for alias with only one hyphen"
            }

            test "Case-insensitive lookup" {
                let mapper = MnemonicDictionary<string>()
                let alias = mapper.GetOrAddAlias("case-test")
                let upper = alias.ToUpperInvariant()
                let mutable result = Unchecked.defaultof<string>
                let found = mapper.TryGetId(upper, &result)
                Expect.isTrue found "Lookup should be case-insensitive"
                Expect.equal result "case-test" "Should return the original id"
            }

            test "Some mnemonic values" {
                let mapper = MnemonicDictionary<int>()
                let testIds = [| 1; 234; 567890; Int32.MaxValue; Int32.MinValue |]

                let expectedAliases =
                    [|
                        "forced-junior-clock"
                        "piny-price-boon"
                        "jade-elated-crisp"
                        "wide-gritty-prawn"
                        "inner-telling-gust"
                    |]

                let aliases = testIds |> Array.map mapper.GetOrAddAlias
                Expect.equal aliases expectedAliases "Should produce expected aliases for test ids"
            }
        ]

[<Tests>]
let collisionTests =
    testList
        "Collision handling"
        [
            test "Handles many ids without losing any" {
                let mapper = MnemonicDictionary<string>()
                let ids = [| for i in 1..100 -> $"id-{i}" |]
                let aliases = ids |> Array.map mapper.GetOrAddAlias
                // Expect.equal (List.ofArray aliases) [] "Should return an alias for every id"

                // All aliases should be unique
                let uniqueAliases = aliases |> Array.distinct
                Expect.equal uniqueAliases.Length ids.Length "All aliases should be unique"

                // All should round-trip
                for i in 0 .. ids.Length - 1 do
                    let mutable result = Unchecked.defaultof<string>
                    let found = mapper.TryGetId(aliases.[i], &result)
                    Expect.isTrue found $"Should find id for alias {i} {aliases.[i]}"
                    Expect.equal result ids.[i] $"Should round-trip id-{i + 1}"
            }
        ]

[<Tests>]
let threadSafetyTests =
    testList
        "Thread safety"
        [
            test "Concurrent inserts produce unique aliases that all round-trip" {
                let mapper = MnemonicDictionary<string>()
                let idCount = 10_000
                let threadCount = Environment.ProcessorCount
                let idsPerThread = idCount / threadCount
                // Each thread gets its own slice of IDs
                let allAliases = ConcurrentDictionary<string, string>()

                let tasks =
                    [|
                        for t in 0 .. threadCount - 1 ->
                            Task.Run(fun () ->
                                for i in 0 .. idsPerThread - 1 do
                                    let id = $"thread{t}-id{i}"
                                    let alias = mapper.GetOrAddAlias(id)
                                    allAliases.[id] <- alias
                            )
                    |]

                Task.WaitAll(tasks)

                // Every ID got an alias
                Expect.equal allAliases.Count (threadCount * idsPerThread) "All IDs should have aliases"

                // All aliases are unique
                let uniqueAliases = allAliases.Values |> Seq.distinct |> Seq.length
                Expect.equal uniqueAliases allAliases.Count "All aliases should be unique"

                // Every alias round-trips
                for kvp in allAliases do
                    let mutable result = Unchecked.defaultof<string>
                    let found = mapper.TryGetId(kvp.Value, &result)
                    Expect.isTrue found $"Should find id for alias {kvp.Value}"
                    Expect.equal result kvp.Key $"Should round-trip {kvp.Key}"
            }

            test "Same id from multiple threads always returns the same alias" {
                let mapper = MnemonicDictionary<string>()
                let threadCount = Environment.ProcessorCount
                let aliases = Array.zeroCreate<string> threadCount

                // Use a barrier so all threads call GetOrAddAlias at the same time
                use barrier = new Barrier(threadCount)

                let tasks =
                    [|
                        for t in 0 .. threadCount - 1 ->
                            Task.Run(fun () ->
                                barrier.SignalAndWait()
                                aliases.[t] <- mapper.GetOrAddAlias("contested-id")
                            )
                    |]

                Task.WaitAll(tasks)

                let distinct = aliases |> Array.distinct
                Expect.equal distinct.Length 1 "All threads should get the same alias"
            }

            test "Concurrent reads and writes don't corrupt state" {
                let mapper = MnemonicDictionary<string>()
                let idCount = 1_000
                // Pre-load some IDs
                let preloadedIds = [| for i in 1..idCount -> $"preloaded-{i}" |]
                let preloadedAliases = preloadedIds |> Array.map mapper.GetOrAddAlias

                let errors = ConcurrentBag<string>()
                let cts = new CancellationTokenSource(TimeSpan.FromSeconds(2.0))

                // Readers: continuously verify pre-loaded aliases round-trip
                let readers =
                    [|
                        for _ in 1 .. Environment.ProcessorCount / 2 ->
                            Task.Run(fun () ->
                                while not cts.Token.IsCancellationRequested do
                                    for i in 0 .. idCount - 1 do
                                        let mutable result = Unchecked.defaultof<string>
                                        let found = mapper.TryGetId(preloadedAliases.[i], &result)

                                        if not found then
                                            errors.Add($"TryGetId failed for {preloadedAliases.[i]}")
                                        elif result <> preloadedIds.[i] then
                                            errors.Add(
                                                $"Wrong id for {preloadedAliases.[i]}: expected {preloadedIds.[i]}, got {result}"
                                            )
                            )
                    |]

                // Writers: continuously add new IDs
                let writerCount = ref 0

                let writers =
                    [|
                        for t in 1 .. Environment.ProcessorCount / 2 ->
                            Task.Run(fun () ->
                                let mutable i = 0

                                while not cts.Token.IsCancellationRequested do
                                    let id = $"writer{t}-{i}"
                                    let alias = mapper.GetOrAddAlias(id)
                                    let mutable result = Unchecked.defaultof<string>

                                    if mapper.TryGetId(alias, &result) then
                                        if result <> id then
                                            errors.Add($"Wrong id for new alias {alias}: expected {id}, got {result}")
                                    else
                                        errors.Add($"TryGetId failed for newly inserted {alias}")

                                    i <- i + 1
                                    Interlocked.Increment(writerCount) |> ignore
                            )
                    |]

                Task.WaitAll(Array.append readers writers)

                Expect.isEmpty (errors |> Seq.toList) $"No errors should occur ({writerCount.Value} writes performed)"
            }
        ]

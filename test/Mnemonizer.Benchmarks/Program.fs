module Mnemonizer.Benchmarks.Program

open System.Reflection
open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(argv)
    |> ignore

    0

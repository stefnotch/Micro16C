﻿open System.IO
open Micro16C.Backend
open Micro16C.Frontend
open Micro16C.MiddleEnd

let debugModulePasses title (irModule: IR.Module ref) =
#if DEBUG
    printf "%s\n%s\n" title ((!irModule) |> IR.Module.asText)
#endif
    irModule

[<EntryPoint>]
let main argv =
    if argv |> Array.isEmpty then
        eprintfn "Expected file name as first parameter"
        -1
    else
        let fileName = argv.[0]
        let text = File.ReadAllText fileName

        let result =
            Lex.tokenize text
            |> Result.bind Parse.parse
            |> Result.bind Sema.analyse
            |> Result.map Codegen.codegen
            |> Result.map (debugModulePasses "Before optimizations:")
            |> Result.map Passes.instructionSimplify
            |> Result.map Passes.instructionCombine
            |> Result.map Passes.deadCodeElimination
            |> Result.map Passes.simplifyCFG
            |> Result.map Passes.analyzeAlloc
            |> Result.map Passes.analyzeDominance
            |> Result.map Passes.analyzeDominanceFrontiers
            |> Result.map Passes.mem2reg
            |> Result.map Passes.deadCodeElimination
            |> Result.map Passes.jumpThreading
            |> Result.map Passes.instructionSimplify
            |> Result.map Passes.instructionCombine
            |> Result.map Passes.deadCodeElimination
            |> Result.map Passes.simplifyCFG
            |> Result.map Passes.removeRedundantLoadStores
            |> Result.map (debugModulePasses "End of optimizations:")
            |> Result.map Legalize.legalizeConstants
            |> Result.map Legalize.destroyCriticalEdges
            |> Result.map Legalize.genPhiMoves
            |> Result.map (debugModulePasses "End of IR:")
            |> Result.map Passes.numberAll
            |> Result.map Passes.analyzeLifetimes
            |> Result.map RegisterAllocator.allocateRegisters
            |> Result.map GenAssembly.genAssembly
            |> Result.map GenAssembly.removeRedundantLabels
            |> Result.map Assembly.printAssembly

        match result with
        | Ok _ -> 0
        | Error _ -> -1

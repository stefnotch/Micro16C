module Micro16C.MiddleEnd.IR

open System
open System.Collections.Generic

let (|Ref|) (ref: 'T ref) = ref.Value

type Register =
    | PC
    | R0
    | R1
    | R2
    | R3
    | R4
    | R5
    | R6
    | R7
    | R8
    | R9
    | R10
    | AC

    member this.asString =
        match this with
        | PC -> "PC"
        | R0 -> "R0"
        | R1 -> "R1"
        | R2 -> "R2"
        | R3 -> "R3"
        | R4 -> "R4"
        | R5 -> "R5"
        | R6 -> "R6"
        | R7 -> "R7"
        | R8 -> "R8"
        | R9 -> "R9"
        | R10 -> "R10"
        | AC -> "AC"


[<NoComparison>]
[<ReferenceEquality>]
type Value =
    { Users: Value ref list
      Name: string
      Content: ValueContent
      ParentBlock: Value ref option
      Index: int option
      LifeIntervals: (int * int) list
      Register: Register option }

    override this.ToString() = this.Name

    static member Default =
        { Users = []
          Name = ""
          Content = Undef
          ParentBlock = None
          Index = None
          LifeIntervals = []
          Register = None }

    static member UndefValue = ref Value.Default

and ValueContent =
    | Constant of Constant
    | Register of Register
    | AllocationInstruction of AllocationInstruction
    | BinaryInstruction of BinaryInstruction
    | UnaryInstruction of UnaryInstruction
    | LoadInstruction of LoadInstruction
    | StoreInstruction of StoreInstruction
    | GotoInstruction of GotoInstruction
    | CondBrInstruction of CondBrInstruction
    | PhiInstruction of PhiInstruction
    | BasicBlockValue of BasicBlock
    | Undef
    | CopyInstruction of MoveInstruction

and Constant = { Value: int16 }

and AllocationInstruction = { Aliased: bool option }

and BinaryKind =
    | And
    | Add

and BinaryInstruction =
    { Left: Value ref
      Kind: BinaryKind
      Right: Value ref }

and UnaryKind =
    | Not
    | Shl
    | Shr

and UnaryInstruction = { Kind: UnaryKind; Value: Value ref }

and LoadInstruction = { Source: Value ref }

and StoreInstruction =
    { Destination: Value ref
      Value: Value ref }

and GotoInstruction = { BasicBlock: Value ref }

and CondBrKind =
    | Zero
    | Negative

and CondBrInstruction =
    { Kind: CondBrKind
      Value: Value ref
      TrueBranch: Value ref
      FalseBranch: Value ref }

and PhiInstruction =
    { Incoming: (Value ref * Value ref) list }

and MoveInstruction = { Source: Value ref }

and BasicBlock =
    { Instructions: Value ref list
      ImmediateDominator: Value ref option
      DominanceFrontier: Value ref list option
      ParentModule: Module ref }

and Module =
    internal
        { BasicBlocks: Value ref list }

        static member Default = { BasicBlocks = [] }

let rec private filterOnce predicate list =
    match list with
    | [] -> []
    | head :: list -> if not (predicate head) then list else head :: (filterOnce predicate list)

module Value =

    let name value = value.Name

    let parentBlock value = value.ParentBlock

    let users value = value.Users

    let index value = Option.get value.Index

    let register (value: Value) = value.Register

    let lifeIntervals value = value.LifeIntervals

    let tracksUsers value =
        match value.Content with
        | Constant _
        | Register _
        | Undef -> false
        | _ -> true

    let internal addUser dependent operand =
        if !operand |> tracksUsers then
            operand
            := { !operand with
                     Users = dependent :: (!operand).Users }

    let private removeUser dependant operand =
        operand
        := { !operand with
                 Users = (!operand).Users |> filterOnce ((<>) dependant) }

    let operands value =
        match value.Content with
        | Constant _
        | Register _
        | BasicBlockValue _
        | Undef _
        | AllocationInstruction _ -> []
        | BinaryInstruction { Left = lhs; Right = rhs } -> [ lhs; rhs ]
        | GotoInstruction { BasicBlock = value }
        | UnaryInstruction { Value = value }
        | CopyInstruction { Source = value }
        | LoadInstruction { Source = value } -> [ value ]
        | StoreInstruction { Value = value
                             Destination = destination } -> [ value; destination ]
        | CondBrInstruction { Value = value
                              TrueBranch = trueBranch
                              FalseBranch = falseBranch } -> [ value; trueBranch; falseBranch ]
        | PhiInstruction { Incoming = list } ->
            list
            |> List.fold (fun state (x, y) -> y :: x :: state) []
            |> List.rev

    let setOperand index operand value =
        operand |> addUser value

        match (index, (!value).Content) with
        | (_, Constant _)
        | (_, Register _)
        | (_, AllocationInstruction _) -> failwith "Internal Compiler Error: Invalid Operand Index"
        | (i, UnaryInstruction _)
        | (i, GotoInstruction _)
        | (i, CopyInstruction _)
        | (i, LoadInstruction _) when i >= 1 -> failwith "Internal Compiler Error: Invalid Operand Index"
        | (i, BinaryInstruction _)
        | (i, StoreInstruction _) when i >= 2 -> failwith "Internal Compiler Error: Invalid Operand Index"
        | (i, CondBrInstruction _) when i >= 3 -> failwith "Internal Compiler Error: Invalid Operand Index"
        | (i, PhiInstruction instr) when i >= 2 * List.length instr.Incoming ->
            failwith "Internal Compiler Error: Invalid Operand Index"
        | (0, CondBrInstruction instr) ->
            instr.Value |> removeUser value

            value
            := { !value with
                     Content = CondBrInstruction { instr with Value = operand } }
        | (1, CondBrInstruction instr) ->
            instr.TrueBranch |> removeUser value

            value
            := { !value with
                     Content = CondBrInstruction { instr with TrueBranch = operand } }
        | (2, CondBrInstruction instr) ->
            instr.FalseBranch |> removeUser value

            value
            := { !value with
                     Content = CondBrInstruction { instr with FalseBranch = operand } }
        | (0, UnaryInstruction instr) ->
            instr.Value |> removeUser value

            value
            := { !value with
                     Content = UnaryInstruction { instr with Value = operand } }
        | (0, GotoInstruction instr) ->
            instr.BasicBlock |> removeUser value

            value
            := { !value with
                     Content = GotoInstruction { instr with BasicBlock = operand } }
        | (0, LoadInstruction instr) ->
            instr.Source |> removeUser value

            value
            := { !value with
                     Content = LoadInstruction { instr with Source = operand } }
        | (0, CopyInstruction instr) ->
            instr.Source |> removeUser value

            value
            := { !value with
                     Content = CopyInstruction { instr with Source = operand } }
        | (0, BinaryInstruction instr) ->
            instr.Left |> removeUser value

            value
            := { !value with
                     Content = BinaryInstruction { instr with Left = operand } }
        | (1, BinaryInstruction instr) ->
            instr.Right |> removeUser value

            value
            := { !value with
                     Content = BinaryInstruction { instr with Right = operand } }
        | (0, StoreInstruction instr) ->
            instr.Value |> removeUser value

            value
            := { !value with
                     Content = StoreInstruction { instr with Value = operand } }
        | (1, StoreInstruction instr) ->
            instr.Destination |> removeUser value

            value
            := { !value with
                     Content = StoreInstruction { instr with Destination = operand } }
        | (i, PhiInstruction instr) ->

            value
            := { !value with
                     Content =
                         PhiInstruction
                             { instr with
                                   Incoming =
                                       instr.Incoming
                                       |> List.indexed
                                       |> List.map (fun pair ->
                                           match pair with
                                           | (j, (old, block)) when i / 2 = j && i % 2 = 0 ->
                                               old |> removeUser value
                                               (operand, block)
                                           | (j, (incoming, old)) when i / 2 = j && i % 2 = 1 ->
                                               old |> removeUser value
                                               (incoming, operand)
                                           | (_, x) -> x) } }

        | _ -> failwith "Internal Compiler Error"

    // Careful, in an instruction with multiple operands this will delete all occurrences
    let replaceOperand operand replacement value =
        !value
        |> operands
        |> List.indexed
        |> List.filter (snd >> ((=) operand))
        |> List.map fst
        |> List.iter (fun i -> setOperand i replacement value)

    let useCount = users >> List.length

    let hasSideEffects value =
        match value.Content with
        | GotoInstruction _
        | StoreInstruction _
        | CondBrInstruction _ -> true
        | _ -> false

    let isInstruction value =
        match value.Content with
        | Constant _
        | Register _
        | Undef
        | BasicBlockValue _ -> false
        | _ -> true

    let producesValue value =
        match value.Content with
        | AllocationInstruction _
        | BinaryInstruction _
        | UnaryInstruction _
        | LoadInstruction _
        | CopyInstruction _
        | PhiInstruction _ -> true
        | _ -> false

    let isTerminating value =
        match value.Content with
        | GotoInstruction _
        | CondBrInstruction _ -> true
        | _ -> false

    let asBasicBlock =
        function
        | { Content = BasicBlockValue value } -> value
        | _ -> failwith "Internal Compiler Error: Value is not a BasicBlock"

    let removeFromParent value =
        match (!value).ParentBlock with
        | None -> ()
        | Some (Ref { Content = BasicBlockValue block } as bb) ->
            bb
            := { !bb with
                     Content =
                         BasicBlockValue
                             { block with
                                   Instructions = block.Instructions |> List.filter ((<>) value) } }

            value := { !value with ParentBlock = None }
        | _ -> failwith "Internal Compiler Error"

    let rec destroy value =

        // If we are removing a basic block we need to make sure every instruction does not have an operand that doesn't
        // use operands from other basic blocks nor have users from other blocks. We replace those with undefs
        match (!value) with
        | { Content = BasicBlockValue ({ ParentModule = irModule } as block) } ->
            block.Instructions
            |> List.map (fun x ->
                (x,
                 !x
                 |> operands
                 |> List.indexed
                 |> List.filter (fun (_, o) -> (!o).ParentBlock <> (!x).ParentBlock)
                 |> List.map fst))
            |> List.iter (fun (x, list) ->
                list
                |> List.iter (fun i -> setOperand i Value.UndefValue x))

            block.Instructions
            |> List.iter (replaceWith Value.UndefValue)

            irModule
            := { !irModule with
                     BasicBlocks = (!irModule).BasicBlocks |> List.except [ value ] }

            !value
            |> users
            |> List.iter (fun phi ->
                match !phi with
                | { Content = PhiInstruction ({ Incoming = list } as phiInstr) } ->

                    let trueList, falseList =
                        list |> List.partition (snd >> (<>) value)

                    phi
                    := { !phi with
                             Content = PhiInstruction { phiInstr with Incoming = trueList } }

                    falseList
                    |> List.iter (fun (x, y) ->
                        x |> removeUser phi
                        y |> removeUser phi)
                | _ -> ())
        | _ -> ()

        (!value)
        |> operands
        |> List.indexed
        |> List.map fst
        |> List.iter (fun i -> setOperand i Value.UndefValue value)

        value |> removeFromParent

        assert (useCount !value = 0)

        assert (!value
                |> operands
                |> List.exists ((!) >> tracksUsers)
                |> not)

        value := !Value.UndefValue

    and replaceWith replacement value =

        (!value).Users
        |> List.iter (replaceOperand value replacement)

        assert (useCount !value = 0)

        match (!replacement).ParentBlock with
        | None when isInstruction !replacement ->
            match (!value).ParentBlock with
            | None -> ()
            | Some parentBlockValue ->
                replacement
                := { !replacement with
                         ParentBlock = Some parentBlockValue }

                let index =
                    (!parentBlockValue |> asBasicBlock).Instructions
                    |> List.findIndex ((=) value)

                destroy value

                let parentBlock = !parentBlockValue |> asBasicBlock

                let (first, second) =
                    parentBlock.Instructions |> List.splitAt index

                parentBlockValue
                := { !parentBlockValue with
                         Content =
                             BasicBlockValue
                                 { parentBlock with
                                       Instructions = first @ [ replacement ] @ second } }
        | _ -> destroy value

module BasicBlock =

    let createDefault parent =
        { Instructions = []
          ImmediateDominator = None
          DominanceFrontier = None
          ParentModule = parent }

    let revInstructions basicBlock = basicBlock.Instructions

    let instructions = revInstructions >> List.rev

    let immediateDominator basicBlock = basicBlock.ImmediateDominator

    let dominanceFrontier basicBlock = basicBlock.DominanceFrontier

    let successors =
        Value.asBasicBlock
        >> revInstructions
        >> List.tryHead
        >> Option.filter ((!) >> Value.isTerminating)
        >> Option.map
            ((!)
             >> Value.operands
             >> List.filter (function
                 | Ref { Content = BasicBlockValue _ } -> true
                 | _ -> false))
        >> Option.defaultValue []

    let predecessors =
        Value.users
        >> List.filter ((!) >> Value.isTerminating)
        >> List.choose ((!) >> Value.parentBlock)

    let dominators =
        Seq.unfold (fun blockValue ->
            !blockValue
            |> Value.asBasicBlock
            |> immediateDominator
            |> Option.filter ((=) blockValue)
            |> Option.map (fun x -> (x, x)))

    let dominates other basicBlock =
        other |> dominators |> Seq.contains basicBlock

    let tryTerminator =
        revInstructions
        >> List.tryHead
        >> Option.filter ((!) >> Value.isTerminating)

    let terminator = tryTerminator >> Option.get

    let phis =
        instructions
        >> List.takeWhile (function
            | Ref { Content = PhiInstruction _ } -> true
            | _ -> false)

    let nonPhiInstructions =
        instructions
        >> List.skipWhile (function
            | Ref { Content = PhiInstruction _ } -> true
            | _ -> false)

module Module =

    let basicBlocks irModule = irModule.BasicBlocks |> List.rev

    let revBasicBlocks irModule = irModule.BasicBlocks

    let revInstructions =
        revBasicBlocks
        >> List.map
            ((!)
             >> Value.asBasicBlock
             >> BasicBlock.revInstructions)
        >> List.concat

    let instructions = revInstructions >> List.rev

    let asText irModule =
        let mutable counter = 0

        let mutable seenValues =
            Dictionary<Value, string>(HashIdentity.Reference)

        let seenNames = ref Set.empty

        irModule.BasicBlocks
        |> List.rev
        |> List.fold (fun text blockValue ->

            let getName (value: Value ref) =
                match !value with
                | { Content = Constant { Value = constant } } -> constant |> string
                | { Content = Register register } -> register.asString
                | { Content = Undef } -> "undef"
                | { Name = name } ->
                    match seenValues.TryGetValue !value with
                    | (true, name) -> name
                    | (false, _) ->
                        match name with
                        | "" ->
                            counter <- counter + 1
                            let name = "%" + ((counter - 1) |> string)
                            seenValues.Add(!value, name)
                            name
                        | _ ->
                            let rec uniqueName name =
                                if Set.contains name !seenNames then
                                    match name
                                          |> List.ofSeq
                                          |> List.rev
                                          |> List.takeWhile Char.IsDigit with
                                    | [] -> uniqueName (name + "0")
                                    | digits ->
                                        let newInt =
                                            digits
                                            |> List.rev
                                            |> List.toArray
                                            |> String
                                            |> int
                                            |> ((+) 1)

                                        let name =
                                            name
                                            |> List.ofSeq
                                            |> List.rev
                                            |> List.skip (List.length digits)
                                            |> List.rev
                                            |> List.toArray
                                            |> String

                                        uniqueName (name + (newInt |> string))
                                else
                                    seenValues.Add(!value, name)
                                    seenNames := Set.add name !seenNames
                                    name

                            uniqueName ("%" + name)

            let block = !blockValue |> Value.asBasicBlock

            let pred =
                !blockValue
                |> BasicBlock.predecessors
                |> List.map getName

            let succ =
                !blockValue
                |> BasicBlock.successors
                |> List.map getName

            let text =
                text + sprintf "; succ = %A pred = %A\n" succ pred

            let text =
                text + sprintf "%s:\n" (getName blockValue)

            (block.Instructions
             |> List.rev
             |> List.fold (fun text instruction ->
                 match !instruction with
                 | { Content = AllocationInstruction _ } ->
                     text
                     + sprintf "\t%s = alloca\n" (getName instruction)
                 | { Content = GotoInstruction goto } ->
                     text
                     + sprintf "\tgoto %s\n" (getName goto.BasicBlock)
                 | { Content = BinaryInstruction binary } ->
                     let opName =
                         match binary.Kind with
                         | Add -> "add"
                         | And -> "and"

                     text
                     + sprintf "\t%s = %s %s %s\n" (getName instruction) opName (getName binary.Left)
                           (getName binary.Right)
                 | { Content = UnaryInstruction unary } ->
                     let opName =
                         match unary.Kind with
                         | Not -> "not"
                         | Shl -> "shl"
                         | Shr -> "shr"

                     text
                     + sprintf "\t%s = %s %s\n" (getName instruction) opName (getName unary.Value)
                 | { Content = LoadInstruction load } ->
                     text
                     + sprintf "\t%s = load %s\n" (getName instruction) (getName load.Source)
                 | { Content = CopyInstruction move } ->
                     text
                     + sprintf "\t%s = copy %s\n" (getName instruction) (getName move.Source)
                 | { Content = CondBrInstruction cr } ->

                     let opName =
                         match cr.Kind with
                         | Negative -> "< 0"
                         | Zero -> "== 0"

                     text
                     + sprintf "\tbr %s %s %s %s\n" (getName cr.Value) opName (getName cr.TrueBranch)
                           (getName cr.FalseBranch)
                 | { Content = StoreInstruction store } ->
                     text
                     + sprintf "\tstore %s -> %s\n" (getName store.Value) (getName store.Destination)
                 | { Content = PhiInstruction phi } ->
                     let list =
                         phi.Incoming
                         |> List.map (fun (x, y) -> sprintf "(%s,%s)" (getName x) (getName y))
                         |> List.reduce (fun x y -> x + " " + y)

                     text
                     + sprintf "\t%s = phi %s\n" (getName instruction) list
                 | _ -> failwith "Internal Compiler Error") text)
            + "\n") ""

type Builder =
    { InsertBlock: Value ref option
      InsertIndex: int
      Module: Module ref }

type InsertPoint =
    | Before of Value ref
    | After of Value ref
    | Start
    | End

module Builder =

    let fromModule irModule =
        { InsertBlock = None
          InsertIndex = 0
          Module = irModule }

    let private isBasicBlock =
        function
        | { Content = BasicBlockValue _ } -> true
        | _ -> false

    let private addValue value builder =
        match builder.InsertBlock with
        | None -> (value, builder)
        | Some (Ref { Content = BasicBlockValue block } as blockVal) ->

            let (first, second) =
                block.Instructions
                |> List.splitAt builder.InsertIndex

            blockVal
            := { !blockVal with
                     Content =
                         BasicBlockValue
                             { block with
                                   Instructions = first @ [ value ] @ second } }

            value
            := { !value with
                     ParentBlock = Some blockVal }

            (value, builder)
        | _ -> failwith "Internal Compiler Error"

    let insertValue value builder =
        value |> Value.removeFromParent
        builder |> addValue value

    let createBasicBlockAt (insertPoint: InsertPoint) name builder =
        let basicBlock =
            ref
                { Value.Default with
                      Name = name
                      Content = BasicBlockValue(BasicBlock.createDefault builder.Module) }

        match insertPoint with
        | End ->
            builder.Module
            := { !builder.Module with
                     BasicBlocks = basicBlock :: (!builder.Module).BasicBlocks }

            basicBlock
        | Start ->
            builder.Module
            := { !builder.Module with
                     BasicBlocks = (!builder.Module).BasicBlocks @ [ basicBlock ] }

            basicBlock
        | After ref ->
            match (!builder.Module).BasicBlocks
                  |> List.tryFindIndex ((=) ref) with
            | None -> failwith "Internal Compiler Error: Failed to find Basic Block in block list"
            | Some i ->
                let (first, second) =
                    (!builder.Module).BasicBlocks |> List.splitAt i

                builder.Module
                := { !builder.Module with
                         BasicBlocks = first @ [ basicBlock ] @ second }

                basicBlock
        | Before ref ->
            match (!builder.Module).BasicBlocks
                  |> List.tryFindIndex ((=) ref) with
            | None -> failwith "Internal Compiler Error: Failed to find Basic Block in block list"
            | Some i ->
                let (first, second) =
                    (!builder.Module).BasicBlocks
                    |> List.splitAt (i + 1)

                builder.Module
                := { BasicBlocks = first @ [ basicBlock ] @ second }

                basicBlock

    let createBasicBlock = createBasicBlockAt End

    let insertBlock builder = builder.InsertBlock

    let afterInstr builder =
        match builder.InsertBlock with
        | None -> failwith "Internal Compiler Error: No current insert block"
        | Some blockValue ->
            let block = !blockValue |> Value.asBasicBlock

            if builder.InsertIndex = List.length block.Instructions then
                Start
            else
                block.Instructions
                |> List.item builder.InsertIndex
                |> After

    let beforeInstr builder =
        match builder.InsertBlock with
        | None -> failwith "Internal Compiler Error: No current insert block"
        | Some blockValue ->
            let block = !blockValue |> Value.asBasicBlock

            if builder.InsertIndex = 0 then
                End
            else
                (block.Instructions
                 |> List.item (builder.InsertIndex - 1))
                |> Before

    let setInsertBlock basicBlock builder =
        assert (basicBlock
                |> Option.map ((!) >> isBasicBlock)
                |> Option.defaultValue true)

        { builder with
              InsertBlock = basicBlock
              InsertIndex = 0 }

    let setInsertPoint (insertPoint: InsertPoint) builder =
        match builder.InsertBlock with
        | None -> builder
        | Some blockValue ->
            let block = !blockValue |> Value.asBasicBlock

            match insertPoint with
            | End -> { builder with InsertIndex = 0 }
            | Start ->
                { builder with
                      InsertIndex = List.length block.Instructions }
            | After ref ->
                match block.Instructions |> List.tryFindIndex ((=) ref) with
                | None -> failwith "Internal Compiler Error: Failed to find After instruction in insert block"
                | Some i -> { builder with InsertIndex = i }
            | Before ref ->
                match block.Instructions |> List.tryFindIndex ((=) ref) with
                | None -> failwith "Internal Compiler Error: Failed to find Before instruction in insert block"
                | Some i -> { builder with InsertIndex = i + 1 }

    let createConstant value =
        ref
            { Value.Default with
                  Name = value |> string
                  Content = Constant { Value = value } }

    let createRegister register =
        ref
            { Value.Default with
                  Content = Register register }

    let createNamedAlloca name builder =
        let value =
            ref
                { Value.Default with
                      Name = name
                      Content = AllocationInstruction { Aliased = None } }

        builder |> addValue value

    let createAlloca = createNamedAlloca ""

    let createNamedBinary name left kind right builder =
        let value =
            ref
                { Value.Default with
                      Name = name
                      Content =
                          BinaryInstruction
                              { Left = left
                                Kind = kind
                                Right = right } }

        left |> Value.addUser value
        right |> Value.addUser value

        builder |> addValue value

    let createBinary = createNamedBinary ""

    let createNamedUnary name kind value builder =
        let unary =
            ref
                { Value.Default with
                      Name = name
                      Content = UnaryInstruction { Kind = kind; Value = value } }

        value |> Value.addUser unary

        builder |> addValue unary

    let createUnary = createNamedUnary ""

    let createNamedLoad name value builder =

        let load =
            ref
                { Value.Default with
                      Name = name
                      Content = LoadInstruction { Source = value } }

        value |> Value.addUser load

        builder |> addValue load

    let createLoad = createNamedLoad ""

    let createNamedCopy name value builder =

        let copy =
            ref
                { Value.Default with
                      Name = name
                      Content = CopyInstruction { Source = value } }

        value |> Value.addUser copy

        builder |> addValue copy

    let createCopy = createNamedCopy ""

    let createStore destination value builder =

        let store =
            ref
                { Value.Default with
                      Content =
                          StoreInstruction
                              { Destination = destination
                                Value = value } }

        value |> Value.addUser store
        destination |> Value.addUser store

        builder |> addValue store

    let createGoto destination builder =

        assert (!destination |> isBasicBlock)

        let value =
            ref
                { Value.Default with
                      Content = GotoInstruction { BasicBlock = destination } }

        destination |> Value.addUser value

        builder |> addValue value

    let createCondBr kind condition trueBranch falseBranch builder =

        assert (!trueBranch |> isBasicBlock)
        assert (!falseBranch |> isBasicBlock)

        let value =
            ref
                { Value.Default with
                      Content =
                          CondBrInstruction
                              { Kind = kind
                                Value = condition
                                TrueBranch = trueBranch
                                FalseBranch = falseBranch } }

        condition |> Value.addUser value
        trueBranch |> Value.addUser value
        falseBranch |> Value.addUser value

        builder |> addValue value

    let createNamedPhi name incoming builder =

        let value =
            ref
                { Value.Default with
                      Name = name
                      Content = PhiInstruction { Incoming = incoming } }

        incoming
        |> List.map (snd >> (!) >> isBasicBlock)
        |> List.iter (fun x -> assert x)

        incoming
        |> List.iter (fun (x, y) ->
            x |> Value.addUser value
            y |> Value.addUser value)

        builder |> addValue value

    let createPhi = createNamedPhi ""

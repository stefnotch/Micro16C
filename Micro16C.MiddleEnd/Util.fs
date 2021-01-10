module Micro16C.MiddleEnd.Util

open System.Collections.Generic
open System.Collections.Immutable

module ImmutableMap =

    let ofList (list: ('Key * 'Value) list): ImmutableDictionary<'Key, 'Value> =
        ImmutableDictionary.CreateRange<'Key, 'Value>(HashIdentity.Reference, list |> List.map (KeyValuePair))

    let ofSeq (seq: seq<('Key * 'Value)>): ImmutableDictionary<'Key, 'Value> =
        ImmutableDictionary.CreateRange<'Key, 'Value>(HashIdentity.Reference, seq |> Seq.map (KeyValuePair))

    let tryFind value (map: ImmutableDictionary<'Key, 'Value>) =
        match map.TryGetValue value with
        | (false, _) -> None
        | (true, value) -> Some value

    let inline add key value (map: ImmutableDictionary<'Key, 'Value>) = map.SetItem(key, value)

    let inline map (f: ('Key * 'Value -> 'Key2 * 'Value2)) (map: ImmutableDictionary<'Key, 'Value>) =
        map
        |> Seq.map (fun kv -> f (kv.Deconstruct()))
        |> ofSeq

    let iter f =
        Seq.iter (fun (kv: KeyValuePair<'U, 'V>) -> kv.Deconstruct() |> f)

module ImmutableSet =

    let ofList (list: 'Key list): ImmutableHashSet<'Key> =
        ImmutableHashSet.CreateRange<'Key>(HashIdentity.Reference, list)

    let ofSeq (seq: seq<'Key>): ImmutableHashSet<'Key> =
        ImmutableHashSet.CreateRange<'Key>(HashIdentity.Reference, seq)

    let empty<'Key when 'Key: not struct> =
        ImmutableHashSet<'Key>
            .Empty.WithComparer(HashIdentity.Reference)

    let add value (set: ImmutableHashSet<'Key>) = set.Add(value)

    let contains value (set: ImmutableHashSet<'Key>) = set.Contains(value)

    let count (set: ImmutableHashSet<'Key>) = set.Count

    let difference (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.Except set2

    let exists predicate (set: ImmutableHashSet<'Key>) = set |> Seq.exists predicate

    let filter predicate (set: ImmutableHashSet<'Key>) = set |> Seq.filter predicate |> ofSeq

    let fold folder state (set: ImmutableHashSet<'Key>) = set |> Seq.fold folder state |> ofSeq

    let foldBack folder (set: ImmutableHashSet<'Key>) state = Seq.foldBack folder set state

    let forall predicate (set: ImmutableHashSet<'Key>) = set |> Seq.forall predicate

    let intersect (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.Intersect set2

    let intersectMany (sets: seq<ImmutableHashSet<'Key>>) = sets |> Seq.reduce intersect

    let isEmpty (set: ImmutableHashSet<'Key>) = set.IsEmpty

    let isProperSubset (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.IsProperSubsetOf set2

    let isProperSuperset (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.IsProperSupersetOf set2

    let isSubset (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.IsSubsetOf set2

    let isSuperset (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.IsSupersetOf set2

    let iter action (set: ImmutableHashSet<'Key>) = set |> Seq.iter action

    let map mapping (set: ImmutableHashSet<'Key>) = set |> Seq.map mapping |> ofSeq

    let remove value (set: ImmutableHashSet<'Key>) = set.Remove value

    let toList (set: ImmutableHashSet<'Key>) = set |> List.ofSeq

    let toSeq (set: ImmutableHashSet<'Key>) = set :> seq<'Key>

    let union (set1: ImmutableHashSet<'Key>) (set2: ImmutableHashSet<'Key>) = set1.Union set2

    let unionMany (sets: seq<ImmutableHashSet<'Key>>) = sets |> Seq.reduce union

let associateValue v x = (x, v)

let associateWith f x = (x, f x)
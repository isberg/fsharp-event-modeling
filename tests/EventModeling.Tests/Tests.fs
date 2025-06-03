module EventModeling.Tests

open Expecto
open CommandPattern
open ViewPattern

// Domain types and decider as in README

type Event =
    | Incremented
    | Decremented
    interface TypeShape.UnionContract.IUnionContract

type Command =
    | Increment
    | Decrement

type State =
    | Zero
    | Succ of State

let counterDecider : Decider<State, Command, Event> = {
    initial = Zero
    decide = fun cmd state ->
        match cmd, state with
        | Increment, _ -> [ Incremented ]
        | Decrement, Zero -> []
        | Decrement, Succ _ -> [ Decremented ]
    evolve = fun state event ->
        match event, state with
        | Incremented, _ -> Succ state
        | Decremented, Zero -> Zero
        | Decremented, Succ s -> s
}

let countProjection : Projection<int, Event> =
    { initial = 0
      project = fun count -> function
        | Incremented -> count + 1
        | Decremented -> count - 1 }

[<Tests>]
let executeTests =
    testList "execute" [
        testCase "returns no events when decrementing zero" <| fun _ ->
            let events = execute counterDecider [] Decrement
            Expect.equal events [] "No events should be produced"

        testCase "returns event when decrementing successor" <| fun _ ->
            let history = [ Incremented ]
            let events = execute counterDecider history Decrement
            Expect.equal events [ Decremented ] "Decrement event expected"
    ]

[<Tests>]
let hydrateTests =
    testList "hydrate" [
        testCase "recreates state from history" <| fun _ ->
            let history = [ Incremented; Incremented; Decremented ]
            let state = CommandPattern.hydrate counterDecider history
            Expect.equal state (Succ Zero) "State should equal Succ Zero"

        testCase "projects view from history" <| fun _ ->
            let history = [ Incremented; Decremented; Incremented ]
            let count = ViewPattern.hydrate countProjection history
            Expect.equal count 1 "Count should be 1"
    ]

[<EntryPoint>]
let main args =
    runTestsInAssembly defaultConfig args

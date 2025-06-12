module EventModeling.Tests

open Expecto
open CommandPattern
open ViewPattern
open AutomationPattern
open TranslationPattern
open Service

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

[<Tests>]
let automationTests =
    let triggerIfZero count = if count = 0 then Some Increment else None
    let automation =
        { projection = countProjection
          trigger = triggerIfZero
          decider = counterDecider }
    testList "automation" [
        testCase "runIncremental executes command when trigger fires" <| fun _ ->
            let events, view, state =
                AutomationPattern.runIncremental automation 0 Zero []
            Expect.equal events [ Incremented ] "Should emit Incremented"
            Expect.equal view 0 "View unchanged"
            Expect.equal state (Succ Zero) "State updated"

        testCase "run ignores when trigger does not fire" <| fun _ ->
            let events, view, state =
                AutomationPattern.runIncremental automation 1 (Succ Zero) []
            Expect.equal events [] "No events emitted"
            Expect.equal view 1 "View unchanged"
            Expect.equal state (Succ Zero) "State unchanged"

        testCase "run hydrates and executes" <| fun _ ->
            let history = []
            let events = AutomationPattern.run automation history
            Expect.equal events [ Incremented ] "Should produce event from run"
    ]

[<Tests>]
let translationTests =
    let lastEventProjection =
        { ViewPattern.initial = None
          project = fun _ e -> Some e }
    let translator =
        { projection = lastEventProjection
          translate = function
            | Some Incremented -> Some Increment
            | _ -> None }
    testList "translation" [
        testCase "runIncremental produces command" <| fun _ ->
            let cmds, view =
                TranslationPattern.runIncremental translator None [ Incremented ]
            Expect.equal cmds [ Increment ] "Command emitted"
            Expect.equal view (Some Incremented) "View updated"

        testCase "run processes history" <| fun _ ->
            let history = [ Incremented; Decremented ]
            let cmds = TranslationPattern.run translator history
            Expect.equal cmds [] "No command for last event"

        testCase "run produces command for last event" <| fun _ ->
            let history = [ Incremented ]
            let cmds = TranslationPattern.run translator history
            Expect.equal cmds [ Increment ] "Command expected"
    ]

[<Tests>]
let crossStreamTests =
    let service = Service.createService counterDecider "Counter" None None Service.defaultStreamId
    testCase "loadCategory aggregates events across streams" <| fun _ ->
        Async.RunSynchronously <| service.Execute "a" Increment
        Async.RunSynchronously <| service.Execute "b" Increment
        let events = service.LoadCategory()
        let ids = events |> List.map (fun e -> e.StreamId)
        Expect.equal (ids |> List.sort) [ "a"; "b" ] "Both stream ids should be present"

[<EntryPoint>]
let main args =
    runTestsInAssembly defaultConfig args

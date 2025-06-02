open System
open Suave

// Domain model from README

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

let counterDecider : CommandPattern.Decider<State, Command, Event> = {
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
        | Decremented, Succ state' -> state'
}

// View projections demonstrating the ViewPattern

let countProjection : ViewPattern.Projection<int, Event> =
    { initial = 0
      project = fun count -> function
        | Incremented -> count + 1
        | Decremented -> count - 1 }

let historyProjection : ViewPattern.Projection<Event list, Event> =
    { initial = []
      project = fun events e -> events @ [ e ] }

[<EntryPoint>]
let main _ =
    let service = Service.createService counterDecider "Counter" None None
    let _ : IDisposable =
        service.Subscribe (fun name events -> printfn "%A" (name, events))
    let app =
        GenericResource.configure
            "Counter"
            "/counters/%s"
            "/counters/%s/%s"
            service
            [ "count", countProjection
              "history", historyProjection ]
    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0

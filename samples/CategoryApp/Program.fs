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

// Per-counter projection
let countProjection : ViewPattern.ProjectionSpec<int, Event> =
    { initial = 0
      project = fun count -> function
        | Incremented -> count + 1
        | Decremented -> count - 1 }

// Category-level projections
let totalProjection : ViewPattern.ProjectionSpec<int, ViewPattern.StreamEvent<Event>> =
    { initial = 0
      project = fun total se ->
        match se.Event with
        | Incremented -> total + 1
        | Decremented -> total - 1 }

let allCountsProjection : ViewPattern.ProjectionSpec<Map<string,int>, ViewPattern.StreamEvent<Event>> =
    { initial = Map.empty
      project = fun counts se ->
        let current = counts |> Map.tryFind se.StreamId |> Option.defaultValue 0
        let updated =
            match se.Event with
            | Incremented -> current + 1
            | Decremented -> current - 1
        counts |> Map.add se.StreamId updated }

[<EntryPoint>]
let main _ =
    let service = Service.createService counterDecider "Counter" None None Service.defaultStreamId
    let _ : IDisposable = service.Subscribe (fun name events -> printfn "%A" (name, events))
    let app =
        GenericResource.configureWithCategory
            "Counter"
            "/counter/%s"
            "/counters/%s/%s"
            "/counters/%s"
            service
            [ GenericResource.boxedStream "count" countProjection
              GenericResource.boxedCategory "total" totalProjection
              GenericResource.boxedCategory "all" allCountsProjection ]
    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0

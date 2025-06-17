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

// Projection and automation trigger

let countProjection : ViewPattern.ProjectionSpec<int, Event> =
    { initial = 0
      project = fun count -> function
        | Incremented -> count + 1
        | Decremented -> count - 1 }

let triggerIfZero count =
    if count = 0 then Some Increment else None

let automation : AutomationPattern.Automation<int, State, Event, Command> =
    { projection = countProjection
      trigger = triggerIfZero
      decider = counterDecider }

[<EntryPoint>]
let main _ =
    let service =
        Service.ServiceConfig.create "Counter"
        |> Service.ServiceConfig.withAutomation automation
        |> Service.createServiceWith counterDecider
    let _ : IDisposable =
        service.Subscribe (fun name events -> printfn "%A" (name, events))
    let app =
        GenericResource.ResourceConfig.create "Counter" "/counters/%s" "/counters/%s/%s" service
            [ GenericResource.box "count" (ViewPattern.StreamProjection countProjection) ]
        |> GenericResource.configure
    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0

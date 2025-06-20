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

let countProjection : ViewPattern.ProjectionSpec<int, Event> =
    { initial = 0
      project = fun count -> function
        | Incremented -> count + 1
        | Decremented -> count - 1 }

let lastEventProjection : ViewPattern.ProjectionSpec<Event option, Event> =
    { initial = None
      project = fun _ e -> Some e }

let mirrorTranslator : TranslationPattern.Translator<Event, Event option, Command> =
    { projection = lastEventProjection
      translate = function
        | Some Incremented -> Some Increment
        | _ -> None }

let mirrorService =
    Service.ServiceConfig.create "Mirror"
    |> Service.createServiceWith counterDecider

let counterService =
    Service.ServiceConfig.create "Counter"
    |> Service.ServiceConfig.withTranslation (mirrorTranslator, mirrorService)
    |> Service.createServiceWith counterDecider

[<EntryPoint>]
let main _ =
    let _ : IDisposable =
        counterService.Subscribe (fun name events -> printfn "%A" (name, events))
    let _ : IDisposable =
        mirrorService.Subscribe (fun name events -> printfn "%A" (name, events))

    let counterApp =
        GenericResource.ResourceConfig.create "Counter" counterService
        |> GenericResource.ResourceConfig.withProjections [ GenericResource.box "count" (ViewPattern.StreamProjection countProjection) ]
        |> GenericResource.configure

    let mirrorApp =
        GenericResource.ResourceConfig.create "Mirror" mirrorService
        |> GenericResource.ResourceConfig.withProjections [ GenericResource.box "count" (ViewPattern.StreamProjection countProjection) ]
        |> GenericResource.configure

    let app = choose [ counterApp; mirrorApp ]

    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0

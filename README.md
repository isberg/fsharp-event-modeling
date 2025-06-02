# fsharp-event-modeling

The intention of this library is to make proptotyping in F# using Event Storming, Event Modeling and Event Sourcing easier. Please check https://eventmodeling.org/posts/event-modeling-cheatsheet/ before using the library. :-)

## Recommended further reading
- https://www.eventstorming.com/
- https://eventmodeling.org/
- https://gregfyoung.wordpress.com/tag/event-sourcing/

## Example Usage

Model your domain aggregate with types for state, command and event as well as initial state, command handler and folder, then wrap these up in the `CommandPattern.Decider` type.

```fsharp
type Event =
    | Incremented
    | Decremented
    interface TypeShape.UnionContract.IUnionContract

type Command =
    | Increment
    | Decrement

type State
    = Zero
    | Succ of State

let counterDecider : CommandPattern.Decider<State, Command, Event> = {
    initial = Zero
    decide = fun cmd state ->
        match cmd, state with
        | Increment, _ -> [ Incremented ]
        | Decrement, Zero  -> []
        | Decrement, Succ _  -> [ Decremented ]
    evolve = fun state event ->
        match event, state with
        | Incremented, _ -> Succ state
        | Decremented, Zero -> Zero
        | Decremented, Succ state' -> state'
}
```

### The Decider type

```fsharp
type Decider<'State,'Command,'Event> = {
    initial : 'State
    decide  : 'Command -> 'State -> 'Event list
    evolve  : 'State -> 'Event -> 'State
}
```

### Put a Suave Web API in-front of you aggregate

```fsharp
[<EntryPoint>]
let main _ =
    let service = Service.createService counterDecider "Counter" None None
    let _ : System.IDisposable =
        service.Subscribe (fun name events -> printfn "%A" (name, events))
    let app =
        GenericResource.configure
            "Counter"
            "/counters/%s"
            "/counters/%s/%s"
            service
            []
    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0
```

### Test run your API

Post the Increment command to counter 1: `curl localhost:8080/counters/1 -d '"Increment"'`

Get the current state of counter 1: `curl localhost:8080/counters/1`

### Running the CounterApp sample

Run `dotnet run --project samples/CounterApp/CounterApp.fsproj` to start the web API, then use the curl commands above to interact with it.

### Translating events to commands

Use `TranslationPattern.Translator` to convert the source event history into commands for another service. A translator consists of a projection and a translation function:

```fsharp
let lastEventProjection =
    { ViewPattern.initial = None
      project = fun _ e -> Some e }

let mirrorTranslator : TranslationPattern.Translator<Event, Event option, Command> =
    { projection = lastEventProjection
      translate = function
        | Some Incremented -> Some Increment
        | _ -> None }

let mirrorService = Service.createService counterDecider "Mirror" None None
let counterService =
    Service.createService counterDecider "Counter" None (Some (mirrorTranslator, mirrorService))
```

When `counterService` commits new events, the translator is invoked for each of them. Any produced commands are executed on the `mirrorService` using the same stream identifier.


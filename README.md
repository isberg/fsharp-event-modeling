# fsharp-event-modeling

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
    let service = Service.createService counterDecider "Counter"
    let _ : System.IDisposable = service.Subscribe (fun name events -> printfn "%A" (name, events))
    let app = GenericResource.configure "/counters/%s" service
    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0
```

### Test run your API

Post the Increment command to counter 1: `curl localhost:8080/counters/1 -d '"Increment"'`

Get the current state of counter 1: `curl localhost:8080/counters/1`

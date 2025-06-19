# fsharp-event-modeling

The intention of this library is to make prototyping in F# using Event Storming, Event Modeling and Event Sourcing easier. Please check https://eventmodeling.org/posts/event-modeling-cheatsheet/ before using the library. :-)

## Recommended further reading
- https://www.eventstorming.com/
- https://eventmodeling.org/
- https://gregfyoung.wordpress.com/tag/event-sourcing/

## Getting Started

```bash
dotnet restore
dotnet build
dotnet run --project samples/CounterApp/CounterApp.fsproj
```

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
    let service =
        Service.ServiceConfig.create "Counter"
        |> Service.createServiceWith counterDecider
    let _ : System.IDisposable =
        service.Subscribe (fun name events -> printfn "%A" (name, events))
    let app =
        GenericResource.ResourceConfig.create "Counter" service
        |> GenericResource.configure
    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0
```

### Test run your API

Post the Increment command to counter 1: `curl localhost:8080/counter/1 -d '"Increment"'`

Get the current state of counter 1: `curl localhost:8080/counter/1`

### Running the CounterApp sample

Run `dotnet run --project samples/CounterApp/CounterApp.fsproj` to start the web API, then use the curl commands above to interact with it.

### Translating events to commands

The translation pattern helps wire event streams together by mapping events from one stream or category into commands for another. Use `TranslationPattern.Translator` to convert the source event history into commands for a downstream service. A translator consists of a projection and a translation function:

```fsharp
let lastEventProjection =
    { ViewPattern.initial = None
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
```

When `counterService` commits new events, the translator is invoked for each of them. Any produced commands are executed on the `mirrorService` using the stream identifier returned from the mapping function (here `Service.defaultStreamId`). This example mirrors events within the same category, but by supplying a custom mapping you can translate between different categories or even use completely different stream identifiers.

### Running the AutomationApp sample

Run `dotnet run --project samples/AutomationApp/AutomationApp.fsproj` to start a service that automatically issues an `Increment` command whenever the counter reaches zero.

### Running the TranslationApp sample

Run `dotnet run --project samples/TranslationApp/TranslationApp.fsproj` to start two services where increment events from the `Counter` category are translated into `Increment` commands for the `Mirror` category.


### Running the InventoryApp sample

Run `dotnet run --project samples/InventoryApp/InventoryApp.fsproj` to start a minimal inventory service that demonstrates all four patterns with an automatic reorder workflow.

### Running the CategoryApp sample

Run `dotnet run --project samples/CategoryApp/CategoryApp.fsproj` to start a service exposing category-level views across all counters.

### Substituting a persistent Equinox store

`Service.createService` and `Service.createServiceWith` set up an in-memory
`Equinox.MemoryStore` so the samples run without infrastructure. The memory
store and category are instantiated around
[Service.fs lines 92–128](Service.fs#L92-L128).

To persist events simply reference another Equinox store implementation in your
project file, for example:

```xml
<PackageReference Include="Equinox.EventStoreDb" Version="4.0.0" />
<PackageReference Include="EventStore.Client" Version="22.0.0" />
<PackageReference Include="Equinox.CosmosStore" Version="4.0.0" />
<PackageReference Include="Microsoft.Azure.Cosmos" Version="3.36.0" />
```

Then select the backing store when building the service:

```fsharp
let service =
    Service.ServiceConfig.create categoryName
    |> Service.ServiceConfig.withEventStoreDB "<esdb-connection-string>"
    |> Service.createServiceWith decider

let cosmosService =
    Service.ServiceConfig.create categoryName
    |> Service.ServiceConfig.withCosmosDB "<cosmos-connection-string>"
    |> Service.createServiceWith decider
```

module Service

type Service<'State,'Command,'Event>
    internal
        ( resolve: string -> Equinox.Decider<'Event, 'State>
        , decide : 'Command -> 'State -> 'Event array
        , subscribe : (FsCodec.StreamName -> 'Event list -> unit) -> System.IDisposable
        , load : FsCodec.StreamName -> 'Event list
        , loadCategory : unit -> ViewPattern.StreamEvent<'Event> list
        ) =

    member _.Execute : string -> 'Command -> Async<unit> = fun instanceId command ->
        (resolve instanceId).Transact <| decide command
        
    member _.Read instanceId : Async<'State> =
        (resolve instanceId).Query id
    
    member _.Subscribe : (FsCodec.StreamName -> 'Event list -> unit) -> System.IDisposable = 
        subscribe

    member _.Load : FsCodec.StreamName -> 'Event list =
        load

    member _.LoadCategory () : ViewPattern.StreamEvent<'Event> list =
        loadCategory ()

open Serilog
open TranslationPattern
open ViewPattern

/// Default mapping from source stream name to target instance identifier.
let defaultStreamId (streamName: FsCodec.StreamName) =
    let struct (_, streamId) = FsCodec.StreamName.split streamName
    streamId.ToString()

/// Functions required to integrate a backing store.
type Store<'Event,'State> =
    { category : Equinox.Category<'Event,'State,unit>
      subscribe : (FsCodec.StreamName -> 'Event list -> unit) -> System.IDisposable
      load : FsCodec.StreamName -> 'Event list
      loadCategory : unit -> ViewPattern.StreamEvent<'Event> list }

/// Configuration for selecting a backing store
type StoreChoice<'Event,'State> =
    | Memory
    | Custom of Store<'Event,'State>
    | EventStoreDb of string
    | CosmosDb of string

type ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView> =
    { categoryName : string
      automation : AutomationPattern.Automation<'View,'State,'Event,'Command> option
      translation : (Translator<'Event,'TView,'TCommand> * Service<'TState,'TCommand,'TEvent>) option
      mapStreamId : FsCodec.StreamName -> string
      store : StoreChoice<'Event,'State> option }

module ServiceConfig =
    /// Start a configuration with defaults for optional fields
    let create<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>
        categoryName : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView> =
        { categoryName = categoryName
          automation = None
          translation = None
          mapStreamId = defaultStreamId
          store = None }

    /// Set the automation field
    let withAutomation automation (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with automation = Some automation }

    /// Set the translation field
    let withTranslation translation (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with translation = Some translation }

    /// Override the stream id mapping
    let withMapStreamId mapStreamId (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with mapStreamId = mapStreamId }

    /// Use an explicit store instead of the default memory store
    let withStore store (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with store = Some (Custom store) }

    /// Use EventStoreDB as the backing store
    let withEventStoreDB connectionString (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with store = Some (EventStoreDb connectionString) }

    /// Use CosmosDB as the backing store
    let withCosmosDB connectionString (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with store = Some (CosmosDb connectionString) }

    
let createService<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView
    when 'Event :> TypeShape.UnionContract.IUnionContract
    and 'TEvent :> TypeShape.UnionContract.IUnionContract>
    : CommandPattern.Decider<'State,'Command,'Event>
      -> string
      -> AutomationPattern.Automation<'View,'State,'Event,'Command> option
      -> (Translator<'Event,'TView,'TCommand> * Service<'TState,'TCommand,'TEvent>) option
      -> (FsCodec.StreamName -> string)
      -> StoreChoice<'Event,'State> option
      -> Service<'State,'Command,'Event>
    = fun decider categoryName automation translation mapStreamId storeOpt ->
        let log = LoggerConfiguration().WriteTo.Console().CreateLogger()
        let codec : FsCodec.IEventCodec<'Event,obj,unit> = FsCodec.Box.Codec.Create()

        // Default in-memory implementation used by the samples
        let memoryStore () =
            // In-memory store used by the samples. Replace these two values with an
            // Equinox store of your choice (e.g. CosmosStoreCategory or
            // EventStoreCategory) when persisting events.
            let store : Equinox.MemoryStore.VolatileStore<obj> = Equinox.MemoryStore.VolatileStore()
            let categoryEvents = System.Collections.Generic.List<ViewPattern.StreamEvent<'Event>>()
            let logAndRecord sn (events: FsCodec.ITimelineEvent<_>[]) =
                log.Information("Committed to {streamName}, events: {@events}", sn, seq { for x in events -> x.EventType })
                let decoded =
                    events
                    |> Array.map codec.Decode
                    |> Array.choose (function | ValueSome v -> Some v | ValueNone -> None)
                let struct (_, streamId) = FsCodec.StreamName.split sn
                lock categoryEvents (fun () ->
                    decoded
                    |> Array.iter (fun e -> categoryEvents.Add({ StreamId = streamId.ToString(); Event = e }))
                )
            let _ = store.Committed.Subscribe(fun struct (sn, xs) -> logAndRecord sn xs)
            let cat : Equinox.Category<'Event,'State,unit> =
                Equinox.MemoryStore.MemoryStoreCategory(store, categoryName, codec, Array.fold decider.evolve, decider.initial)
            let subscribe callback =
                store.Committed.Subscribe(
                    fun struct (streamName, events) ->
                        callback streamName (events |> Array.map codec.Decode |> Array.choose (function | ValueSome v -> Some v | ValueNone -> None) |> Array.toList)
                )
            let loadSafe streamName =
                match store.Load (streamName.ToString()) with
                | null -> Array.empty<FsCodec.ITimelineEvent<obj>>
                | events -> events
            let load streamName =
                loadSafe streamName
                |> Array.map codec.Decode
                |> Array.choose (function | ValueSome v -> Some v | ValueNone -> None)
                |> Array.toList
            let loadCategory () =
                lock categoryEvents (fun () -> categoryEvents |> Seq.toList)
            { category = cat; subscribe = subscribe; load = load; loadCategory = loadCategory }

        let eventStoreDbStore connectionString =
            let settings = EventStore.Client.EventStoreClientSettings.Create connectionString
            let client = new EventStore.Client.EventStoreClient(settings)
            let conn = new Equinox.EventStoreDb.EventStoreConnection(client)
            let context = new Equinox.EventStoreDb.EventStoreContext(conn, new Equinox.EventStoreDb.BatchOptions(500))
            let cat =
                Equinox.EventStoreDb.EventStoreCategory(
                    context,
                    categoryName,
                    codec,
                    Array.fold decider.evolve,
                    decider.initial,
                    Equinox.EventStoreDb.AccessStrategy.Unoptimized,
                    Equinox.CachingStrategy.NoCaching)

            let decode (e: EventStore.Client.ResolvedEvent) =
                FsCodec.EventStoreDb.Client.resolvedEvent e
                |> codec.Decode
                |> ValueOption.toOption

            let subscribe callback =
                let eventAppeared _ ev _ =
                    task {
                        match FsCodec.StreamName.tryParse ev.Event.EventStreamId with
                        | ValueSome sn when FsCodec.StreamName.category sn = categoryName ->
                            match decode ev with
                            | Some e -> callback sn [ e ]
                            | None -> ()
                        | _ -> ()
                    }
                let filter =
                    EventStore.Client.SubscriptionFilterOptions(
                        EventStore.Client.StreamFilter.Prefix categoryName)
                let sub =
                    client.SubscribeToAllAsync(
                        EventStore.Client.Position.Start,
                        eventAppeared,
                        false,
                        filter)
                { new System.IDisposable with member _.Dispose() = sub.Result.Dispose() }

            let load streamName =
                client.ReadStreamAsync(EventStore.Client.Direction.Forwards, streamName.ToString(), EventStore.Client.StreamPosition.Start)
                |> Seq.map decode
                |> Seq.choose id
                |> Seq.toList

            let loadCategory () =
                client.ReadAllAsync(EventStore.Client.Direction.Forwards, EventStore.Client.Position.Start)
                |> Seq.choose (fun ev ->
                    match FsCodec.StreamName.tryParse ev.Event.EventStreamId with
                    | ValueSome sn when FsCodec.StreamName.category sn = categoryName ->
                        decode ev
                        |> Option.map (fun e ->
                            let struct (_, sid) = FsCodec.StreamName.split sn
                            { StreamId = sid.ToString(); Event = e })
                    | _ -> None)
                |> Seq.toList

            { category = cat; subscribe = subscribe; load = load; loadCategory = loadCategory }

        let cosmosDbStore connectionString =
            let connector = Equinox.CosmosStore.Discovery.FromConnectionString connectionString
            let cosmos = Equinox.CosmosStore.CosmosStoreClient connector
            let cat =
                Equinox.CosmosStore.CosmosStoreCategory(
                    cosmos,
                    categoryName,
                    codec,
                    Array.fold decider.evolve,
                    decider.initial)

            let decode (e: Equinox.CosmosStore.EventBody) =
                codec.Decode(FsCodec.Core.TimelineEvent.Create(e.Index, e.EventType, e.Data, e.Meta, e.Timestamp))
                |> ValueOption.toOption

            let subscribe callback =
                let sub =
                    cosmos.Submit(fun ct ->
                        Equinox.CosmosStore.CosmosStoreClient.crawl cosmos.Container categoryName (fun sn events ->
                            events |> Array.choose decode |> Array.toList |> callback sn) ct)
                { new System.IDisposable with member _.Dispose() = sub.Dispose() }

            let load streamName =
                cosmos.QueryStream streamName |> Seq.map decode |> Seq.choose id |> Seq.toList

            let loadCategory () =
                cosmos.QueryAll categoryName
                |> Seq.choose (fun (sn, e) -> decode e |> Option.map (fun ev -> let struct (_, id) = FsCodec.StreamName.split sn in { StreamId = id.ToString(); Event = ev }))
                |> Seq.toList

            { category = cat; subscribe = subscribe; load = load; loadCategory = loadCategory }

        let storeApi =
            match storeOpt with
            | None
            | Some Memory -> memoryStore ()
            | Some (Custom s) -> s
            | Some (EventStoreDb cs) -> eventStoreDbStore cs
            | Some (CosmosDb cs) -> cosmosDbStore cs
        let cat = storeApi.category
        let subscribe = storeApi.subscribe
        let load = storeApi.load
        let loadCategory = storeApi.loadCategory
           
        let service =
            Service
                ( FsCodec.StreamId.gen id >> Equinox.Decider.forStream log cat
                , fun cmd state -> decider.decide cmd state |> List.toArray
                , subscribe
                , load
                , loadCategory
                )
        
        match automation with
        | Some automation' ->
            let _ : System.IDisposable = service.Subscribe (
                fun streamName events ->
                    let events' = service.Load streamName
                    let view = ViewPattern.hydrate automation'.projection events'
                    let struct (_, streamId) =  FsCodec.StreamName.split streamName
                    match automation'.trigger view with
                    | Some cmd -> Async.RunSynchronously <| service.Execute (streamId.ToString()) cmd
                    | None -> ()
                    ()
            )
            ()
        | None -> ()

        match translation with
        | Some (translator, targetService) ->
            let _ : System.IDisposable = service.Subscribe (
                fun streamName _events ->
                    let history = service.Load streamName
                    let cmds = TranslationPattern.run translator history
                    let targetId = mapStreamId streamName
                    cmds |> List.iter (fun cmd ->
                        Async.RunSynchronously <| targetService.Execute targetId cmd
                    )
            )
            ()
        | None -> ()

        service

let createServiceWith<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView
    when 'Event :> TypeShape.UnionContract.IUnionContract
    and 'TEvent :> TypeShape.UnionContract.IUnionContract>
    (decider : CommandPattern.Decider<'State,'Command,'Event>)
    (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>)
    : Service<'State,'Command,'Event> =
    createService<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>
        decider
        config.categoryName
        config.automation
        config.translation
        config.mapStreamId
        config.store


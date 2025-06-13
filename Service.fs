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

type ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView> =
    { categoryName : string
      automation : AutomationPattern.Automation<'View,'State,'Event,'Command> option
      translation : (Translator<'Event,'TView,'TCommand> * Service<'TState,'TCommand,'TEvent>) option
      mapStreamId : FsCodec.StreamName -> string }

module ServiceConfig =
    /// Start a configuration with defaults for optional fields
    let create<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>
        categoryName : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView> =
        { categoryName = categoryName
          automation = None
          translation = None
          mapStreamId = defaultStreamId }

    /// Set the automation field
    let withAutomation automation (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with automation = Some automation }

    /// Set the translation field
    let withTranslation translation (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with translation = Some translation }

    /// Override the stream id mapping
    let withMapStreamId mapStreamId (config : ServiceConfig<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView>) =
        { config with mapStreamId = mapStreamId }

    
let createService<'View,'State,'Command,'Event,'TState,'TCommand,'TEvent,'TView
    when 'Event :> TypeShape.UnionContract.IUnionContract
    and 'TEvent :> TypeShape.UnionContract.IUnionContract>
    : CommandPattern.Decider<'State,'Command,'Event>
      -> string
      -> AutomationPattern.Automation<'View,'State,'Event,'Command> option
      -> (Translator<'Event,'TView,'TCommand> * Service<'TState,'TCommand,'TEvent>) option
      -> (FsCodec.StreamName -> string)
      -> Service<'State,'Command,'Event>
    = fun decider categoryName automation translation mapStreamId ->
        let store : Equinox.MemoryStore.VolatileStore<obj> = Equinox.MemoryStore.VolatileStore()
        let log = LoggerConfiguration().WriteTo.Console().CreateLogger()
        let codec : FsCodec.IEventCodec<'Event,obj,unit> = FsCodec.Box.Codec.Create()
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
        let cat 
            : Equinox.Category<'Event,'State,unit> 
            = Equinox.MemoryStore.MemoryStoreCategory(store, categoryName, codec, Array.fold decider.evolve, decider.initial)
        let subscribe : (FsCodec.StreamName -> 'Event list -> unit) -> System.IDisposable = fun callback ->
            store.Committed.Subscribe(
                fun struct (streamName, events) ->
                    callback streamName (events |> Array.map codec.Decode |> Array.choose (function | ValueSome v -> Some v | ValueNone -> None) |> Array.toList)
            )
        let loadSafe : FsCodec.StreamName -> FsCodec.ITimelineEvent<_> array = fun streamName ->
            match store.Load (streamName.ToString()) with
            | null -> Array.empty<FsCodec.ITimelineEvent<obj>>
            | events -> events
        let load : FsCodec.StreamName -> 'Event list =
            loadSafe
            >> Array.map codec.Decode
            >> Array.choose (function | ValueSome v -> Some v | ValueNone -> None)
            >> Array.toList

        let loadCategory () : ViewPattern.StreamEvent<'Event> list =
            lock categoryEvents (fun () -> categoryEvents |> Seq.toList)
           
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



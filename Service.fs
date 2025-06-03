module Service

type Service<'State,'Command,'Event> 
    internal 
        ( resolve: string -> Equinox.Decider<'Event, 'State>
        , decide : 'Command -> 'State -> 'Event array
        , subscribe : (FsCodec.StreamName -> 'Event list -> unit) -> System.IDisposable
        , load : FsCodec.StreamName -> 'Event list
        ) =

    member _.Execute : string -> 'Command -> Async<unit> = fun instanceId command ->
        (resolve instanceId).Transact <| decide command
        
    member _.Read instanceId : Async<'State> =
        (resolve instanceId).Query id
    
    member _.Subscribe : (FsCodec.StreamName -> 'Event list -> unit) -> System.IDisposable = 
        subscribe

    member _.Load : FsCodec.StreamName -> 'Event list =
        load

open Serilog
open TranslationPattern

/// Default mapping from source stream name to target instance identifier.
let defaultStreamId (streamName: FsCodec.StreamName) =
    let struct (_, streamId) = FsCodec.StreamName.split streamName
    streamId.ToString()
    
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
        let logEvents sn (events: FsCodec.ITimelineEvent<_>[]) =
            log.Information("Committed to {streamName}, events: {@events}", sn, seq { for x in events -> x.EventType })
        let _ = store.Committed.Subscribe(fun struct (sn, xs) -> logEvents sn xs)
        let codec : FsCodec.IEventCodec<'Event,obj,unit> = FsCodec.Box.Codec.Create()
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
        let load : FsCodec.StreamName -> 'Event list = loadSafe >> Array.map codec.Decode >> Array.choose (function | ValueSome v -> Some v | ValueNone -> None) >> Array.toList
           
        let service = 
            Service
                ( FsCodec.StreamId.gen id >> Equinox.Decider.forStream log cat
                , fun cmd state -> decider.decide cmd state |> List.toArray
                , subscribe
                , load
                )
        
        match automation with
        | Some automation' ->
            let _ : System.IDisposable = service.Subscribe (
                fun streamName events ->
                    let events' = service.Load streamName
                    let view = ViewPattern.hydrate automation'.projection events'
                    let struct (name, streamId) =  FsCodec.StreamName.split streamName
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



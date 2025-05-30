module Service

type Service<'State,'Command,'Event> internal (resolve: string -> Equinox.Decider<'Event, 'State>, decide : 'Command -> 'State -> 'Event array) =

    member _.Execute : string -> 'Command -> Async<unit> = fun instanceId command ->
        (resolve instanceId).Transact <| decide command
        
    member _.Read instanceId : Async<'State> =
        (resolve instanceId).Query id

let private streamId = FsCodec.StreamId.gen id

open Serilog
    
let createService<'State,'Command,'Event when 'Event :> TypeShape.UnionContract.IUnionContract>
    : CommandPattern.Decider<'State,'Command,'Event> -> string -> Service<'State,'Command,'Event> 
    = fun decider categoryName ->
        let store : Equinox.MemoryStore.VolatileStore<obj> = Equinox.MemoryStore.VolatileStore()
        let log = LoggerConfiguration().WriteTo.Console().CreateLogger()
        let logEvents sn (events: FsCodec.ITimelineEvent<_>[]) =
            log.Information("Committed to {streamName}, events: {@events}", sn, seq { for x in events -> x.EventType })
        let _ = store.Committed.Subscribe(fun struct (sn, xs) -> logEvents sn xs)
        let codec : FsCodec.IEventCodec<'Event,obj,unit> = FsCodec.Box.Codec.Create()
        let cat 
            : Equinox.Category<'Event,'State,unit> 
            = Equinox.MemoryStore.MemoryStoreCategory(store, categoryName, codec, Array.fold decider.evolve, decider.initial)
        
        Service(streamId >> Equinox.Decider.forStream log cat, fun cmd state -> decider.decide cmd state |> List.toArray) 


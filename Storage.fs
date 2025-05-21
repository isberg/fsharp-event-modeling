module Storage

open System.Collections.Concurrent 
open System

/// A stream identifier (e.g. "Order-42")
type StreamId = StreamId of string

type EventRecord<'Event> = {
    Version : int
    Payload : 'Event
    When    : System.DateTimeOffset
}

type AppendResult =
    | Ok of nextVersion:int
    | Conflict of currentVersion:int * attemptedVersion:int

type IEventStore<'Event> =
    abstract readStream       : StreamId -> Async<EventRecord<'Event> list>
    abstract appendToStream   :
        StreamId ->
        expectedVersion:int ->
        newEvents:'Event list ->
        Async<AppendResult>
    abstract subscribe        : (StreamId * EventRecord<'Event> -> unit) -> Async<unit>

type InMemoryEventStore<'Event>() =
    let store = ConcurrentDictionary<StreamId, ResizeArray<EventRecord<'Event>>>()
    let subscribers = ConcurrentBag<(StreamId * EventRecord<'Event>) -> unit>()

    interface IEventStore<'Event> with
        member _.readStream sid = async {
            let buf = store.GetOrAdd(sid, fun _ -> ResizeArray())
            return List.ofSeq buf
        }

        member _.appendToStream sid expectedVersion newEvents = async {
            let buf = store.GetOrAdd(sid, fun _ -> ResizeArray())
            // apply optimistic append under lock
            let (result, appended) =
                lock buf (fun () ->
                    let currentV =
                        if buf.Count = 0 then 0
                        else buf.[buf.Count-1].Version
                    if currentV <> expectedVersion then
                        Conflict(currentV, expectedVersion), []
                    else
                        let mutable v = expectedVersion
                        let recs =
                            newEvents 
                            |> List.map (fun payload ->
                                v <- v + 1
                                { Version = v; Payload = payload; When = DateTimeOffset.UtcNow })
                        recs |> List.iter buf.Add
                        Ok v, recs
                )
            // notify subscribers outside lock
            for r in appended do
                for callback in subscribers do
                    callback (sid, r)
            return result
        }

        member _.subscribe callback = async {
            subscribers.Add callback
            return ()
        }

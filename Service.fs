module Service

open System
open Storage
open CommandPattern

/// A service that binds a Decider to a store resolver, exposing
/// a transactional command API with optimistic concurrency & retry.
type Service<'State,'Command,'Event>
  (decider      : Decider<'State,'Command,'Event>
  , resolveStore : StreamId -> IEventStore<'Event>
  , maxRetries   : int
  ) =

  /// Read current event history and state
  member _.Load(id: StreamId) = async {

    let store = resolveStore id
    let!records = store.readStream id
    let events = records |> List.map(fun r->r.Payload)

    let state = hydrate decider events
    state |> printfn "State loaded: %A"

    let version =
      records
      |> List.tryLast
      |> Option.map(fun r->r.Version)
      |> Option.defaultValue 0

    return state, version
  }

  /// Public GetState
  member this.GetState id = async {
    let! state, _ = this.Load id
    return state
  }

  /// Try to append, with retries on version conflict
  member this.Transact (id:StreamId) (command:'Command) : Async<'State> =
    let rec attempt n = async {
      if n > maxRetries then
        return failwithf "Exceeded %d retries for %A" maxRetries id
      // 1. load
      let! state, version = this.Load id
      // 2. decide & evolve
      let newState, newEvents = update decider state command
      // 3. append
      let store = resolveStore id
      let! result = store.appendToStream id version newEvents
      
      match result with
      | Ok _       -> return newState
      | Conflict(currentV, _) ->
          // retry with latest
          return! attempt (n + 1)
    }
    attempt 0

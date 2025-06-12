module ViewPattern

type Projection<'View,'Event> = {
    initial: 'View
    project: 'View -> 'Event-> 'View
}

/// Wrapper carrying the originating stream id for a given event
type StreamEvent<'Event> = {
    StreamId: string
    Event: 'Event
}

let replay = List.fold

let hydrate : Projection<'View,'Event> -> 'Event list -> 'View = fun p ->
    replay p.project p.initial

let update = replay


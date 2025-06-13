module ViewPattern

type ProjectionSpec<'View,'Event> = {
    initial: 'View
    project: 'View -> 'Event -> 'View
}

/// Wrapper carrying the originating stream id for a given event
type StreamEvent<'Event> = {
    StreamId: string
    Event: 'Event
}

/// Distinguishes between per-stream and category-level projections
type Projection<'View,'Event> =
    | StreamProjection of string * ProjectionSpec<'View,'Event>
    | CategoryProjection of string * ProjectionSpec<'View, StreamEvent<'Event>>

let replay = List.fold

let hydrate : ProjectionSpec<'View,'Event> -> 'Event list -> 'View = fun p ->
    replay p.project p.initial

let update = replay


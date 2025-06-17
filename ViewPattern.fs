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
/// Identify whether a projection operates over a single stream or an entire category
/// The projection specification itself does not carry a view name
type Projection<'View,'Event> =
    | StreamProjection of ProjectionSpec<'View,'Event>
    | CategoryProjection of ProjectionSpec<'View, StreamEvent<'Event>>

let replay = List.fold

let hydrate : ProjectionSpec<'View,'Event> -> 'Event list -> 'View = fun p ->
    replay p.project p.initial

let update = replay


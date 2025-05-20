module CommandPattern

type Decider<'State,'Command,'Event> = {
    initial : 'State
    decide  : 'Command -> 'State -> 'Event list
    evolve  : 'State -> 'Event -> 'State
}

let replay = List.fold

let hydrate : Decider<'State,'Command,'Event> -> 'Event list -> 'State = fun d ->
    replay d.evolve d.initial

let execute : Decider<'State,'Command,'Event> -> 'Event list -> 'Command -> 'Event list = fun d history command ->
    let state = hydrate d history
    d.decide command state

let update : Decider<'State,'Command,'Event> -> 'State -> 'Command -> 'State * 'Event list = fun d state command ->
    let events   = d.decide command state
    let newState = List.fold d.evolve state events
    newState, events


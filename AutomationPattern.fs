module AutomationPattern

open CommandPattern
open ViewPattern

type Automation<'View,'State,'Event,'Command> = {
    projection : ProjectionSpec<'View,'Event>
    trigger    : 'View -> 'Command option 
    decider    : Decider<'State,'Command,'Event>
}

let runIncremental
    (automation : Automation<'View,'State,'Event,'Command>)
    (currentView : 'View)
    (currentState : 'State)
    (newEvents : 'Event list)
    : ('Event list * 'View * 'State) =
    let updatedView  = update automation.projection.project currentView newEvents
    let updatedState = List.fold automation.decider.evolve currentState newEvents
    match automation.trigger updatedView with
    | None -> ([], updatedView, updatedState)
    | Some cmd ->
        let (finalState, evts) = CommandPattern.update automation.decider updatedState cmd
        (evts, updatedView, finalState)

let run
    (automation : Automation<'View,'State,'Event,'Command>)
    (history : 'Event list)
    : 'Event list =
    let initView  = ViewPattern.hydrate automation.projection history
    let initState = CommandPattern.hydrate automation.decider history
    let (evts, _, _) = runIncremental automation initView initState []
    evts

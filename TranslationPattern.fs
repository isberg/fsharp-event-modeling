module TranslationPattern

open ViewPattern

/// Translates a view of source events into a command for another service
type Translator<'SourceEvent,'SourceView,'Command> = {
    projection : ProjectionSpec<'SourceView,'SourceEvent>
    translate  : 'SourceView -> 'Command option
}

let runIncremental
    (translator : Translator<'SourceEvent,'SourceView,'Command>)
    (currentView : 'SourceView)
    (newEvents : 'SourceEvent list)
    : 'Command list * 'SourceView =
    let updatedView =
        update translator.projection.project currentView newEvents
    match translator.translate updatedView with
    | Some cmd -> ([ cmd ], updatedView)
    | None -> ([], updatedView)

let run
    (translator : Translator<'SourceEvent,'SourceView,'Command>)
    (history : 'SourceEvent list)
    : 'Command list =
    let view = hydrate translator.projection history
    translator.translate view |> Option.toList


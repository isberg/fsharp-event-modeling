module GenericResource

open Service
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Suave.Writers
open System.Text.Json
open System.Text.Json.Serialization
open FsCodec.SystemTextJson

    
// Shared JSON options with F# DU support
let jsonOptions = 
    JsonFSharpOptions
        .Default()
        .WithUnionExternalTag()
        .WithUnionUnwrapFieldlessTags()
        .WithUnionTagCaseInsensitive()
        .ToJsonSerializerOptions()

/// JSON serialization helper
let toJson (data: 'T) : WebPart =
    Writers.setMimeType "application/json; charset=utf-8"
    >=> OK (JsonSerializer.Serialize(data, jsonOptions))

/// Box a projection so heterogeneous view types can be used together
let private boxProjection name (projection: ViewPattern.ProjectionSpec<'View,'Event>) :
    string * ('Event list -> obj) =
    name, fun events -> ViewPattern.hydrate projection events |> box

/// Box a category projection operating over multiple streams
let private boxCategoryProjection name (projection: ViewPattern.ProjectionSpec<'View, ViewPattern.StreamEvent<'Event>>) :
    string * (ViewPattern.StreamEvent<'Event> list -> obj) =
    name, fun events -> ViewPattern.hydrate projection events |> box

/// Projection scope for routing
type BoxedProjection<'Event> =
    | StreamProjection of string * ('Event list -> obj)
    | CategoryProjection of string * (ViewPattern.StreamEvent<'Event> list -> obj)

/// Box a typed projection under a given view name
let box name (projection: ViewPattern.Projection<'View,'Event>) : BoxedProjection<'Event> =
    match projection with
    | ViewPattern.StreamProjection spec ->
        let n, f = boxProjection name spec
        StreamProjection (n, f)
    | ViewPattern.CategoryProjection spec ->
        let n, f = boxCategoryProjection name spec
        CategoryProjection (n, f)

/// Helper to wrap a stream projection into a scoped one
let boxedStream name projection =
    box name (ViewPattern.StreamProjection projection)

/// Helper to wrap a category projection into a scoped one
let boxedCategory name projection =
    box name (ViewPattern.CategoryProjection projection)

let private partition (projections: BoxedProjection<'Event> list) :
    (string * ('Event list -> obj)) list * (string * (ViewPattern.StreamEvent<'Event> list -> obj)) list =
    let streams =
        projections
        |> List.choose (function StreamProjection (n,f) -> Some (n,f) | _ -> None)
    let categories =
        projections
        |> List.choose (function CategoryProjection (n,f) -> Some (n,f) | _ -> None)
    streams, categories

let deserialize<'TValue> : byte array -> Result<'TValue,string> = fun bytes ->
    try
        Ok <| JsonSerializer.Deserialize<'TValue>(bytes, jsonOptions)
    with ex -> Error ex.Message

/// Configure routes for a resource identified by 'idPath' (e.g. "counters/%s")
let configure<'State,'Command,'Event>
  (category : string)
  (path: PrintfFormat<string -> WebPart, unit, string, WebPart, string> )
  (viewPath: PrintfFormat<string -> string -> WebPart, unit, string, WebPart, string * string> )
  (service: Service<'State,'Command,'Event>)
  (projections: BoxedProjection<'Event> list)
  : WebPart<HttpContext> =
      let streamProjections, _ = partition projections
      choose [
        GET >=> pathScan viewPath (fun (id,view) ctx -> async {
            let key = FsCodec.StreamName.compose category [| id |]
            let history = service.Load key
            match streamProjections |> List.filter (fun (n,_) -> n = view) with
            | [] -> return! BAD_REQUEST $"No view named '{view}'" ctx
            | (_, hydrateView) :: _ ->
                let view = hydrateView history
                return! (toJson view) ctx
        })
        // GET /{resource}/{id}
        GET >=> pathScan path (fun id ctx -> async {
          let! state = service.Read id
          return! toJson state ctx
        })
        // POST /{resource}/{id}
        POST >=> pathScan path (fun id ctx -> async {
            match deserialize<'Command> ctx.request.rawForm with
            | Error msg -> return! BAD_REQUEST msg ctx
            | Ok cmd ->
                try
                    do! service.Execute id cmd
                    let! state = service.Read id
                    return! (toJson state) ctx
                with ex -> return! CONFLICT ex.Message ctx
        })
      ]

/// Configure routes with additional category-level projections
let configureWithCategory<'State,'Command,'Event>
  (category : string)
  (path: PrintfFormat<string -> WebPart, unit, string, WebPart, string> )
  (viewPath: PrintfFormat<string -> string -> WebPart, unit, string, WebPart, string * string> )
  (categoryViewPath: PrintfFormat<string -> WebPart, unit, string, WebPart, string>)
  (service: Service<'State,'Command,'Event>)
  (projections: BoxedProjection<'Event> list)
  : WebPart<HttpContext> =
      let streamProjections, categoryProjections = partition projections
      choose [
        GET >=> pathScan categoryViewPath (fun view ctx -> async {
            match categoryProjections |> List.filter (fun (n,_) -> n = view) with
            | [] -> return! BAD_REQUEST $"No view named '{view}'" ctx
            | (_, hydrateView) :: _ ->
                let events = service.LoadCategory()
                let v = hydrateView events
                return! (toJson v) ctx
        })
        // per-stream projections
        GET >=> pathScan viewPath (fun (id,view) ctx -> async {
            let key = FsCodec.StreamName.compose category [| id |]
            let history = service.Load key
            match streamProjections |> List.filter (fun (n,_) -> n = view) with
            | [] -> return! BAD_REQUEST $"No view named '{view}'" ctx
            | (_, hydrateView) :: _ ->
                let view = hydrateView history
                return! (toJson view) ctx
        })
        // GET /{resource}/{id}
        GET >=> pathScan path (fun id ctx -> async {
          let! state = service.Read id
          return! toJson state ctx
        })
        // POST /{resource}/{id}
        POST >=> pathScan path (fun id ctx -> async {
            match deserialize<'Command> ctx.request.rawForm with
            | Error msg -> return! BAD_REQUEST msg ctx
            | Ok cmd ->
                try
                    do! service.Execute id cmd
                    let! state = service.Read id
                    return! (toJson state) ctx
                with ex -> return! CONFLICT ex.Message ctx
        })
      ]

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

let deserialize<'TValue> : byte array -> Result<'TValue,string> = fun bytes ->
    try
        Ok <| JsonSerializer.Deserialize<'TValue>(bytes, jsonOptions)
    with ex -> Error ex.Message

/// Configure routes for a resource identified by 'idPath' (e.g. "counters/%s")
let configure<'State,'Command,'Event>
  (path: PrintfFormat<string -> WebPart, unit, string, WebPart, string> )
  (service: Service<'State,'Command,'Event>)
  : WebPart<HttpContext> =
      choose [
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

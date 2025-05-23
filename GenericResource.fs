module GenericResource

open System.Text.Json
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Suave.Writers
open Storage
open Service

/// Configure routes for a resource identified by 'idPath' (e.g. "counters/%s")
let configure<'State,'Command,'Event>
  (parseId: string -> StreamId)
  (parseCmd: JsonDocument -> 'Command)
  (toJson: 'State -> WebPart)
  (service: Service<'State,'Command,'Event>)
  (idFormat: PrintfFormat<string -> WebPart, unit, string, WebPart, string> )
  : WebPart =
  choose [
    // GET /{resource}/{id}
    GET >=> pathScan idFormat (fun idStr -> fun ctx -> async {
      let id = parseId idStr
      let! state = service.GetState id
      return! toJson state ctx
    })
    // POST /{resource}/{id}
    POST >=> pathScan idFormat (fun idStr -> fun ctx -> async {
      let id = parseId idStr
      // parse JSON
      let body = System.Text.Encoding.UTF8.GetString ctx.request.rawForm
      use doc = JsonDocument.Parse(body)
      let cmd = parseCmd doc
      try
        let! newState = service.Transact id cmd
        return! toJson newState ctx
      with ex -> return! CONFLICT ex.Message ctx
    })
  ]


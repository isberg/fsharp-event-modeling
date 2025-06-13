open System
open Suave

open CommandPattern
open ViewPattern
open AutomationPattern
open TranslationPattern

// Inventory domain types

// Commands that can be issued to the inventory
// Add or remove stock, or request a reorder internally

type InventoryCommand =
    | AddStock of int
    | RemoveStock of int
    | RequestReorder

// Events emitted by the inventory

type InventoryEvent =
    | StockAdded of int
    | StockRemoved of int
    | ReorderRequested of int
    interface TypeShape.UnionContract.IUnionContract

// State is the current quantity on hand

type InventoryState = int

// Decider implementing the command pattern for inventory

let inventoryDecider : Decider<InventoryState, InventoryCommand, InventoryEvent> =
    {
        initial = 0
        decide = fun cmd state ->
            match cmd with
            | AddStock qty when qty > 0 ->
                [ StockAdded qty ]
            | RemoveStock qty when qty > 0 && state >= qty ->
                [ StockRemoved qty ]
            | RequestReorder ->
                // request a fixed amount when stock is low
                [ ReorderRequested 10 ]
            | _ ->
                []
        evolve = fun state event ->
            match event with
            | StockAdded qty -> state + qty
            | StockRemoved qty -> state - qty
            | ReorderRequested _ -> state
    }

// Projection of current stock level (View pattern)

let stockProjection : Projection<int, InventoryEvent> =
    { initial = 0
      project = fun current -> function
        | StockAdded qty -> current + qty
        | StockRemoved qty -> current - qty
        | ReorderRequested _ -> current }

// Automation to trigger reorder when stock below threshold

let threshold = 5

let lowStockAutomation : Automation<int, InventoryState, InventoryEvent, InventoryCommand> =
    { projection = stockProjection
      trigger = fun qty -> if qty < threshold then Some RequestReorder else None
      decider = inventoryDecider }

// Supplier domain for external orders

type SupplierCommand =
    | PlaceOrder of int

type SupplierEvent =
    | OrderPlaced of int
    interface TypeShape.UnionContract.IUnionContract

type SupplierState = int

let supplierDecider : Decider<SupplierState, SupplierCommand, SupplierEvent> =
    {
        initial = 0
        decide = fun cmd _state ->
            match cmd with
            | PlaceOrder qty when qty > 0 -> [ OrderPlaced qty ]
            | _ -> []
        evolve = fun state event ->
            match event with
            | OrderPlaced _ -> state + 1
    }

// Translator from inventory events to supplier commands

let lastEventProjection : Projection<InventoryEvent option, InventoryEvent> =
    { initial = None
      project = fun _ e -> Some e }

let reorderTranslator : Translator<InventoryEvent, InventoryEvent option, SupplierCommand> =
    { projection = lastEventProjection
      translate = function
        | Some (ReorderRequested qty) -> Some (PlaceOrder qty)
        | _ -> None }

// Create the services

let supplierService =
    Service.createService supplierDecider "Supplier" None None Service.defaultStreamId

let inventoryService =
    Service.createService inventoryDecider "Inventory"
        (Some lowStockAutomation)
        (Some (reorderTranslator, supplierService))
        Service.defaultStreamId

[<EntryPoint>]
let main _ =
    // log events from both services
    let _ : IDisposable =
        inventoryService.Subscribe (fun name events -> printfn "%A" (name, events))
    let _ : IDisposable =
        supplierService.Subscribe (fun name events -> printfn "%A" (name, events))

    let inventoryApp =
        GenericResource.configure
            "Inventory"
            "/inventory/%s"
            "/inventory/%s/%s"
            inventoryService
            [ GenericResource.boxedStream "stock" stockProjection ]

    let supplierApp =
        GenericResource.configure
            "Supplier"
            "/supplier/%s"
            "/supplier/%s/%s"
            supplierService
            []

    let app = choose [ inventoryApp; supplierApp ]

    Suave.Web.startWebServer Suave.Web.defaultConfig app
    0


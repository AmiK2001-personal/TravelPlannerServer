module TravelPlannerServer.Api

open System
open Microsoft.AspNetCore.Http
open System.Threading.Tasks
open Giraffe
open FSharp.Control.Tasks

open TravelPlannerServer.Domain.Models
open TravelPlannerServer.Operators.Result
open TravelPlannerServer.Database.Db
open TravelPlannerServer.Operators

type PaginatedResponse<'a> = { count: int; items: seq<'a> }

let results (sequence: seq<'a>) =
    { items = sequence
      count = sequence |> Seq.length }

module Query =
    let tryGetValue key (queryCollection: IQueryCollection) =
        match queryCollection.ContainsKey key with
        | true -> Some ^ queryCollection.[key].ToString()
        | false -> None

module ServerErrors =
    let NotImplemented =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task { return! (ServerErrors.notImplemented (text "Not implemented")) next ctx }

type Endpoint<'a>() =
    member _.Get(_: string) : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.GetAll : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.Post : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.Delete(_: string) : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.Put(_: string) : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.Patch(_: string) : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.Head(_: string) : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented
    member _.HeadAll : HttpFunc -> HttpContext -> Task<HttpContext option> = ServerErrors.NotImplemented

type Message =
    static member fromResult input = input |> either json (ToString >> json)

module Controllers =
    type AccountController() =
        inherit Endpoint<Account>()

        member __.Get(id: string) =
            BsonObjectId.tryParse id
            >>> ToString
            >>> AccountRepository.Read
            |> Message.fromResult

        member __.Post =
            fun (next: HttpFunc) (ctx: HttpContext) ->
                task {
                    let! accountFromBody = fromBody<Account> ctx

                    return!
                        (accountFromBody >>> AccountRepository.Add
                         |> Message.fromResult)
                            next
                            ctx
                }


        member __.Delete id =
            BsonObjectId.tryParse id
            >>> ToString
            >>> AccountRepository.Delete
            |> Message.fromResult

        member __.Put id =
            fun (next: HttpFunc) (ctx: HttpContext) ->
                task {
                    let! accountFromBody = fromBody<Account> ctx

                    return!
                        (accountFromBody >>> AccountRepository.Edit id
                         |> Message.fromResult)
                            next
                            ctx
                }

        member __.GetAll =
            fun (next: HttpFunc) (ctx: HttpContext) ->
                task {
                    return!
                        (AccountRepository.Browse(fun _ -> true)
                         |> Task.map (List.ofSeq >> results) |> ok
                         |> Message.fromResult)
                            next
                            ctx
                }

    type TravelController() =
        inherit Endpoint<Travel>()

    let Account = AccountController()
    let Travel = TravelController()

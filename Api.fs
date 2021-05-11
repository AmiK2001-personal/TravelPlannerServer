module TravelPlannerServer.Api

open System
open Microsoft.AspNetCore.Http
open System.Threading.Tasks
open FSharp.Data
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
    let NotImplemented : HttpHandler =
        fun next ctx -> task { return! (ServerErrors.notImplemented (text "Not implemented")) next ctx }

type Message =
    static member fromResult input = input |> either json (ToString >> json)
    static member from input = input |> json

let tryParseObjectBsonId id = BsonObjectId.tryParse id >>> ToString

let toResults x =
    x |> Task.map (List.ofSeq >> results) |> ok

module Account =
    let accountFromBody = fromBody<Account>

    let Get (id: string) =
        BsonObjectId.tryParse id
        >>> ToString
        >>> AccountRepository.Read
        |> Message.fromResult
        
    let IsRegistered (login: string) =
        task {
            let! res = AccountRepository.Browse (fun x -> x.login = login)
            if res |> Seq.length > 0 then
                return true
            else
                return false;
            } |> Message.from

//    type AuthorizationInfo = JsonProvider<""" { "login":"John", "password":"pass" } """>
    let IsPasswordMatch : HttpHandler =
        fun next ctx ->
            task {
                let query = ctx.Request.Query
                let login = Query.tryGetValue "login" query
                let password = Query.tryGetValue "login" query
                
                if login.IsSome && password.IsSome then
                    let! res = AccountRepository.Browse (fun x -> x.login = login.Value)
                
                    return!
                        (Message.from <| match res |> Seq.tryHead with
                        | Some x -> true
                        | None -> false)
                        next
                        ctx
                else
                    return! (false |> Message.from) next ctx
            }
    
    let Register : HttpHandler =
        fun next ctx ->
            task {
                let! accountFromBody = fromBody<Account> ctx

                return!
                    (accountFromBody >>> AccountRepository.Add
                     |> Message.fromResult)
                        next
                        ctx
            }


    let Delete id =
        BsonObjectId.tryParse id
        >>> ToString
        >>> AccountRepository.Delete
        |> Message.fromResult

    let Put id : HttpHandler =
        fun next ctx ->
            task {
                let! accountFromBody = fromBody<Account> ctx

                return!
                    (accountFromBody >>> AccountRepository.Edit id
                     |> Message.fromResult)
                        next
                        ctx
            }

    let GetAll : HttpHandler =
        fun next ctx ->
            task {
                return!
                    (AccountRepository.Browse(fun _ -> true)
                     |> Task.map (List.ofSeq >> results)
                     |> ok
                     |> Message.fromResult)
                        next
                        ctx
            }

module TravelPlannerServer.Database.Db

open System
open System.IO
open System.Linq.Expressions
open MongoDB.Driver
open FSharp.Control.Tasks
open System.Threading.Tasks
open FSharp.Data

open TravelPlannerServer.Operators.Result
open TravelPlannerServer.Validation
open TravelPlannerServer.Domain.Models

type IRepository<'a> =
    abstract Add : 'a -> Task<Result<'a, DomainError>>
    abstract Read : string -> Task<Result<'a, DomainError>>
    abstract Edit : string -> 'a -> Task<'a>
    abstract Delete : string -> Task<Result<'a, DomainError>>
    abstract Browse : Expression<Func<'a, bool>> -> Task<seq<'a>>
    abstract Collection : IMongoCollection<'a>

module Config =
    type DbConfig =
        JsonProvider<"""{
        "config": {
                "login": "login",
                "password": "password",
                "url" : "example.net",
                "db_name" : "db_name"
        }
    }""">

    let dbConfig =
        DbConfig.Parse(File.ReadAllText "dbsettings.json")

    let connectionString =
        $"mongodb+srv://{dbConfig.Config.Login}:{dbConfig.Config.Password}@{dbConfig.Config.Url}/{dbConfig.Config.DbName}?retryWrites=true&w=majority"

    let client = MongoClient connectionString

    let db =
        client.GetDatabase(dbConfig.Config.DbName)

let AccountRepository =
    { new IRepository<Account> with
        member __.Collection =
            Config.db.GetCollection<Account> "accounts"

        member __.Add account =
            match (account :> IValidation<Account>).Validate() with
            | Ok ->
                let newAccount = account |> Account.encryptPassword

                task {
                    do! __.Collection.InsertOneAsync newAccount
                    return newAccount |> ok
                }
            | Errors errors -> task { return AccountValidationError errors |> error }

        member __.Read id =
            task {
                match __.Collection.Find(fun x -> x.id = id).Any() with
                | true ->
                    let! accCursor = __.Collection.FindAsync<Account>(fun x -> x.id = id)
                    let! acc = accCursor.FirstOrDefaultAsync()
                    return (acc |> ok)
                | false -> return (AccountNotFounded id |> error)
            }

        member __.Edit id newAccount =
            task {
                return! __.Collection.FindOneAndReplaceAsync<Account>((fun x -> x.id = id), { newAccount with id = id })
            }

        member __.Delete id =
            task {
                match __.Collection.Find(fun x -> x.id = id).Any() with
                | true ->
                    let! acc = __.Collection.FindOneAndDeleteAsync<Account>(fun x -> x.id = id)
                    return acc |> ok
                | false -> return AccountNotFounded id |> error
            }

        member __.Browse predicate =
            task {
                let! asyncCursor = __.Collection.FindAsync(predicate)
                return asyncCursor.ToEnumerable()
            } }

let TravelRepository =
    { new IRepository<Travel> with
        member __.Collection =
            Config.db.GetCollection<Travel> "travels"

        member __.Add travel =
            task {
                do! __.Collection.InsertOneAsync travel
                return travel |> ok
            }

        member __.Read id =
            task {
                match __.Collection.Find(fun x -> x.id = id).Any() with
                | true ->
                    let! accCursor = __.Collection.FindAsync<Travel>(fun x -> x.id = id)
                    let! acc = accCursor.FirstOrDefaultAsync()
                    return (acc |> ok)
                | false -> return (TravelNotFounded id |> error)
            }

        member __.Edit id newTravel =
            task {
                return! __.Collection.FindOneAndReplaceAsync<Travel>((fun x -> x.id = id), { newTravel with id = id }) }

        member __.Delete id =
            task {
                match __.Collection.Find(fun x -> x.id = id).Any() with
                | true ->
                    let! acc = __.Collection.FindOneAndDeleteAsync<Travel>(fun x -> x.id = id)
                    return acc |> ok
                | false -> return TravelNotFounded id |> error
            }

        member __.Browse predicate =
            task {
                let! asyncCursor = __.Collection.FindAsync(predicate)
                return asyncCursor.ToEnumerable()
            } }

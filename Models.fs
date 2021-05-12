module TravelPlannerServer.Domain.Models

open System
open System.Security.Cryptography
open Microsoft.AspNetCore.Cryptography.KeyDerivation
open Giraffe
open Microsoft.AspNetCore.Http
open MongoDB.Bson.Serialization.Attributes
open MongoDB.Bson
open FSharp.Control.Tasks

open TravelPlannerServer.Operators
open TravelPlannerServer.Validation
open TravelPlannerServer.Operators.Result

type DomainError =
    // Account errors
    | AccountValidationError of ValidationItem list
    | AccountNotFounded of string

    // Travel errors
    | TravelNotFounded of string
    | TravelValidationError of ValidationItem list
    | TravelQueryError

    // Db errors
    | InvalidBsonId
    // Controller error
    | InvalidQuery
    // Json errors
    | JsonParseError of string
    override this.ToString() =
        match this with
        // DbErrors
        | InvalidBsonId -> "Invalid BsonId"
        // AccountErrors
        | AccountNotFounded id -> $"Account {id} not founded. "
        | AccountValidationError errorMessages -> $"Account is not valid.\nMessage: {errorMessages}"
        // TravelErrors
        | TravelNotFounded id -> $"Travel {id} not founded. "
        | TravelValidationError errorMessages -> $"Travel is not valid.\nMessage: {errorMessages}"
        | TravelQueryError -> "Travel query error. "
        // QueryErrors
        | InvalidQuery -> "Invalid query"
        | JsonParseError s -> $"Json cannot be parsed. {s}"

type Id = String

module BsonObjectId =
    let generate () = BsonObjectId(ObjectId.GenerateNewId())

    let tryParse id =
        let isValid, objectId = ObjectId.TryParse id

        match isValid with
        | true -> BsonObjectId(objectId) |> ok
        | false -> InvalidBsonId |> error

let fromBody<'a> (ctx: HttpContext) =
    task {
        try
            let! body = ctx.ReadBodyFromRequestAsync()
            return body |> Json.fromJson<'a> |> ok
        with _ ->
            return
                error
                ^ JsonParseError $"Can't parse json from body into {typeof<'a>}"
    }

[<CLIMutable>]
type PersonalInfo =
    { firstname: string
      lastname: string
      patronymic: string option
      age: int option }

[<CLIMutable>]
type WeatherInfo = { tempC: float; timestamp: DateTime }

[<CLIMutable>]
type Goodie =
    { [<BsonId>]
      [<BsonRepresentation(BsonType.ObjectId)>]
      id: string
      name: string
      cost: double option
      quantity: int }

[<CLIMutable>]
type Location = { X: double; Y: double; name: string }

[<CLIMutable>]
type Route = { from: Location; ``to``: Location }

[<CLIMutable>]
type TransportWaste =
    { [<BsonId>]
      [<BsonRepresentation(BsonType.ObjectId)>]
      id: String
      route: Route option
      name: string option
      cost: double }

type TravellerType =
    | Planner
    | Creator
    | Guide
    | Tourist

[<CLIMutable>]
type EncryptedPassword =
    { hash: string
      salt: string }
    static member create password =
        let mutable salt = Array.zeroCreate<byte> (128 / 8)
        use rng = RandomNumberGenerator.Create()
        rng.GetBytes salt

        { hash =
              KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 10000, 256 / 8)
              |> Convert.ToBase64String
          salt = salt |> Convert.ToBase64String }

[<CLIMutable>]
type Account =
    { [<BsonId>]
      [<BsonRepresentation(BsonType.ObjectId)>]
      id: string
      login: string
      encryptedPassword: EncryptedPassword option
      password: string option
      personalInfo: PersonalInfo }

    interface IValidation<Account> with
        member __.Validate() =
            createValidatorFor () {
                validate (fun o -> o.login) [ isNotEmpty; hasMaxLengthOf 50 ]
                validateWhen (fun o -> o.password.IsSome) (fun o -> o.password.Value) [ isNotEmpty; hasMaxLengthOf 50 ]
                validate (fun o -> o.password) []
            }
            <| __

    static member create id login password personalInfo =
        { id = id
          login = login
          password = password
          encryptedPassword = None
          personalInfo = personalInfo }

    static member encryptPassword account =
        { account with
              id = BsonObjectId.generate().ToString()
              encryptedPassword =
                  EncryptedPassword.create account.password.Value
                  |> Some
              password = None }


type Image = Array of byte

[<CLIMutable>]
type Travel =
    { [<BsonId>]
      [<BsonRepresentation(BsonType.ObjectId)>]
      id: string
      travellers: (Id * TravellerType) list
      name: string
      goodies: Goodie list option
      transportWastes: TransportWaste list option
      locations: Location list option
      images: Image list option }
    
    static member create name accountId =
        {
            id = BsonObjectId.generate().ToString()
            travellers = [(accountId, Creator)]
            name = name
            goodies = None
            transportWastes = None
            locations = None
            images = None
        }

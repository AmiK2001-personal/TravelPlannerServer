module TravelPlannerServer.Json

open FSharp.Json

let toJson object = object |> Json.serialize

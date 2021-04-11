module TravelPlannerServer.Operators

open System
open System.Threading.Tasks
open FSharp.Control.Tasks

// Святая крышечка
let inline (^) f x = f x

module Result =
    type ResultBuilder() =
        member __.Return(x) = Ok x

        member __.ReturnFrom(m: 'T option) = m

        member __.Bind(m, f) = Result.bind f m

        member __.Zero() = None

        member __.Combine(m, f) = Option.bind f m

        member __.Delay(f: unit -> _) = f

        member __.Run(f) = f ()

        member __.TryWith(m, h) =
            try
                __.ReturnFrom(m)
            with e -> h e

        member __.TryFinally(m, compensation) =
            try
                __.ReturnFrom(m)
            finally
                compensation ()

        member __.Using(res: #IDisposable, body) =
            __.TryFinally(
                body res,
                fun () ->
                    match res with
                    | null -> ()
                    | disp -> disp.Dispose()
            )

        member __.While(guard, f) =
            if not (guard ()) then
                Some()
            else
                do f () |> ignore
                __.While(guard, f)

        member __.For(sequence: seq<_>, body) =
            __.Using(
                sequence.GetEnumerator(),
                (fun enum -> __.While(enum.MoveNext, __.Delay(fun () -> body enum.Current)))
            )

    let result = ResultBuilder()
    let ok x = Ok x
    let error x = Error x

    let either onOk onError inputResult =
        match inputResult with
        | Ok s -> onOk s
        | Error f -> onError f

    let switch f = f >> ok

    let tee f x =
        f x |> ignore
        x

    let tryCatch f exnHandler x =
        try
            f x |> error
        with ex -> exnHandler ex |> error

    let doubleMap onOk onError = either (onOk >> error) (onError >> ok)

    let plus addOk addError switch1 switch2 x =
        match (switch1 x), (switch2 x) with
        | Ok s1, Ok s2 -> ok (addOk s1 s2)
        | Error f1, Ok _ -> error f1
        | Ok _, Error f2 -> error f2
        | Error f1, Error f2 -> error (addError f1 f2)

    let ignoreError newInput input =
        match input with
        | Ok _ -> newInput
        | Error f -> Failure f

// Связывает результат с функцией возвращающей другой результат
let (>>=) x f = x |> Result.bind f

// Композиция
let (>=>) s1 s2 = s1 >> Result.bind s2

let (&&&) v1 v2 =
    let addSuccess r1 _ = r1
    let addFailure s1 s2 = s1 + "; " + s2
    Result.plus addSuccess addFailure v1 v2

// Применят функцию на значение результата.
let (>>>) input switch = Result.bind (switch >> Ok) input

/// Выполняет функцию над значением результата и передает значение дальше.
let (>->) input func =
    match input with
    | Ok x ->
        func x |> ignore
        Ok x
    | Error e -> Error e

module Task =
    // Создает Task возвращающий значение.
    let singleton value = value |> Task.FromResult

    let bind (f: 'a -> Task<'b>) (x: Task<'a>) =
        task {
            let! x = x
            return! f x
        }

    let map f x = x |> bind (f >> singleton)

let inline ToString x = x.ToString()

module String =
    let tryConvertToInt (str: string option) =
        match str with
        | Some value ->
            try
                value |> int |> Some
            with :? InvalidCastException -> None
        | None -> None

module Json =
    let fromJson<'a> (json: string) =
        Utf8Json.JsonSerializer.Deserialize<'a> json

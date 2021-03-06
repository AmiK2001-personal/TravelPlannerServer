module TravelPlannerServer.Validation

open System
open System.Linq.Expressions

type ValidationItem =
    { message: string
      property: string
      errorCode: string }

type ValidationState =
    | Errors of ValidationItem list
    | Ok

type PropertyValidatorConfig =
    { predicate: obj -> bool
      validators: (obj -> ValidationState) list }

type MatchResult<'propertyType> =
    | Unwrapped of 'propertyType
    | Ignore

let private getPropertyPath (expression: Expression<Func<'commandType, 'propertyType>>) =
    let objectQualifiedExpression = expression.Body.ToString()
    let indexOfDot = objectQualifiedExpression.IndexOf('.')

    if indexOfDot = -1 then
        objectQualifiedExpression
    else
        objectQualifiedExpression.Substring(indexOfDot + 1)

let private packageValidator
    (propertyGetterExpr: Expression<Func<'targetType, 'propertyType>>)
    (validator: string -> 'propertyType -> ValidationState)
    =
    let propertyName = propertyGetterExpr |> getPropertyPath

    let propertyGetter = propertyGetterExpr.Compile()
    fun (value: obj) -> validator propertyName (propertyGetter.Invoke(value :?> 'targetType))

let private packageValidatorWithSingleCaseUnwrapper
    (propertyGetterExpr: Expression<Func<'targetType, 'wrappedPropertyType>>)
    (unwrapper: 'wrappedPropertyType -> 'propertyType)
    (validator: string -> 'propertyType -> ValidationState)
    =
    let propertyName = propertyGetterExpr |> getPropertyPath

    let propertyGetter = propertyGetterExpr.Compile()
    fun (value: obj) -> validator propertyName (unwrapper (propertyGetter.Invoke(value :?> 'targetType)))

let private packageValidatorWithUnwrapper
    (propertyGetterExpr: Expression<Func<'targetType, 'wrappedPropertyType>>)
    (unwrapper: 'wrappedPropertyType -> MatchResult<'propertyType>)
    (validator: string -> 'propertyType -> ValidationState)
    =
    let propertyName = propertyGetterExpr |> getPropertyPath

    let propertyGetter = propertyGetterExpr.Compile()

    fun (value: obj) ->
        match (unwrapper (propertyGetter.Invoke(value :?> 'targetType))) with
        | Unwrapped unwrappedValue -> validator propertyName unwrappedValue
        | Ignore -> Ok

let private packageValidatorRequired
    (propertyGetterExpr: Expression<Func<'targetType, 'propertyType option>>)
    (validator: string -> 'propertyType -> ValidationState)
    =
    let propertyName = propertyGetterExpr |> getPropertyPath

    let propertyGetter = propertyGetterExpr.Compile()

    fun (value: obj) ->
        match propertyGetter.Invoke(value :?> 'targetType) with
        | Some v -> validator propertyName v
        | None ->
            Errors(
                [ { errorCode = "validatorRequired"
                    property = propertyName
                    message = "Option type is required" } ]
            )

let private packageValidatorUnrequired
    (propertyGetterExpr: Expression<Func<'targetType, 'propertyType option>>)
    (validator: string -> 'propertyType -> ValidationState)
    =
    let propertyName = propertyGetterExpr |> getPropertyPath

    let propertyGetter = propertyGetterExpr.Compile()

    fun (value: obj) ->
        match propertyGetter.Invoke(value :?> 'targetType) with
        | Some v -> validator propertyName v
        | None -> Ok

let private packagePredicate (predicate: 'targetType -> bool) =
    fun (value: obj) -> predicate (value :?> 'targetType)

type ValidatorBuilder<'targetType>() =
    member __.Yield(_: unit) : PropertyValidatorConfig list = []

    member __.Run(config: PropertyValidatorConfig list) =
        let execValidation (record: 'targetType) : ValidationState =
            let results =
                config
                |> Seq.filter (fun p -> p.predicate (record :> obj))
                |> Seq.map (fun p -> p.validators |> Seq.map (fun v -> v record))
                |> Seq.concat
                |> Seq.map
                    (fun f ->
                        match f with
                        | Errors e -> e
                        | _ -> [])
                |> Seq.concat

            match (results |> Seq.isEmpty) with
            | true -> Ok
            | false -> Errors(results |> Seq.toList)

        execValidation

    [<CustomOperation("validate")>]
    member this.validate
        (
            config: PropertyValidatorConfig list,
            propertyGetter: Expression<Func<'targetType, 'propertyType>>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = (fun _ -> true) |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidator propertyGetter)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateSingleCaseUnion")>]
    member this.validateSingleCaseUnion
        (
            config: PropertyValidatorConfig list,
            propertyGetter: Expression<Func<'targetType, 'wrappedPropertyType>>,
            unwrapper: 'wrappedPropertyType -> 'propertyType,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = (fun _ -> true) |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidatorWithSingleCaseUnwrapper propertyGetter unwrapper)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateUnion")>]
    member this.validateUnion
        (
            config: PropertyValidatorConfig list,
            propertyGetter: Expression<Func<'targetType, 'wrappedPropertyType>>,
            unwrapper: 'wrappedPropertyType -> MatchResult<'propertyType>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = (fun _ -> true) |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidatorWithUnwrapper propertyGetter unwrapper)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateRequired")>]
    member this.validateRequired
        (
            config: PropertyValidatorConfig list,
            propertyGetter: Expression<Func<'targetType, 'propertyType option>>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = (fun _ -> true) |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidatorRequired propertyGetter)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateUnrequired")>]
    member this.validateUnrequired
        (
            config: PropertyValidatorConfig list,
            propertyGetter: Expression<Func<'targetType, 'propertyType option>>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = (fun _ -> true) |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidatorUnrequired propertyGetter)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateWhen")>]
    member this.validateWhen
        (
            config: PropertyValidatorConfig list,
            predicate: 'targetType -> bool,
            propertyGetter: Expression<Func<'targetType, 'propertyType>>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = predicate |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidator propertyGetter)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateRequiredWhen")>]
    member this.validateRequiredWhen
        (
            config: PropertyValidatorConfig list,
            predicate: 'targetType -> bool,
            propertyGetter: Expression<Func<'targetType, 'propertyType option>>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = predicate |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidatorRequired propertyGetter)
                              |> Seq.toList } ]
        |> Seq.toList

    [<CustomOperation("validateUnrequiredWhen")>]
    member this.validateUnrequiredWhen
        (
            config: PropertyValidatorConfig list,
            predicate: 'targetType -> bool,
            propertyGetter: Expression<Func<'targetType, 'propertyType option>>,
            validatorFunctions: (string -> 'propertyType -> ValidationState) list
        ) =
        config
        |> Seq.append [ { predicate = predicate |> packagePredicate
                          validators =
                              validatorFunctions
                              |> Seq.map (packageValidatorUnrequired propertyGetter)
                              |> Seq.toList } ]
        |> Seq.toList


// General validators
let isEqualTo comparisonValue =
    let comparator propertyName value =
        match value = comparisonValue with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must be equal to %O" comparisonValue
                    property = propertyName
                    errorCode = "isEqualTo" } ]
            )

    comparator

let isNotEqualTo comparisonValue =
    let comparator propertyName value =
        match not (value = comparisonValue) with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must not be equal to %O" comparisonValue
                    property = propertyName
                    errorCode = "isNotEqualTo" } ]
            )

    comparator

let isNotNull propertyName value =
    match isNull value with
    | true ->
        Errors(
            [ { message = "Must not be null"
                property = propertyName
                errorCode = "isNotNull" } ]
        )
    | false -> Ok

// Numeric validators
let isGreaterThanOrEqualTo minValue =
    let comparator propertyName value =
        match value >= minValue with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must have a minimum value of %O" minValue
                    property = propertyName
                    errorCode = "isGreaterThanOrEqualTo" } ]
            )

    comparator

let isGreaterThan minValue =
    let comparator propertyName value =
        match value > minValue with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must be greater than %O" minValue
                    property = propertyName
                    errorCode = "isGreaterThan" } ]
            )

    comparator

let isLessThanOrEqualTo maxValue =
    let comparator propertyName value =
        match value <= maxValue with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must have a maximum value of %O" maxValue
                    property = propertyName
                    errorCode = "isLessThanOrEqualTo" } ]
            )

    comparator

let isLessThan lessThanValue =
    let comparator propertyName value =
        match value < lessThanValue with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must be less than %O" lessThanValue
                    property = propertyName
                    errorCode = "isLessThan" } ]
            )

    comparator

// Collection validators
let isNotEmpty propertyName (value: seq<'item>) = // this also applies to strings
    if isNull value then
        Errors(
            [ { message = "Must not be null"
                property = propertyName
                errorCode = "isNotEmpty" } ]
        )
    elif (Seq.length value) = 0 then
        Errors(
            [ { message = "Must not be empty"
                property = propertyName
                errorCode = "isNotEmpty" } ]
        )
    else
        Ok

let isEmpty propertyName (value: seq<'item>) = // this also applies to strings
    if not (isNull value || (Seq.length value) = 0) then
        Errors(
            [ { message = "Must be empty"
                property = propertyName
                errorCode = "isEmpty" } ]
        )
    else
        Ok

let eachItemWith (validatorFunc: 'validatorTargetType -> ValidationState) =
    let buildIndexedPropertyName rootPropertyName index error =
        { error with
              property = sprintf "%s.[%d].%s" rootPropertyName index error.property }

    let comparator propertyName (items: seq<'validatorTargetType>) =
        let allResults =
            items
            |> Seq.mapi (fun index item -> (validatorFunc item), index)

        let failures =
            allResults
            |> Seq.map
                (fun r ->
                    match r with
                    | Ok, _ -> []
                    | Errors errors, index ->
                        errors
                        |> Seq.map (buildIndexedPropertyName propertyName index)
                        |> Seq.toList)
            |> Seq.concat
            |> Seq.toList

        match failures.Length with
        | 0 -> Ok
        | _ -> Errors(failures)

    comparator

let hasLengthOf length =
    let comparator propertyName (value: seq<'item>) =
        match (Seq.length value) = length with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must have a length of %O" length
                    property = propertyName
                    errorCode = "hasLengthOf" } ]
            )

    comparator

let hasMinLengthOf length =
    let comparator propertyName (value: seq<'item>) =
        match (Seq.length value) >= length with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must have a length no less than %O" length
                    property = propertyName
                    errorCode = "hasMinLengthOf" } ]
            )

    comparator

let hasMaxLengthOf length =
    let comparator propertyName (value: seq<'item>) =
        match (Seq.length value) <= length with
        | true -> Ok
        | false ->
            Errors(
                [ { message = sprintf "Must have a length no greater than %O" length
                    property = propertyName
                    errorCode = "hasMaxLengthOf" } ]
            )

    comparator

// String validators
let isNotEmptyOrWhitespace propertyName (value: string) =
    if isNull value then
        Errors(
            [ { message = "Must not be null"
                property = propertyName
                errorCode = "isNotEmptyOrWhitespace" } ]
        )
    elif String.IsNullOrEmpty(value) then
        Errors(
            [ { message = "Must not be empty"
                property = propertyName
                errorCode = "isNotEmptyOrWhitespace" } ]
        )
    elif String.IsNullOrWhiteSpace(value) then
        Errors(
            [ { message = "Must not be whitespace"
                property = propertyName
                errorCode = "isNotEmptyOrWhitespace" } ]
        )
    else
        Ok

// Function
let withFunction (validatorFunc: 'validatorTargetType -> ValidationState) =
    let comparator _ (value: 'validatorTargetType) = validatorFunc value
    comparator


// Sub-validators - very similar to functions but prefix the property name with the current property path
let private runChildValidator value propertyPath (validatorFunc: 'validatorTargetType -> ValidationState) =
    let buildPrefixedPropertyName error =
        { error with
              property = sprintf "%s.%s" propertyPath error.property }

    match (validatorFunc value) with
    | Ok -> Ok
    | Errors e ->
        Errors(
            e
            |> Seq.map buildPrefixedPropertyName
            |> Seq.toList
        )

let withValidatorWhen
    (predicate: 'validatorTargetType -> bool)
    (validatorFunc: 'validatorTargetType -> ValidationState)
    =
    let comparator propertyPath (value: 'validatorTargetType) =
        match predicate value with
        | true -> runChildValidator value propertyPath validatorFunc
        | false -> Ok

    comparator

let withValidator (validatorFunc: 'validatorTargetType -> ValidationState) =
    let comparator propertyPath (value: 'validatorTargetType) =
        runChildValidator value propertyPath validatorFunc

    comparator

let createValidatorFor<'targetType> () = ValidatorBuilder<'targetType>()

type IValidation<'T> =
    abstract Validate : Unit -> ValidationState

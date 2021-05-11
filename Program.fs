module TravelPlannerServer.App

open System
open Giraffe
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open TravelPlannerServer.Api
open Utf8Json.Resolvers
open Utf8Json.FSharp

// ---------------------------------
// Web app
// ---------------------------------

CompositeResolver.RegisterAndSetAsDefault(FSharpResolver.Instance, StandardResolver.Default)

let webApp =
    choose [ subRoute
                 "/api"
                 (choose [ GET
                           >=> choose [ route "/accounts"
                                        >=> publicResponseCaching 30 None
                                        >=> Account.GetAll
                                        route "/accounts/registration" >=> Account.IsPasswordMatch
                                        routef "/accounts/%s" Account.Get]
                           POST
                           >=> choose [ route "/accounts/registration" >=> Account.Register ]
                           DELETE
                           >=> choose [ routef "/accounts/%s" Account.Delete ]
                           PUT
                           >=> choose [ routef "/accounts/%s" Account.Put ]
                           setStatusCode 404 >=> text "Not Found" ]) ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex: Exception) (logger: ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")

    clearResponse
    >=> setStatusCode 500
    >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder: CorsPolicyBuilder) =
    builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
    |> ignore

let configureApp (app: IApplicationBuilder) =
    let env =
        app.ApplicationServices.GetService<IHostEnvironment>()

    (match env.IsDevelopment() with
     | true -> app.UseDeveloperExceptionPage()
     | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseCors(configureCors)
        .UseGiraffe(webApp)

let configureServices (services: IServiceCollection) =
    services.AddCors() |> ignore
    services.AddGiraffe() |> ignore

    services.AddSingleton<Json.ISerializer>(Utf8Json.Serializer(Utf8Json.Serializer.DefaultResolver))
    |> ignore

let configureLogging (builder: ILoggingBuilder) =
    let filter (l: LogLevel) = l.Equals LogLevel.Error

    builder.AddFilter(filter).AddConsole().AddDebug()
    |> ignore

[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .UseIISIntegration()
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()

    0

module NotifyRelay.Program

open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Azure.Functions.Worker

[<EntryPoint>]
let main _argv =
    let host =
        HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices(fun services ->
                services.AddApplicationInsightsTelemetryWorkerService() |> ignore
                services.ConfigureFunctionsApplicationInsights() |> ignore
                services.AddSingleton<Config>(fun _ -> Config.load ()) |> ignore
                services.AddSingleton<Storage>() |> ignore
                services.AddSingleton<Auth>() |> ignore
                services.AddSingleton<PushService>() |> ignore
                ())
            .Build()

    host.Run()
    0

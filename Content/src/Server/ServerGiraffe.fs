#if (remoting)
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
#endif
#if (deploy == "azure")
open FSharp.Azure.StorageTypeProvider
#endif
open FSharp.Control.Tasks.V2
open Giraffe
#if (deploy == "azure")
open global.Shared
#endif
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
#if (deploy != "azure")
open Shared
#endif
open System
open System.IO
#if (deploy == "azure")
open Thoth.Json.Net
#endif

#if (deploy == "azure")
let tryGetEnv = System.Environment.GetEnvironmentVariable >> function null | "" -> None | x -> Some x
let publicPath = tryGetEnv "public_path" |> Option.defaultValue "../Client/public" |> Path.GetFullPath
let runtimeAzure = tryGetEnv "STORAGE_CONNECTIONSTRING" |> Option.defaultValue "UseDevelopmentStorage=true"

/// A handle to an Azure storage account without a connection string. Schema is provided by the
/// azure-schema.json file. You can remove the blobSchema key/value and replace with a full Azure
/// connection string; schema will be inferred from the storage account contents directly.
type Azure = AzureTypeProvider<blobSchema="azure-schema.json">
let safeData = Azure.Containers.safedata
#else
let publicPath = Path.GetFullPath "../Client/public"
#endif
let port = 8085us

#if (deploy == "azure")
// Initialise the Azure storage account with a container and the counter state.
do
    safeData.AsCloudBlobContainer(runtimeAzure).CreateIfNotExistsAsync().Wait()
    safeData.``counter.txt``.AsCloudBlockBlob(runtimeAzure).UploadTextAsync(Encode.Auto.toString(4, { Value = 42 })).Wait()

let getInitCounter() = task {
    let! counter = safeData.``counter.txt``.ReadAsync(runtimeAzure)
    return Decode.Auto.unsafeFromString<Counter>(counter) }
#else
let getInitCounter() = task { return { Value = 42 } }
#endif
#if (remoting)

let counterApi = {
    initialCounter = getInitCounter >> Async.AwaitTask
}

let webApp =
    Remoting.createApi()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromValue counterApi
    |> Remoting.buildHttpHandler

#else
let webApp =
    route "/api/init" >=>
        fun next ctx ->
            task {
                let! counter = getInitCounter()
                return! Successful.OK counter next ctx
            }
#endif

let configureApp (app : IApplicationBuilder) =
    app.UseDefaultFiles()
       .UseStaticFiles()
       .UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    services.AddGiraffe() |> ignore
    #if (!remoting)
    services.AddSingleton<Giraffe.Serialization.Json.IJsonSerializer>(Thoth.Json.Giraffe.ThothSerializer()) |> ignore
    #endif
    #if (deploy == "azure")
    tryGetEnv "APPINSIGHTS_INSTRUMENTATIONKEY" |> Option.iter (services.AddApplicationInsightsTelemetry >> ignore)
    #endif

WebHost
    .CreateDefaultBuilder()
    .UseWebRoot(publicPath)
    .UseContentRoot(publicPath)
    .Configure(Action<IApplicationBuilder> configureApp)
    .ConfigureServices(configureServices)
    .UseUrls("http://0.0.0.0:" + port.ToString() + "/")
    .Build()
    .Run()
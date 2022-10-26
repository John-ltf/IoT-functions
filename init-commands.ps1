#Azure functions prerequisites
winget install --silent Microsoft.AzureFunctionsCoreTools
winget install --silent Microsoft.DotNet.SDK.6

#Azure functions project initialization
func init IoT-functions --dotnet
# func init --worker-runtime dotnetIsolated
Set-Location .\IoT-functions\
#dotnet add reference ..\IoTLib #not really needed, it used for sql client to cosmosdb but simmpler code was finally used

dotnet add package Microsoft.Azure.WebJobs.Extensions.SignalRService
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.SignalRService
Microsoft.Azure.Functions.Worker.Extensions.Http
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Microsoft.Azure.WebJobs.Extensions.EventHubs
dotnet add package Microsoft.Azure.Functions.Extensions
dotnet add package Newtonsoft.Json
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.CosmosDB
dotnet add package Microsoft.Azure.Cosmos

func new --name TelemetryToDB
#select EventHubTrigger
func new --language c# --name liveHistoryFeed
#select any value, there is no option for signalr
#or use func new --name LiveFeed --template EventHubTrigger
func settings add AzureSignalRConnectionString "<signalr-connection-string>"

#develop/deploy an to function app
func start --csharp
# func start --dotnet-isolated-debug

#publish to azure functions:
func azure functionapp publish <azure-functions> --dotnet-isolated

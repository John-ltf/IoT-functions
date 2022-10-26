using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.EventHubs;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using Microsoft.Azure.Cosmos;

namespace IoTTelemetryData
{
    public class IoTCosmoDB
    {
        private readonly ILogger _logger;

        public IoTCosmoDB(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<IoTCosmoDB>();
        }

       /* [Function("index")]
        public HttpResponseData GetWebPage([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString(File.ReadAllText("content/index.html"));
            response.Headers.Add("Content-Type", "text/html");
            return response;
        }*/

        [Function("DeviceHistory")]
        public async Task<HttpResponseData> DeviceHistory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "history/{device}/{timeStart}")] HttpRequestData req,
            string device,
            [CosmosDBInput(databaseName: "%CosmosDb%",
                       collectionName: "%Collection%",
                       ConnectionStringSetting = "CosmosConnection",
                       SqlQuery ="SELECT * FROM c WHERE c.deviceId = {device} AND c.telemetry.time >= {timeStart} ORDER BY c.telemetry.time DESC",
                       PartitionKey ="{device}")] IEnumerable<TelemetryData> telemetryData)
        {
            _logger.LogInformation($"Getting from Cosmos DB history for device {device})");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(telemetryData, "application/json");

            return response;
        }

        [Function("DeleteTelemetry")]
        public async Task<HttpResponseData> DeleteTelemetry(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delete/{device}/{telemetryId}")] HttpRequestData req,
            string device,
            string telemetryId)
        {
            _logger.LogInformation($"Deleting telemetry data with id {telemetryId})");

            ItemResponse<TelemetryData> orderResponse;
            using (CosmosClient client = new CosmosClient(
                Environment.GetEnvironmentVariable("CosmosDbConnectionString"),
                new CosmosClientOptions()
                {
                    ApplicationRegion = Regions.EastUS2,
                }))
            {
                var database = client.GetDatabase(Environment.GetEnvironmentVariable("CosmosDb"));
                var container = database.GetContainer(Environment.GetEnvironmentVariable("Collection"));
                orderResponse = await container.DeleteItemAsync<TelemetryData>(telemetryId, new PartitionKey(device));
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(orderResponse.Resource,"application/json");

            return response;
        }
    }

    public class NewConnection
    {
        public string ConnectionId { get; }
        public string Authentication { get; }

        public NewConnection(string connectionId, string auth)
        {
            ConnectionId = connectionId;
            Authentication = auth;
        }
    }

    public class NewMessage
    {
        public string ConnectionId { get; }
        public string Sender { get; }
        public string Text { get; }

        public NewMessage(SignalRInvocationContext invocationContext, string message)
        {
            Sender = string.IsNullOrEmpty(invocationContext.UserId) ? string.Empty : invocationContext.UserId;
            ConnectionId = invocationContext.ConnectionId;
            Text = message;
        }
    }


    public class telemetryToCosmosDB
    {
        private readonly ILogger _logger;
        public telemetryToCosmosDB(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<telemetryToCosmosDB>();
        }
        [Function("TelemetryToCosmosDB")]
        [CosmosDBOutput(
            databaseName: "%CosmosDb%",
            collectionName: "%Collection%",
            CreateIfNotExists = true,
            ConnectionStringSetting = "CosmosDbConnectionString")]
        public List<TelemetryData> TelemetryToCosmosDB(
            [EventHubTrigger("%EventHubName%", ConsumerGroup = "telemetrykeeper", Connection = "EventHubConnectionAppSetting")] string[] eventHubMessages,
            Dictionary<string, JsonElement>[] systemPropertiesArray)
        {
            List<TelemetryData> telemetryData = new List<TelemetryData>();
            for (int i = 0; i < eventHubMessages.Length; i++)
            {
                if(String.Equals(systemPropertiesArray[i]["iothub-message-source"].ToString(), "Telemetry") )
                {
                    _logger.LogInformation($"First Event Hubs triggered message: {systemPropertiesArray[i]["iothub-connection-device-id"]}");
                    _logger.LogInformation($"First Event Hubs triggered message: {eventHubMessages[i]}");
                    telemetryData.Add(
                        TelemetryDataBuilder.newTelemetryDataRaw(
                            systemPropertiesArray[i]["iothub-connection-device-id"].ToString(),
                            eventHubMessages[i].ToString()
                        )
                    );
                }
            }
            return telemetryData;
        }
    }

    public class IoTLiveFeed
    {
        private readonly ILogger _logger;
        public IoTLiveFeed(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<IoTLiveFeed>();
        }
        [Function("NegotiateLive")]
        public SignalRConnectionInfo NegotiateLive([HttpTrigger(AuthorizationLevel.Anonymous, Route = "negotiate/live")] HttpRequestData req,
            [SignalRConnectionInfoInput(HubName = "HubLiveData", UserId = "{query.userid}")] SignalRConnectionInfo signalRConnectionInfo)
        {
            _logger.LogInformation("Executing negotation.");
            return signalRConnectionInfo;
        }
        [Function("OnLiveConnected")]
        [SignalROutput(HubName = "HubLiveData")]
        public SignalRMessageAction OnLiveConnected([SignalRTrigger("HubLiveData", "connections", "connected")] SignalRInvocationContext invocationContext)
        {
            invocationContext.Headers.TryGetValue("Authorization", out var auth);
            _logger.LogInformation($"{invocationContext.ConnectionId} has connected");
            return new SignalRMessageAction("newConnection")
            {
                Arguments = new object[] { new NewConnection(invocationContext.ConnectionId, auth) },

            };
        }
        [Function("OnLiveDisconnected")]
        [SignalROutput(HubName = "HubLiveData")]
        public void OnLiveDisconnected([SignalRTrigger("HubLiveData", "connections", "disconnected")] SignalRInvocationContext invocationContext)
        {
            _logger.LogInformation($"{invocationContext.ConnectionId} has disconnected");
        }
        [Function("RegisterLiveToDevice")]
        [SignalROutput(HubName = "HubLiveData")]
        public SignalRGroupAction RegisterLiveToDevice([SignalRTrigger("HubLiveData", "messages", "RegisterLiveToDevice", "connectionId", "groupName")] SignalRInvocationContext invocationContext, string connectionId, string groupName)
        {
            _logger.LogInformation($"connection:{connectionId} added to group:{groupName}");
            return new SignalRGroupAction(SignalRGroupActionType.Add)
            {
                GroupName = groupName,
                ConnectionId = connectionId
            };
        }
        [Function("UnregisterFromLiveDevice")]
        [SignalROutput(HubName = "HubLiveData")]
        public SignalRGroupAction UnregisterFromLiveDevice([SignalRTrigger("HubLiveData", "messages", "UnregisterFromLiveDevice", "connectionId", "groupName")] SignalRInvocationContext invocationContext, string connectionId, string groupName)
        {
            return new SignalRGroupAction(SignalRGroupActionType.Remove)
            {
                GroupName = groupName,
                ConnectionId = connectionId
            };
        }
        [Function("LiveFeed")]
        [SignalROutput(HubName = "HubLiveData")]
        public List<SignalRMessageAction> LiveFeed(
            [EventHubTrigger("%EventHubName%", ConsumerGroup = "livefeeder", Connection = "EventHubConnectionAppSetting")] string[] eventHubMessages,
            Dictionary<string, JsonElement>[] systemPropertiesArray)
        {
            TelemetryData telemetryData;
            List<SignalRMessageAction> messages = new List<SignalRMessageAction>();
            SignalRMessageAction message;

            for (int i = 0; i < eventHubMessages.Length; i++)
            {
                if(String.Equals(systemPropertiesArray[i]["iothub-message-source"].ToString(), "Telemetry") )
                {
                    telemetryData = TelemetryDataBuilder.newTelemetryDataRaw(
                        systemPropertiesArray[i]["iothub-connection-device-id"].ToString(),
                        eventHubMessages[i].ToString()
                    );
                    _logger.LogInformation($"New live data for device: {telemetryData.deviceId} {telemetryData.id})");
                    message = new SignalRMessageAction("newMessage")
                    {
                        GroupName = telemetryData.deviceId,
                        Arguments = new object[] { telemetryData }
                    };
                    messages.Add(message);

                }
            }
            return messages;
        }
    }

    public class IoTHistoryFeed
    {
        private readonly ILogger _logger;
        public IoTHistoryFeed(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<IoTHistoryFeed>();
        }


        [Function("NegotiateHistory")]
        public SignalRConnectionInfo NegotiateHistory([HttpTrigger(AuthorizationLevel.Anonymous, Route = "negotiate/history")] HttpRequestData req,
            [SignalRConnectionInfoInput(HubName = "HubHistory", UserId = "{query.userid}")] SignalRConnectionInfo signalRConnectionInfo)
        {
            _logger.LogInformation("Executing negotation.");
            return signalRConnectionInfo;
        }
        [Function("OnHistoryConnected")]
        [SignalROutput(HubName = "HubHistory")]
        public SignalRMessageAction OnHistoryConnected([SignalRTrigger("HubHistory", "connections", "connected")] SignalRInvocationContext invocationContext)
        {
            invocationContext.Headers.TryGetValue("Authorization", out var auth);
            _logger.LogInformation($"{invocationContext.ConnectionId} has connected");
            return new SignalRMessageAction("newConnection")
            {
                Arguments = new object[] { new NewConnection(invocationContext.ConnectionId, auth) },

            };
        }
        [Function("OnDisconnected")]
        [SignalROutput(HubName = "HubHistory")]
        public void OnDisconnected([SignalRTrigger("HubHistory", "connections", "disconnected")] SignalRInvocationContext invocationContext)
        {
            _logger.LogInformation($"{invocationContext.ConnectionId} has disconnected");
        }
        [Function("RegisterHistoryToDevice")]
        [SignalROutput(HubName = "HubHistory")]
        public SignalRGroupAction RegisterHistoryToDevice([SignalRTrigger("HubHistory", "messages", "RegisterHistoryToDevice", "connectionId", "groupName")] SignalRInvocationContext invocationContext, string connectionId, string groupName)
        {
            _logger.LogInformation($"connection:{connectionId} added to group:{groupName}");
            return new SignalRGroupAction(SignalRGroupActionType.Add)
            {
                GroupName = groupName,
                ConnectionId = connectionId
            };
        }
        [Function("UnregisterFromHistoryDevice")]
        [SignalROutput(HubName = "HubHistory")]
        public SignalRGroupAction UnregisterFromHistoryDevice([SignalRTrigger("HubHistory", "messages", "UnregisterFromHistoryDevice", "connectionId", "groupName")] SignalRInvocationContext invocationContext, string connectionId, string groupName)
        {
            return new SignalRGroupAction(SignalRGroupActionType.Remove)
            {
                GroupName = groupName,
                ConnectionId = connectionId
            };
        }
        [Function("LiveHistoryFeed")]
        [SignalROutput(HubName = "HubHistory")]
        public List<SignalRMessageAction> LiveHistoryFeed(
            [CosmosDBTrigger("%CosmosDb%", "%CosmosCollIn%", ConnectionStringSetting = "CosmosConnection",
                LeaseCollectionName = "leases", CreateLeaseCollectionIfNotExists = true)] IReadOnlyList<TelemetryData> telemetryData,
            FunctionContext context
        )
        {
            var logger = context.GetLogger("New Feed from Cosmos DB");
            List<SignalRMessageAction> messages = new List<SignalRMessageAction>();
            SignalRMessageAction message;

            if (telemetryData != null && telemetryData.Any())
            {
                foreach (TelemetryData doc in telemetryData)
                {
                    _logger.LogInformation($"New History data for device: {doc.deviceId} {doc.id})");
                    message = new SignalRMessageAction("newMessage")
                    {
                        GroupName = doc.deviceId,
                        Arguments = new object[] { doc }
                    };
                    messages.Add(message);

                }
            }
            return messages;
        }
    }
}
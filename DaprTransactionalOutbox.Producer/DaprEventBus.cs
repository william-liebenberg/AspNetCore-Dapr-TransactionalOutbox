using System.Text;
using System.Text.Json;
using Dapr;
using Dapr.Client;

namespace DaprTransactionalOutbox.Producer;

public class DaprEventBus(ILogger<DaprEventBus> logger, DaprClient daprClient) : IEventBus
{
    // The name of the state store -- TODO: inject store name from configuration
    private const string StoreName = "statestore";

    public async Task Publish<T>(string key, T eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            string json = JsonSerializer.Serialize(eventData);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            var op = new StateTransactionRequest(
                key: key,
                value: jsonBytes,
                operationType: StateOperationType.Upsert,
                metadata: new Dictionary<string, string>
                {
                    { "ttlInSeconds", "60" },
                    // // By setting the metadata item "outbox.projection" to "true" and making sure the key values match (order.Id):
                    // // * The first operation is written to the state store and no message is written to the message broker.
                    // // * The second operation value is published to the configured pub/sub topic.
                    //      //   { "outbox.projection", "true" }, 
                    //      //   { "rawPayload", "true" },
                    { "datacontenttype", "application/json" }
                }
            );

            // Create the list of state operations
            var operations = new List<StateTransactionRequest> { op };

            // Execute the state transaction
            await daprClient.ExecuteStateTransactionAsync(StoreName, operations, cancellationToken: cancellationToken);
        }
        catch (DaprException e)
        {
            logger.LogError(e, "Error executing state transaction to {storeName} with key: {key}", StoreName, key);
            throw;
        }
    }
}
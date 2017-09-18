using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.AcceptanceTests.ScenarioDescriptors;

public class ConfigureEndpointAzureStorageQueueTransport : IConfigureEndpointTestExecution
{
    static string ConnectionString => EnvironmentHelper.GetEnvironmentVariable($"{nameof(AzureStorageQueueTransport)}.ConnectionString") ?? "UseDevelopmentStorage=true";

    public Task Configure(string endpointName, EndpointConfiguration configuration, RunSettings settings, PublisherMetadata publisherMetadata)
    {
        var connectionString = ConnectionString;

        var transportConfig = configuration.UseTransport<AzureStorageQueueTransport>();

        var transportRouting = transportConfig
            .Transactions(TransportTransactionMode.ReceiveOnly)
            .ConnectionString(connectionString)
            .Routing();

        transportConfig.SanitizeQueueNamesWith(BackwardsCompatibleQueueNameSanitizerForTests.Sanitize);

        foreach (var publisher in publisherMetadata.Publishers)
        {
            foreach (var @event in publisher.Events)
            {
                transportRouting.RegisterPublisher(@event, publisher.PublisherName);
            }
        }

        configuration.Pipeline.Register("test-independence-skip", typeof(TestIndependence.SkipBehavior), "Skips messages from other runs");
        transportConfig.SerializeMessageWrapperWith<TestIndependence.TestIdAppendingSerializationDefinition<NewtonsoftSerializer>>();

        return Task.FromResult(0);
    }

    public Task Cleanup()
    {
        return Task.FromResult(0);
    }
}
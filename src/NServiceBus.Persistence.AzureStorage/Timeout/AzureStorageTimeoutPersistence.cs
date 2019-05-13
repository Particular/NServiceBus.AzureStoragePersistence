namespace NServiceBus
{
    using System;
    using System.Threading.Tasks;
    using Persistence.AzureStorage;
    using Features;
    using Logging;
    using Microsoft.Azure.Cosmos.Table;
    using Microsoft.Azure.Storage.Blob;
    using Persistence.AzureStorage.Config;

    /// <summary></summary>
    public class AzureStorageTimeoutPersistence : Feature
    {
        internal AzureStorageTimeoutPersistence()
        {
            DependsOn<TimeoutManager>();
            Defaults(s =>
            {
#if NETFRAMEWORK
                var defaultConnectionString = System.Configuration.ConfigurationManager.AppSettings["NServiceBus/Persistence"];
                if (string.IsNullOrEmpty(defaultConnectionString) != true)
                {
                    logger.Warn(@"Connection string should be assigned using code API: var persistence = endpointConfiguration.UsePersistence<AzureStoragePersistence, StorageType.Timeouts>();\npersistence.ConnectionString(""connectionString"");");
                }
#endif
                s.SetDefault(WellKnownConfigurationKeys.TimeoutStorageCreateSchema, AzureTimeoutStorageDefaults.CreateSchema);
                s.SetDefault(WellKnownConfigurationKeys.TimeoutStorageTimeoutManagerDataTableName, AzureTimeoutStorageDefaults.TimeoutManagerDataTableName);
                s.SetDefault(WellKnownConfigurationKeys.TimeoutStorageTimeoutDataTableName, AzureTimeoutStorageDefaults.TimeoutDataTableName);
                s.SetDefault(WellKnownConfigurationKeys.TimeoutStorageCatchUpInterval, AzureTimeoutStorageDefaults.CatchUpInterval);
                s.SetDefault(WellKnownConfigurationKeys.TimeoutStoragePartitionKeyScope, AzureTimeoutStorageDefaults.PartitionKeyScope);
                s.SetDefault(WellKnownConfigurationKeys.TimeoutStorageTimeoutStateContainerName, AzureTimeoutStorageDefaults.TimeoutStateContainerName);
            });
        }

        /// <summary>
        /// See <see cref="Feature.Setup"/>
        /// </summary>
        protected override void Setup(FeatureConfigurationContext context)
        {
            var createIfNotExist = context.Settings.Get<bool>(WellKnownConfigurationKeys.TimeoutStorageCreateSchema);
            var timeoutDataTableName = context.Settings.Get<string>(WellKnownConfigurationKeys.TimeoutStorageTimeoutDataTableName);
            var timeoutManagerDataTableName = context.Settings.Get<string>(WellKnownConfigurationKeys.TimeoutStorageTimeoutManagerDataTableName);
            var connectionString = context.Settings.Get<string>(WellKnownConfigurationKeys.TimeoutStorageConnectionString);
            var blobConnectionString = context.Settings.GetOrDefault<string>(WellKnownConfigurationKeys.TimeoutStateStorageConnectionString);
            var catchUpInterval = context.Settings.Get<int>(WellKnownConfigurationKeys.TimeoutStorageCatchUpInterval);
            var partitionKeyScope = context.Settings.Get<string>(WellKnownConfigurationKeys.TimeoutStoragePartitionKeyScope);
            var endpointName = context.Settings.EndpointName();
            var hostDisplayName = context.Settings.GetOrDefault<string>("NServiceBus.HostInformation.DisplayName");
            var timeoutStateContainerName = context.Settings.GetOrDefault<string>(WellKnownConfigurationKeys.TimeoutStorageTimeoutStateContainerName);


            // When Azure Storage account is used for the entire persistence and not CosmosDB
            var timeoutBlobConnectionString = string.IsNullOrEmpty(blobConnectionString) ? connectionString : blobConnectionString;

            if (createIfNotExist)
            {
                var startupTask = new StartupTask(timeoutDataTableName, connectionString, timeoutBlobConnectionString, timeoutManagerDataTableName, timeoutStateContainerName);
                context.RegisterStartupTask(startupTask);
            }

            context.Container.ConfigureComponent(() =>
                new TimeoutPersister(connectionString, timeoutBlobConnectionString, timeoutDataTableName, timeoutManagerDataTableName, timeoutStateContainerName, catchUpInterval,
                                     partitionKeyScope, endpointName, hostDisplayName, () => DateTime.UtcNow),
                DependencyLifecycle.InstancePerCall);
        }

        class StartupTask : FeatureStartupTask
        {
            ILog log = LogManager.GetLogger<StartupTask>();
            string timeoutDataTableName;
            string connectionString;
            string blobConnectionString;
            string timeoutManagerDataTableName;
            string timeoutStateContainerName;

            public StartupTask(string timeoutDataTableName, string connectionString, string blobConnectionString, string timeoutManagerDataTableName, string timeoutStateContainerName)
            {
                this.timeoutDataTableName = timeoutDataTableName;
                this.connectionString = connectionString;
                this.blobConnectionString = blobConnectionString;
                this.timeoutManagerDataTableName = timeoutManagerDataTableName;
                this.timeoutStateContainerName = timeoutStateContainerName;
            }

            protected override async Task OnStart(IMessageSession session)
            {
                log.Info("Creating Timeout Table");

                var account = CloudStorageAccount.Parse(connectionString);
                var cloudTableClient = account.CreateCloudTableClient();
                var timeoutTable = cloudTableClient.GetTableReference(timeoutDataTableName);
                await timeoutTable.CreateIfNotExistsAsync().ConfigureAwait(false);

                var timeoutManagerTable = cloudTableClient.GetTableReference(timeoutManagerDataTableName);
                await timeoutManagerTable.CreateIfNotExistsAsync().ConfigureAwait(false);

                var blobAccount = Microsoft.Azure.Storage.CloudStorageAccount.Parse(blobConnectionString);
                var container = blobAccount.CreateCloudBlobClient()
                    .GetContainerReference(timeoutStateContainerName);
                await container.CreateIfNotExistsAsync().ConfigureAwait(false);
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }
        }

        static ILog logger => LogManager.GetLogger<AzureStorageTimeoutPersistence>();
    }
}
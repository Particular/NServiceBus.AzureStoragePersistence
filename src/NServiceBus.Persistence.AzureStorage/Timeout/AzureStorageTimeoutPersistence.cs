namespace NServiceBus
{
    using System;
    using System.Threading.Tasks;
    using Persistence.AzureStorage;
    using Features;
    using Logging;
    using Microsoft.WindowsAzure.Storage;
    using Persistence.AzureStorage.Config;

    /// <summary></summary>
    [ObsoleteEx(Message = "Azure Storage Queues supports timeouts natively and does not require timeout persistence.",
        TreatAsErrorFromVersion = "4",
        RemoveInVersion = "5")]
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
            var catchUpInterval = context.Settings.Get<int>(WellKnownConfigurationKeys.TimeoutStorageCatchUpInterval);
            var partitionKeyScope = context.Settings.Get<string>(WellKnownConfigurationKeys.TimeoutStoragePartitionKeyScope);
            var endpointName = context.Settings.EndpointName();
            var hostDisplayName = context.Settings.GetOrDefault<string>("NServiceBus.HostInformation.DisplayName");
            var timeoutStateContainerName = context.Settings.GetOrDefault<string>(WellKnownConfigurationKeys.TimeoutStorageTimeoutStateContainerName);

            if (createIfNotExist)
            {
                var startupTask = new StartupTask(timeoutDataTableName, connectionString, timeoutManagerDataTableName, timeoutStateContainerName);
                context.RegisterStartupTask(startupTask);
            }

            context.Container.ConfigureComponent(() =>
                new TimeoutPersister(connectionString, timeoutDataTableName, timeoutManagerDataTableName, timeoutStateContainerName, catchUpInterval,
                                     partitionKeyScope, endpointName, hostDisplayName, () => DateTime.UtcNow),
                DependencyLifecycle.InstancePerCall);
        }

        class StartupTask : FeatureStartupTask
        {
            ILog log = LogManager.GetLogger<StartupTask>();
            string timeoutDataTableName;
            string connectionString;
            string timeoutManagerDataTableName;
            string timeoutStateContainerName;

            public StartupTask(string timeoutDataTableName, string connectionString, string timeoutManagerDataTableName, string timeoutStateContainerName)
            {
                this.timeoutDataTableName = timeoutDataTableName;
                this.connectionString = connectionString;
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

                var container = account.CreateCloudBlobClient()
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
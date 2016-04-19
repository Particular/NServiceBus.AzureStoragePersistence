namespace NServiceBus
{
    using System;
    using System.Threading.Tasks;
    using Config;
    using Features;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.RetryPolicies;
    using Microsoft.WindowsAzure.Storage.Table;
    using NServiceBus.Logging;
    using Unicast.Subscriptions;

    public class AzureStorageSubscriptionPersistence : Feature
    {
        internal AzureStorageSubscriptionPersistence()
        {
            DependsOn<MessageDrivenSubscriptions>();
            Defaults(s =>
            {
                var configSection = s.GetConfigSection<AzureSubscriptionStorageConfig>() ?? new AzureSubscriptionStorageConfig();
                s.SetDefault("AzureSubscriptionStorage.ConnectionString", configSection.ConnectionString);
                s.SetDefault("AzureSubscriptionStorage.TableName", configSection.TableName);
                s.SetDefault("AzureSubscriptionStorage.CreateSchema", configSection.CreateSchema);
            });
        }

        /// <summary>
        /// See <see cref="Feature.Setup"/>
        /// </summary>
        protected override void Setup(FeatureConfigurationContext context)
        {
            var subscriptionTableName = context.Settings.Get<string>("AzureSubscriptionStorage.TableName");
            var connectionString = context.Settings.Get<string>("AzureSubscriptionStorage.ConnectionString");
            var createIfNotExist = context.Settings.Get<bool>("AzureSubscriptionStorage.CreateSchema");

            if (createIfNotExist)
            {
                var startupTask = new StartupTask(subscriptionTableName, connectionString);
                context.RegisterStartupTask(startupTask);
            }

            context.Container.ConfigureComponent(() => new AzureSubscriptionStorage(subscriptionTableName, connectionString), DependencyLifecycle.InstancePerCall);
        }

        class StartupTask : FeatureStartupTask
        {
            ILog log = LogManager.GetLogger<StartupTask>();
            string subscriptionTableName;
            string connectionString;

            public StartupTask(string subscriptionTableName, string connectionString)
            {
                this.subscriptionTableName = subscriptionTableName;
                this.connectionString = connectionString;
            }

            protected override async Task OnStart(IMessageSession session)
            {
                log.Info("Creating Subscription Table");
                var account = CloudStorageAccount.Parse(connectionString);
                var table = account.CreateCloudTableClient().GetTableReference(subscriptionTableName);
                await table.CreateIfNotExistsAsync()
                    .ConfigureAwait(false);
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }
        }
    }

}
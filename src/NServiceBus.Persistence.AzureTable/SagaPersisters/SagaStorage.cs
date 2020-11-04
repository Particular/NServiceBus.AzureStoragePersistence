﻿namespace NServiceBus.Persistence.AzureTable
{
    using Features;
    using Logging;
    using Microsoft.Extensions.DependencyInjection;
    using Sagas;

    class SagaStorage : Feature
    {
        internal SagaStorage()
        {
            Defaults(s =>
            {
                s.SetDefault(WellKnownConfigurationKeys.SagaStorageCreateSchema, AzureStorageSagaDefaults.CreateSchema);
                s.SetDefault(WellKnownConfigurationKeys.SagaStorageAssumeSecondaryIndicesExist, AzureStorageSagaDefaults.AssumeSecondaryIndicesExist);
                s.SetDefault(WellKnownConfigurationKeys.SagaStorageAssumeSecondaryKeyUsesANonEmptyRowKeySetToThePartitionKey, AzureStorageSagaDefaults.AssumeSecondaryKeyUsesANonEmptyRowKeySetToThePartitionKey);
                s.SetDefault(WellKnownConfigurationKeys.MigrationMode, AzureStorageSagaDefaults.MigrationModeEnabled);

                s.EnableFeatureByDefault<SynchronizedStorage>();
                s.SetDefault<ISagaIdGenerator>(new SagaIdGenerator());
            });
            DependsOn<SynchronizedStorage>();
            DependsOn<Features.Sagas>();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            var updateSchema = context.Settings.Get<bool>(WellKnownConfigurationKeys.SagaStorageCreateSchema);
            var migrationModeEnabled = context.Settings.Get<bool>(WellKnownConfigurationKeys.MigrationMode);
            var assumeSecondaryIndicesExist = context.Settings.Get<bool>(WellKnownConfigurationKeys.SagaStorageAssumeSecondaryIndicesExist);
            var assumeSecondaryKeyUsesANonEmptyRowKeySetToThePartitionKey = context.Settings.Get<bool>(WellKnownConfigurationKeys.SagaStorageAssumeSecondaryKeyUsesANonEmptyRowKeySetToThePartitionKey);

            if (migrationModeEnabled)
            {
                var addition = assumeSecondaryKeyUsesANonEmptyRowKeySetToThePartitionKey? ", assuming the secondary index uses RowKey = PartitionKey," : string.Empty;
                Logger.Info($"The version of {nameof(AzureTablePersistence)} uses the migration mode and will fallback to lookup correlated sages based on the secondary index{addition} if necessary.");
            }

            if (assumeSecondaryIndicesExist == false)
            {
                Logger.Warn($"The version of {nameof(AzureTablePersistence)} used is not configured to optimize sagas creation and might fall back to full table scanning to retrieve correlated sagas. It is suggested to migrate saga instances. Consult the upgrade guides for recommendations.");
            }

            context.Services.AddSingleton<ISagaPersister>(provider => new AzureSagaPersister(provider.GetRequiredService<IProvideCloudTableClient>(), updateSchema, migrationModeEnabled, assumeSecondaryIndicesExist, assumeSecondaryKeyUsesANonEmptyRowKeySetToThePartitionKey));
        }

        static readonly ILog Logger = LogManager.GetLogger<SagaStorage>();
    }
}
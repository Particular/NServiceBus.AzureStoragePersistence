﻿namespace NServiceBus
{
    using Config;
    using Features;
    using SagaPersisters.Azure;

    public class AzureStorageSagaPersistence : Feature
    {
        internal AzureStorageSagaPersistence()
        {
            DependsOn<Features.Sagas>();
            Defaults(s =>
            {
                var configSection = s.GetConfigSection<AzureSagaPersisterConfig>() ?? new AzureSagaPersisterConfig();
                s.SetDefault("AzureSagaStorage.ConnectionString", configSection.ConnectionString);
                s.SetDefault("AzureSagaStorage.CreateSchema", configSection.CreateSchema);
            });
        }

        /// <summary>
        /// See <see cref="Feature.Setup"/>
        /// </summary>
        protected override void Setup(FeatureConfigurationContext context)
        {
            var connectionstring = context.Settings.Get<string>("AzureSagaStorage.ConnectionString");
            var updateSchema = context.Settings.Get<bool>("AzureSagaStorage.CreateSchema");
            
            context.Container.ConfigureComponent(() => new AzureSagaPersister(connectionstring, updateSchema), DependencyLifecycle.InstancePerCall);
        }
    }
}
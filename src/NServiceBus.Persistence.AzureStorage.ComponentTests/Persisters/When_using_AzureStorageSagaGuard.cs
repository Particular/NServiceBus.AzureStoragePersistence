﻿namespace NServiceBus.Persistence.AzureStorage.ComponentTests.Persisters
{
    using System;
    using NUnit.Framework;

    public class When_using_AzureStorageSagaGuard
    {
        [TestCase("")]
        [TestCase(null)]
        [ExpectedException(typeof(ArgumentException))]
        public void Should_not_allow_invalid_connection_string(string connectionString)
        {
            AzureStorageSagaGuard.CheckConnectionString(connectionString);
        }

        [Test]
        public void Should_validate_all_default_settings_for_a_new_config()
        {
            Assert.AreEqual(AzureStorageSagaDefaults.CreateSchema, AzureStorageSagaDefaults.CreateSchema);
        }
    }
}
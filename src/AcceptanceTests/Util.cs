﻿namespace Testing
{
    using System;

    public static class Utillities
    {
        public static string GetEnvConfiguredConnectionStringForPersistence()
        {
            var environmentVartiableName = "AzureStoragePersistence_CosmosDB_ConnectionString";
            var connectionString = GetEnvironmentVariable(environmentVartiableName);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"Oh no! We couldn't find an environment variable '{environmentVartiableName}' with Azure CosmosDB (Table API) connection string.");
            }

            return connectionString;

        }

        public static string GetEnvConfiguredConnectionStringForBlobStorage()
        {
            var environmentVartiableName = "AzureStoragePersistence_ConnectionString";
            var connectionString = GetEnvironmentVariable(environmentVartiableName);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"Oh no! We couldn't find an environment variable '{environmentVartiableName}' with Azure Storage connection string.");
            }

            return connectionString;
        }

        public static string GetEnvConfiguredConnectionStringForTransport()
        {
            var environmentVartiableName = "AzureStorageQueueTransport_ConnectionString";
            var connectionString = GetEnvironmentVariable(environmentVartiableName);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"Oh no! We couldn't find an environment variable '{environmentVartiableName}' with Azure Storage connection string.");
            }

            return connectionString;
        }

        static string GetEnvironmentVariable(string variable)
        {
            var candidate = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
            return string.IsNullOrWhiteSpace(candidate) ? Environment.GetEnvironmentVariable(variable) : candidate;
        }

    }
}
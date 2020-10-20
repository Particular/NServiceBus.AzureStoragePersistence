﻿namespace NServiceBus.Persistence.AzureStorage
{
    using System;

    /// <summary>
    ///
    /// </summary>
    public static class SynchronizedStorageSessionExtensions
    {
        /// <summary>
        /// Retrieves the shared <see cref="IAzureStorageStorageSession"/> from the <see cref="SynchronizedStorageSession"/>.
        /// </summary>
        public static IAzureStorageStorageSession AzureStoragePersistenceSession(this SynchronizedStorageSession session)
        {
            Guard.AgainstNull(nameof(session), session);

            if (session is IAzureStorageStorageSession workWith)
            {
                return workWith;
            }

            throw new Exception($"Cannot access the synchronized storage session. Ensure that 'EndpointConfiguration.UsePersistence<{nameof(AzureStoragePersistence)}>()' has been called.");
        }
    }
}
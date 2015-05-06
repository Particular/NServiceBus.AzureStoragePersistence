﻿namespace NServiceBus.Unicast.Subscriptions
{
    using System.Linq;
    using Microsoft.WindowsAzure.Storage.Table;
    using Microsoft.WindowsAzure.Storage.Table.DataServices;

    /// <summary>
    /// 
    /// </summary>
    public class SubscriptionServiceContext : TableServiceContext
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        public SubscriptionServiceContext(CloudTableClient client)
            : base(client)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public static void Init(CloudTableClient client)
        {
            var table = client.GetTableReference(SubscriptionTableName);
            if(CreateIfNotExist) table.CreateIfNotExists();
        }

        /// <summary>
        /// Subscriptions table name.
        /// </summary>
        public static string SubscriptionTableName = "Subscription";

        /// <summary>
        /// Should subscription table be created or not.
        /// </summary>
        public static bool CreateIfNotExist = true;

        /// <summary>
        /// 
        /// </summary>
        public IQueryable<Subscription> Subscriptions
        {
            get
            {
                return CreateQuery<Subscription>(SubscriptionTableName);
            }
        }

        
    }
}
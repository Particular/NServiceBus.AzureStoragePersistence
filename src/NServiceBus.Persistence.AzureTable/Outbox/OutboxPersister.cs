﻿namespace NServiceBus.Persistence.AzureTable
{
    using System.Threading;
    using System.Threading.Tasks;
    using Extensibility;
    using Microsoft.Azure.Cosmos.Table;
    using Outbox;

    class OutboxPersister : IOutboxStorage
    {
        public OutboxPersister(TableHolderResolver tableHolderResolver)
        {
            this.tableHolderResolver = tableHolderResolver;
        }

        public Task<OutboxTransaction> BeginTransaction(ContextBag context, CancellationToken cancellationToken = default)
        {
            var azureStorageOutboxTransaction = new AzureStorageOutboxTransaction(tableHolderResolver, context);

            if (context.TryGet<TableEntityPartitionKey>(out var partitionKey))
            {
                azureStorageOutboxTransaction.PartitionKey = partitionKey;
            }
            return Task.FromResult((OutboxTransaction)azureStorageOutboxTransaction);
        }

        public async Task<OutboxMessage> Get(string messageId, ContextBag context, CancellationToken cancellationToken = default)
        {
            var setAsDispatchedHolder = new SetAsDispatchedHolder
            {
                TableHolder = tableHolderResolver.ResolveAndSetIfAvailable(context)
            };
            context.Set(setAsDispatchedHolder);

            if (!context.TryGet<TableEntityPartitionKey>(out var partitionKey))
            {
                // we return null here to enable outbox work at logical stage
                return null;
            }

            var outboxRecord = await setAsDispatchedHolder.TableHolder.Table
                .ReadOutboxRecord(messageId, partitionKey, context, cancellationToken)
                .ConfigureAwait(false);

            setAsDispatchedHolder.Record = outboxRecord;
            setAsDispatchedHolder.PartitionKey = partitionKey;

            return outboxRecord != null ? new OutboxMessage(outboxRecord.Id, outboxRecord.Operations) : null;
        }

        public Task Store(OutboxMessage message, OutboxTransaction transaction, ContextBag context, CancellationToken cancellationToken = default)
        {
            var azureStorageOutboxTransaction = (AzureStorageOutboxTransaction)transaction;

            if (azureStorageOutboxTransaction == null || azureStorageOutboxTransaction.SuppressStoreAndCommit || azureStorageOutboxTransaction.PartitionKey == null)
            {
                return Task.CompletedTask;
            }

            var setAsDispatchedHolder = context.Get<SetAsDispatchedHolder>();

            var outboxRecord = new OutboxRecord
            {
                Id = message.MessageId,
                Operations = message.TransportOperations,
                PartitionKey = setAsDispatchedHolder.PartitionKey.PartitionKey
            };

            setAsDispatchedHolder.Record = outboxRecord;

            azureStorageOutboxTransaction.StorageSession.Add(new OutboxStore(setAsDispatchedHolder.PartitionKey, outboxRecord, setAsDispatchedHolder.TableHolder.Table));

            return Task.CompletedTask;
        }

        public Task SetAsDispatched(string messageId, ContextBag context, CancellationToken cancellationToken = default)
        {
            var setAsDispatchedHolder = context.Get<SetAsDispatchedHolder>();

            var tableHolder = setAsDispatchedHolder.TableHolder;
            var record = setAsDispatchedHolder.Record;

            record.SetAsDispatched();

            var operation = new OutboxDelete(setAsDispatchedHolder.PartitionKey, record, tableHolder.Table);
            var transactionalBatch = new TableBatchOperation();
            return transactionalBatch.ExecuteOperationAsync(operation, cancellationToken: cancellationToken);
        }

        readonly TableHolderResolver tableHolderResolver;
    }
}
﻿namespace NServiceBus.Persistence.AzureStorage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using Extensibility;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Blob;
    using Microsoft.WindowsAzure.Storage.RetryPolicies;
    using Microsoft.WindowsAzure.Storage.Table;
    using Timeout.Core;
    using Timeout.TimeoutLogic;

    class TimeoutPersister : IPersistTimeouts, IQueryTimeouts
    {
        public TimeoutPersister(string timeoutConnectionString, string timeoutDataTableName, string timeoutStateContainerName, string partitionKeyScope, string endpointName)
        {
            this.timeoutDataTableName = timeoutDataTableName;
            this.timeoutStateContainerName = timeoutStateContainerName;
            this.partitionKeyScope = partitionKeyScope;
            this.endpointName = endpointName;

            var account = CloudStorageAccount.Parse(timeoutConnectionString);
            client = account.CreateCloudTableClient();
            client.DefaultRequestOptions = new TableRequestOptions
            {
                RetryPolicy = new ExponentialRetry()
            };

            cloudBlobclient = account.CreateCloudBlobClient();
        }

        public async Task Add(TimeoutData timeout, ContextBag context)
        {
            var timeoutDataTable = client.GetTableReference(timeoutDataTableName);

            string identifier;
            timeout.Headers.TryGetValue(Headers.MessageId, out identifier);
            if (string.IsNullOrEmpty(identifier))
            {
                identifier = Guid.NewGuid().ToString();
            }
            timeout.Id = identifier;

            var timeoutDataEntity = await GetTimeoutData(timeoutDataTable, identifier, string.Empty).ConfigureAwait(false);
            if (timeoutDataEntity != null) return;

            var headers = Serialize(timeout.Headers);

            var saveActions = new List<Task>
            {
                SaveCurrentTimeoutState(timeout.State, identifier),
                SaveTimeoutEntry(timeout, timeoutDataTable, identifier, headers),
                SaveSagaEntry(timeout, timeoutDataTable, identifier, headers)
            };

            await Task.WhenAll(saveActions).ConfigureAwait(false);
            await SaveMainEntry(timeout, identifier, headers, timeoutDataTable).ConfigureAwait(false);
        }

        public async Task<TimeoutData> Peek(string timeoutId, ContextBag context)
        {
            var timeoutDataTable = client.GetTableReference(timeoutDataTableName);

            var timeoutDataEntity = await GetTimeoutData(timeoutDataTable, timeoutId, string.Empty).ConfigureAwait(false);
            if (timeoutDataEntity == null)
            {
                return null;
            }

            var timeoutData = new TimeoutData
            {
                Destination = timeoutDataEntity.Destination, //TODO: check if the change here is causing issues
                SagaId = timeoutDataEntity.SagaId,
                State = await Download(timeoutDataEntity.StateAddress).ConfigureAwait(false),
                Time = timeoutDataEntity.Time,
                Id = timeoutDataEntity.RowKey,
                OwningTimeoutManager = timeoutDataEntity.OwningTimeoutManager,
                Headers = Deserialize(timeoutDataEntity.Headers)
            };
            return timeoutData;
        }

        public async Task<bool> TryRemove(string timeoutId, ContextBag context)
        {
            var timeoutDataTable = client.GetTableReference(timeoutDataTableName);

            var timeoutDataEntity = await GetTimeoutData(timeoutDataTable, timeoutId, string.Empty).ConfigureAwait(false);
            if (timeoutDataEntity == null)
            {
                return false;
            }

            try
            {
                await DeleteSagaEntity(timeoutId, timeoutDataTable, timeoutDataEntity).ConfigureAwait(false);
                await DeleteTimeEntity(timeoutDataTable, timeoutDataEntity.Time.ToString(partitionKeyScope), timeoutId).ConfigureAwait(false);
                await DeleteState(timeoutDataEntity.StateAddress).ConfigureAwait(false);
                await DeleteMainEntity(timeoutDataEntity, timeoutDataTable).ConfigureAwait(false);
            }
            catch (StorageException e)
            {
                if (e.RequestInformation.HttpStatusCode != (int) HttpStatusCode.NotFound)
                {
                    throw;
                }

                return false;
            }

            return true;
        }

        public async Task RemoveTimeoutBy(Guid sagaId, ContextBag context)
        {
            var timeoutDataTable = client.GetTableReference(timeoutDataTableName);

            var query = new TableQuery<TimeoutDataEntity>()
                .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, sagaId.ToString()));

            var results = await timeoutDataTable.ExecuteQueryAsync(query, take: 1000).ConfigureAwait(false);

            foreach (var timeoutDataEntityBySaga in results)
            {
                await DeleteState(timeoutDataEntityBySaga.StateAddress).ConfigureAwait(false);
                await DeleteTimeEntity(timeoutDataTable, timeoutDataEntityBySaga.Time.ToString(partitionKeyScope), timeoutDataEntityBySaga.RowKey).ConfigureAwait(false);
                await DeleteMainEntity(timeoutDataTable, timeoutDataEntityBySaga.RowKey, string.Empty).ConfigureAwait(false);
                await DeleteSagaEntity(timeoutDataTable, timeoutDataEntityBySaga).ConfigureAwait(false);
            }
        }

        public async Task<TimeoutsChunk> GetNextChunk(DateTime startSlice)
        {
            var tailTimeouts = await GetTailQueryResults().ConfigureAwait(false);

            if (tailTimeouts != null)
            {
                return tailTimeouts;
            }

            return await QueryCurrentTimeouts().ConfigureAwait(false);
        }

        async Task<TimeoutsChunk> GetTailQueryResults()
        {
            if (tailQuery == null)
            {
                tailQuery = QueryTimeouts(QueryComparisons.LessThan);
            }

            try
            {
                TimeoutsChunk tailTimeouts = null;

                if (tailQuery.IsCompleted || tailQuery.IsFaulted)
                {
                    tailTimeouts = await tailQuery.ConfigureAwait(false);

                    var queryDelay = tailTimeouts.DueTimeouts.Length > 0
                        ? TimeSpan.Zero
                        : TimeSpan.FromMinutes(10);

                    tailQuery = QueryTailTimeouts(queryDelay);
                }

                return tailTimeouts;
            }
            catch
            {
                // reissue the query on exception, the previous instance is already faulted
                tailQuery = QueryTailTimeouts(TimeSpan.FromMinutes(10));

                throw;
            }
        }

        async Task<TimeoutsChunk> QueryTailTimeouts(TimeSpan timeout)
        {
            await Task.Delay(timeout).ConfigureAwait(false);

            return await QueryTimeouts(QueryComparisons.LessThan).ConfigureAwait(false);
        }

        Task<TimeoutsChunk> QueryCurrentTimeouts()
        {
            return QueryTimeouts(QueryComparisons.Equal);
        }

        Task<TimeoutsChunk> QueryTimeouts(string partitionKeyComparison)
        {
            var now = CurrentDateTimeProvider();
            var timeoutDataTable = client.GetTableReference(timeoutDataTableName);

            var query = new TableQuery<TimeoutDataEntity>()
                .Where(
                    TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", partitionKeyComparison, now.ToString(partitionKeyScope)),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("OwningTimeoutManager", QueryComparisons.Equal, endpointName))
                );

            return CalculateNextTimeoutChunk((q, t) => timeoutDataTable.ExecuteQuerySegmentedAsync(q, t, new CancellationToken()), query, now);
        }

        internal Func<DateTime> CurrentDateTimeProvider { get; set; } = () => DateTime.UtcNow;

        internal static async Task<TimeoutsChunk> CalculateNextTimeoutChunk(Func<TableQuery<TimeoutDataEntity>, TableContinuationToken, Task<TableQuerySegment<TimeoutDataEntity>>> executeQuerySegmentedAsync, TableQuery<TimeoutDataEntity> query, DateTime now)
        {
            var timeouts = new List<TimeoutDataEntity>();
            TableContinuationToken token = null;
            var nextRequestTime = DateTime.MaxValue;

            do
            {
                var seg = await executeQuerySegmentedAsync(query, token).ConfigureAwait(false);

                token = seg.ContinuationToken;

                var futureTimeouts = seg.Results.Where(c => c.Time > now).ToArray();

                if (futureTimeouts.Any())
                {
                    var futureMinimalTime = futureTimeouts.Min(c => c.Time);

                    if (futureMinimalTime < nextRequestTime)
                    {
                        nextRequestTime = futureMinimalTime;
                    }
                }

                timeouts.AddRange(seg.Results.Where(c => c.Time <= now));
            } while (token != null && timeouts.Count < TimeoutChunkBatchSize);

            var dueTimeouts = timeouts.Where(c => !string.IsNullOrEmpty(c.RowKey))
                .Select(c => new TimeoutsChunk.Timeout(c.RowKey, c.Time))
                .Distinct(new TimoutChunkComparer())
                .ToArray();

            nextRequestTime = timeouts.Count < TimeoutChunkBatchSize && nextRequestTime != DateTime.MaxValue ? nextRequestTime : now.Add(DefaultNextQueryDelay);

            if (nextRequestTime > now + MaximumDelay)
            {
                nextRequestTime = now + MaximumDelay;
            }

            return new TimeoutsChunk(dueTimeouts, nextRequestTime);
        }

        async Task DeleteMainEntity(CloudTable timeoutDataTable, string partitionKey, string rowKey)
        {
            var timeoutDataEntity = await GetTimeoutData(timeoutDataTable, partitionKey, rowKey).ConfigureAwait(false);

            if (timeoutDataEntity != null)
            {
                await DeleteMainEntity(timeoutDataEntity, timeoutDataTable).ConfigureAwait(false);
            }
        }

        Task DeleteMainEntity(TimeoutDataEntity timeoutDataEntity, CloudTable timeoutDataTable)
        {
            var deleteOperation = TableOperation.Delete(timeoutDataEntity);
            return timeoutDataTable.ExecuteAsync(deleteOperation);
        }

        async Task DeleteTimeEntity(CloudTable timeoutDataTable, string partitionKey, string rowKey)
        {
            var timeoutDataEntityByTime = await GetTimeoutData(timeoutDataTable, partitionKey, rowKey).ConfigureAwait(false);
            if (timeoutDataEntityByTime != null)
            {
                var deleteByTimeOperation = TableOperation.Delete(timeoutDataEntityByTime);
                await timeoutDataTable.ExecuteAsync(deleteByTimeOperation).ConfigureAwait(false);
            }
        }

        async Task DeleteSagaEntity(string timeoutId, CloudTable timeoutDataTable, TimeoutDataEntity timeoutDataEntity)
        {
            var timeoutDataEntityBySaga = await GetTimeoutData(timeoutDataTable, timeoutDataEntity.SagaId.ToString(), timeoutId).ConfigureAwait(false);
            if (timeoutDataEntityBySaga != null)
            {
                await DeleteSagaEntity(timeoutDataTable, timeoutDataEntityBySaga).ConfigureAwait(false);
            }
        }

        Task DeleteSagaEntity(CloudTable timeoutDataTable, TimeoutDataEntity sagaEntity)
        {
            var deleteSagaOperation = TableOperation.Delete(sagaEntity);
            return timeoutDataTable.ExecuteAsync(deleteSagaOperation);
        }

        Task SaveMainEntry(TimeoutData timeout, string identifier, string headers, CloudTable timeoutDataTable)
        {
            var timeoutDataObject = new TimeoutDataEntity(identifier, string.Empty)
            {
                Destination = timeout.Destination,
                SagaId = timeout.SagaId,
                StateAddress = identifier,
                Time = timeout.Time,
                OwningTimeoutManager = timeout.OwningTimeoutManager,
                Headers = headers
            };
            var addEntityOperation = TableOperation.Insert(timeoutDataObject);
            return timeoutDataTable.ExecuteAsync(addEntityOperation);
        }

        async Task SaveSagaEntry(TimeoutData timeout, CloudTable timeoutDataTable, string identifier, string headers)
        {
            var timeoutDataEntity = await GetTimeoutData(timeoutDataTable, timeout.SagaId.ToString(), identifier).ConfigureAwait(false);
            if (timeout.SagaId != default(Guid) && timeoutDataEntity == null)
            {
                var timeoutData = new TimeoutDataEntity(timeout.SagaId.ToString(), identifier)
                {
                    Destination = timeout.Destination,
                    SagaId = timeout.SagaId,
                    StateAddress = identifier,
                    Time = timeout.Time,
                    OwningTimeoutManager = timeout.OwningTimeoutManager,
                    Headers = headers
                };

                var addOperation = TableOperation.Insert(timeoutData);
                await timeoutDataTable.ExecuteAsync(addOperation).ConfigureAwait(false);
            }
        }

        async Task SaveTimeoutEntry(TimeoutData timeout, CloudTable timeoutDataTable, string identifier, string headers)
        {
            var timeoutDataEntity = await GetTimeoutData(timeoutDataTable, timeout.Time.ToString(partitionKeyScope), identifier).ConfigureAwait(false);

            if (timeoutDataEntity == null)
            {
                var timeoutData = new TimeoutDataEntity(timeout.Time.ToString(partitionKeyScope), identifier)
                {
                    Destination = timeout.Destination,
                    SagaId = timeout.SagaId,
                    StateAddress = identifier,
                    Time = timeout.Time,
                    OwningTimeoutManager = timeout.OwningTimeoutManager,
                    Headers = headers
                };
                var addOperation = TableOperation.Insert(timeoutData);
                await timeoutDataTable.ExecuteAsync(addOperation).ConfigureAwait(false);
            }
        }

        async Task<TimeoutDataEntity> GetTimeoutData(CloudTable timeoutDataTable, string partitionKey, string rowKey)
        {
            var retrieveOperation = TableOperation.Retrieve<TimeoutDataEntity>(partitionKey, rowKey);
            return (await timeoutDataTable.ExecuteAsync(retrieveOperation).ConfigureAwait(false)).Result as TimeoutDataEntity;
        }

        async Task SaveCurrentTimeoutState(byte[] state, string stateAddress)
        {
            var container = cloudBlobclient.GetContainerReference(timeoutStateContainerName);
            var blob = container.GetBlockBlobReference(stateAddress);
            using (var stream = new MemoryStream(state))
            {
                await blob.UploadFromStreamAsync(stream).ConfigureAwait(false);
            }
        }

        async Task<byte[]> Download(string stateAddress)
        {
            var container = cloudBlobclient.GetContainerReference(timeoutStateContainerName);

            var blob = container.GetBlockBlobReference(stateAddress);
            using (var stream = new MemoryStream())
            {
                await blob.DownloadToStreamAsync(stream).ConfigureAwait(false);
                stream.Position = 0;

                var buffer = new byte[16 * 1024];
                using (var ms = new MemoryStream())
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await ms.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    }
                    return ms.ToArray();
                }
            }
        }

        string Serialize(Dictionary<string, string> headers)
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(headers);
        }

        Dictionary<string, string> Deserialize(string state)
        {
            if (string.IsNullOrEmpty(state))
            {
                return new Dictionary<string, string>();
            }

            var serializer = new JavaScriptSerializer();
            return serializer.Deserialize<Dictionary<string, string>>(state);
        }

        Task DeleteState(string stateAddress)
        {
            var container = cloudBlobclient.GetContainerReference(timeoutStateContainerName);
            var blob = container.GetBlockBlobReference(stateAddress);
            return blob.DeleteIfExistsAsync();
        }


        string timeoutDataTableName;
        string timeoutStateContainerName;
        string partitionKeyScope;
        string endpointName;
        Task<TimeoutsChunk> tailQuery = null;
        CloudTableClient client;
        CloudBlobClient cloudBlobclient;
        internal static readonly TimeSpan DefaultNextQueryDelay = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(10);
        internal const int TimeoutChunkBatchSize = 1000;
    }
}
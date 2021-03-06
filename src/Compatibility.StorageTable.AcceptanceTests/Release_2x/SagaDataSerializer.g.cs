namespace NServiceBus.Persistence.AzureTable.Release_2x
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using global::Newtonsoft.Json;
    using global::Newtonsoft.Json.Serialization;

    /// <summary>
    /// This is a copy of the saga persister code 2.4.1
    /// </summary>
    class SagaDataSerializer
    {
        public static byte[] SerializeSagaData<TSagaData>(TSagaData sagaData) where TSagaData : IContainSagaData
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var zipped = new GZipStream(memoryStream, CompressionMode.Compress))
                using (var writer = new StreamWriter(zipped))
                {
                    serializer.Serialize(writer, sagaData);
                    writer.Flush();
                }

                return memoryStream.ToArray();
            }
        }

        public static IContainSagaData DeserializeSagaData(Type sagaType, byte[] value)
        {
            using (var memoryStream = new MemoryStream(value))
            using (var zipped = new GZipStream(memoryStream, CompressionMode.Decompress))
            using (var reader = new StreamReader(zipped))
            {
                return (IContainSagaData) serializer.Deserialize(reader, sagaType);
            }
        }

        static JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new SagaOnlyPropertiesDataContractResolver()
        };

        class SagaOnlyPropertiesDataContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var properties = new HashSet<string>(SagaPersisterUsingSecondaryIndexes.SelectPropertiesToPersist(type).Select(pi => pi.Name));
                return base.CreateProperties(type, memberSerialization)
                    .Where(p => properties.Contains(p.PropertyName))
                    .ToArray();
            }
        }
    }
}
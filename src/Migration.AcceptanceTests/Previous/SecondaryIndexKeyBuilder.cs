namespace NServiceBus.Persistence.AzureStorage.Previous
{
    using System;
    using System.IO;
    using global::Newtonsoft.Json;
    using Sagas;

    static class SecondaryIndexKeyBuilder
    {
        public static PartitionRowKeyTuple BuildTableKey(Type sagaType, SagaCorrelationProperty correlationProperty)
        {
            var sagaDataTypeName = sagaType.FullName;
            return new PartitionRowKeyTuple($"Index_{sagaDataTypeName}_{correlationProperty.Name}_{Serialize(correlationProperty.Value)}", string.Empty);
        }

        static string Serialize(object propertyValue)
        {
            using (var writer = new StringWriter())
            {
                jsonSerializer.Serialize(writer, propertyValue);
                writer.Flush();
                return writer.ToString();
            }
        }

        static JsonSerializer jsonSerializer = new JsonSerializer();
    }
}
namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus;
    using AcceptanceTesting;
    using EndpointTemplates;
    using NUnit.Framework;
    using Extensibility;
    using Persistence.AzureTable.Release_2x;
    using Sagas;

    public class When_migrated_saga_completed : CompatibilityAcceptanceTest
    {
        [Test]
        public async Task Should_remove_secondary_index()
        {
            Requires.AzureTables();

            var correlationPropertyValue = Guid.NewGuid();
            var sagaId = Guid.NewGuid();

            var previousSagaData = new EndpointWithSagaThatWasMigrated.MigratedSagaData
            {
                Id = sagaId,
                OriginalMessageId = "",
                Originator = "",
                SomeId = correlationPropertyValue
            };

            var sagaCorrelationProperty = new SagaCorrelationProperty("SomeId", correlationPropertyValue);
            await PersisterUsingSecondaryIndexes.Save(previousSagaData, sagaCorrelationProperty, null, new ContextBag());

            var context = await Scenario.Define<Context>()
                .WithEndpoint<EndpointWithSagaThatWasMigrated>(b => b.When(session => session.SendLocal(new ContinueSagaMessage
                {
                    SomeId = correlationPropertyValue
                })))
                .Done(c => c.Done)
                .Run();

            var sagaEntity = GetByRowKey<EndpointWithSagaThatWasMigrated.MigratedSagaData>(context.SagaId.ToString());
            var partitionRowKeyTuple = SecondaryIndexKeyBuilder.BuildTableKey(typeof(EndpointWithSagaThatWasMigrated.MigratedSagaData), sagaCorrelationProperty);
            var secondaryIndexEntry = GetByPartitionKey<EndpointWithSagaThatWasMigrated.MigratedSagaData>(partitionRowKeyTuple.PartitionKey);

            Assert.IsNull(sagaEntity);
            Assert.IsNull(secondaryIndexEntry);
            Assert.AreEqual(sagaId, context.SagaId);
        }

        public class Context : ScenarioContext
        {
            public bool Done { get; set; }
            public Guid SagaId { get; set; }
        }

        public class EndpointWithSagaThatWasMigrated : EndpointConfigurationBuilder
        {
            public EndpointWithSagaThatWasMigrated()
            {
                EndpointSetup<DefaultServer>();
            }

            public class SagaWithMigratedData : Saga<MigratedSagaData>, IAmStartedByMessages<StartSagaMessage>, IHandleMessages<ContinueSagaMessage>
            {
                public SagaWithMigratedData(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(StartSagaMessage message, IMessageHandlerContext context)
                {
                    // would have been called this way on older persistence
                    Data.SomeId = message.SomeId;

                    return context.SendLocal(new ContinueSagaMessage { SomeId = message.SomeId });
                }

                public Task Handle(ContinueSagaMessage message, IMessageHandlerContext context)
                {
                    testContext.SagaId = Data.Id;
                    testContext.Done = true;
                    MarkAsComplete();
                    return Task.CompletedTask;
                }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<MigratedSagaData> mapper)
                {
                    mapper.ConfigureMapping<StartSagaMessage>(m => m.SomeId)
                        .ToSaga(s => s.SomeId);
                    mapper.ConfigureMapping<ContinueSagaMessage>(m => m.SomeId)
                        .ToSaga(s => s.SomeId);
                }

                readonly Context testContext;
            }

            public class MigratedSagaData : IContainSagaData
            {
                public Guid Id { get; set; }
                public string Originator { get; set; }
                public string OriginalMessageId { get; set; }
                public Guid SomeId { get; set; }
            }
        }

        public class StartSagaMessage : ICommand
        {
            public Guid SomeId { get; set; }
        }

        public class ContinueSagaMessage : ICommand
        {
            public Guid SomeId { get; set; }
        }
    }
}
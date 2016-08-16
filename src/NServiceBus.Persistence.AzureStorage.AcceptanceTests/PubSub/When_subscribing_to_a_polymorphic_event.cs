﻿namespace NServiceBus.AcceptanceTests.PubSub
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using EndpointTemplates;
    using Features;
    using NUnit.Framework;

    [Ignore("Polymorphic events are not supported by currently selected JSON serializer.")]
    public class When_subscribing_to_a_polymorphic_event : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Event_should_be_delivered()
        {
            var testContext = await Scenario.Define<Context>()
                .WithEndpoint<Publisher>(b => b.When(c => c.Subscriber1Subscribed && c.Subscriber2Subscribed, bus => bus.Publish(new MyEvent())))
                .WithEndpoint<Subscriber1>(b => b.When(async (session, context) =>
                {
                    await session.Subscribe<IMyEvent>();

                    if (context.HasNativePubSubSupport)
                        context.Subscriber1Subscribed = true;
                }))
                .WithEndpoint<Subscriber2>(b => b.When(async (session, context) =>
                {
                    await session.Subscribe<MyEvent>();

                    if (context.HasNativePubSubSupport)
                        context.Subscriber2Subscribed = true;
                }))
                .Done(c => c.Subscriber1GotTheEvent && c.Subscriber2GotTheEvent)
                .Run();

            Assert.True(testContext.Subscriber1GotTheEvent);
            Assert.True(testContext.Subscriber2GotTheEvent);
        }

        public class Context : ScenarioContext
        {
            public bool Subscriber1GotTheEvent { get; set; }

            public bool Subscriber2GotTheEvent { get; set; }

            public int NumberOfSubscribers { get; set; }

            public bool Subscriber1Subscribed { get; set; }

            public bool Subscriber2Subscribed { get; set; }
        }

        public class Publisher : EndpointConfigurationBuilder
        {
            public Publisher()
            {
                EndpointSetup<DefaultPublisher>(b => b.OnEndpointSubscribed<Context>((args, context) =>
                {
                    if (args.SubscriberReturnAddress.Contains("Subscriber1"))
                    {
                        context.Subscriber1Subscribed = true;
                    }

                    if (args.SubscriberReturnAddress.Contains("Subscriber2"))
                    {
                        context.Subscriber2Subscribed = true;
                    }
                }));
            }
        }

        public class Subscriber1 : EndpointConfigurationBuilder
        {
            public Subscriber1()
            {
                EndpointSetup<DefaultServer>(c => c.DisableFeature<AutoSubscribe>())
                    .AddMapping<IMyEvent>(typeof(Publisher));
            }

            public class MyEventHandler : IHandleMessages<IMyEvent>
            {
                public Context Context { get; set; }

                public Task Handle(IMyEvent messageThatIsEnlisted, IMessageHandlerContext context)
                {
                    Context.Subscriber1GotTheEvent = true;

                    return Task.FromResult(0);
                }
            }
        }

        public class Subscriber2 : EndpointConfigurationBuilder
        {
            public Subscriber2()
            {
                EndpointSetup<DefaultServer>(c => c.DisableFeature<AutoSubscribe>())
                    .AddMapping<MyEvent>(typeof(Publisher));
            }

            public class MyEventHandler : IHandleMessages<MyEvent>
            {
                public Context Context { get; set; }

                public Task Handle(MyEvent messageThatIsEnlisted, IMessageHandlerContext context)
                {
                    Context.Subscriber2GotTheEvent = true;

                    return Task.FromResult(0);
                }
            }
        }

        [Serializable]
        public class MyEvent : IMyEvent
        {
        }

        public interface IMyEvent : IEvent
        {
        }
    }
}
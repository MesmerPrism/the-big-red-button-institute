using NUnit.Framework;
using TheBigRedButtonInstitute.RustyXrBroker;
using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker.Tests
{
    public sealed class RustyXrBrokerProtocolTests
    {
        [Test]
        public void BuildSubscribeCommandJsonUsesBrokerEnvelope()
        {
            var json = RustyXrBrokerProtocol.BuildSubscribeCommandJson(
                "req-1",
                RustyXrBrokerDriveSignal.DefaultStream,
                "unity-test-client",
                "com.example.targetapp",
                "Unity Test Client",
                "1.0");

            var envelope = JsonUtility.FromJson<RustyXrBrokerCommandEnvelope>(json);

            Assert.That(envelope.type, Is.EqualTo("command"));
            Assert.That(envelope.schema, Is.EqualTo(RustyXrBrokerProtocol.CommandSchema));
            Assert.That(envelope.request_id, Is.EqualTo("req-1"));
            Assert.That(envelope.command, Is.EqualTo("subscribe"));
            Assert.That(envelope.client_id, Is.EqualTo("unity-test-client"));
            Assert.That(envelope.app_package, Is.EqualTo("com.example.targetapp"));
            Assert.That(envelope.@params.stream, Is.EqualTo(RustyXrBrokerDriveSignal.DefaultStream));
            Assert.That(json, Does.Contain("\"params\""));
        }

        [Test]
        public void BuildStatusRequestCommandJsonUsesGeneralClientMetadata()
        {
            var json = RustyXrBrokerProtocol.BuildStatusRequestCommandJson(
                "status-1",
                "unity-test-client",
                "com.example.targetapp",
                "Unity Test Client",
                "1.0");

            var envelope = JsonUtility.FromJson<RustyXrBrokerCommandEnvelope>(json);

            Assert.That(envelope.type, Is.EqualTo("command"));
            Assert.That(envelope.command, Is.EqualTo("status_request"));
            Assert.That(envelope.client_id, Is.EqualTo("unity-test-client"));
            Assert.That(envelope.app_label, Is.EqualTo("Unity Test Client"));
        }

        [Test]
        public void BuildOpenUiCommandJsonUsesBrokerEnvelope()
        {
            var json = RustyXrBrokerProtocol.BuildOpenUiCommandJson(
                "open-ui-1",
                "unity-test-client",
                "com.example.targetapp",
                "Unity Test Client",
                "1.0");

            var envelope = JsonUtility.FromJson<RustyXrBrokerCommandEnvelope>(json);

            Assert.That(envelope.type, Is.EqualTo("command"));
            Assert.That(envelope.schema, Is.EqualTo(RustyXrBrokerProtocol.CommandSchema));
            Assert.That(envelope.request_id, Is.EqualTo("open-ui-1"));
            Assert.That(envelope.command, Is.EqualTo("open_ui"));
            Assert.That(envelope.client_id, Is.EqualTo("unity-test-client"));
            Assert.That(envelope.@params.stream, Is.Null.Or.Empty);
        }

        [Test]
        public void BuildCloseUiCommandJsonUsesBrokerEnvelope()
        {
            var json = RustyXrBrokerProtocol.BuildCloseUiCommandJson(
                "close-ui-1",
                "unity-test-client",
                "com.example.targetapp",
                "Unity Test Client",
                "1.0");

            var envelope = JsonUtility.FromJson<RustyXrBrokerCommandEnvelope>(json);

            Assert.That(envelope.type, Is.EqualTo("command"));
            Assert.That(envelope.schema, Is.EqualTo(RustyXrBrokerProtocol.CommandSchema));
            Assert.That(envelope.request_id, Is.EqualTo("close-ui-1"));
            Assert.That(envelope.command, Is.EqualTo("close_ui"));
            Assert.That(envelope.client_id, Is.EqualTo("unity-test-client"));
            Assert.That(envelope.@params.stream, Is.Null.Or.Empty);
        }

        [Test]
        public void BuildHelloJsonAdvertisesBrokerContract()
        {
            var json = RustyXrBrokerProtocol.BuildHelloJson(
                "unity-test-client",
                "com.example.targetapp",
                "Unity Test Client",
                "1.0");

            var hello = JsonUtility.FromJson<RustyXrBrokerHelloEnvelope>(json);

            Assert.That(hello.type, Is.EqualTo("hello"));
            Assert.That(hello.schema, Is.EqualTo(RustyXrBrokerProtocol.HelloSchema));
            Assert.That(hello.client_id, Is.EqualTo("unity-test-client"));
            Assert.That(hello.protocol_min, Is.EqualTo(RustyXrBrokerProtocol.ContractVersion));
            Assert.That(hello.protocol_max, Is.EqualTo(RustyXrBrokerProtocol.ContractVersion));
            Assert.That(hello.supports_commands, Is.True);
        }

        [Test]
        public void TryParseCommandAckPreservesRequestAndSubscription()
        {
            const string json = @"{
                ""type"": ""command_ack"",
                ""schema"": ""rusty.xr.broker.command_ack.v1"",
                ""request_id"": ""req-1"",
                ""command"": ""subscribe"",
                ""accepted"": true,
                ""message"": ""subscribed"",
                ""result"": {
                    ""stream"": ""osc:/rusty-xr/drive/radius"",
                    ""subscription_id"": ""sub-1""
                }
            }";

            Assert.That(RustyXrBrokerProtocol.TryParseCommandAck(json, out var ack), Is.True);
            Assert.That(ack.request_id, Is.EqualTo("req-1"));
            Assert.That(ack.accepted, Is.True);
            Assert.That(ack.result.stream, Is.EqualTo(RustyXrBrokerDriveSignal.DefaultStream));
            Assert.That(ack.result.subscription_id, Is.EqualTo("sub-1"));
        }

        [Test]
        public void TryParseStreamEventAcceptsOscDrivePayload()
        {
            const string json = @"{
                ""type"": ""stream_event"",
                ""schema"": ""rusty.xr.broker.stream_event.v1"",
                ""stream"": ""osc:/rusty-xr/drive/radius"",
                ""subscription_id"": ""sub-1"",
                ""sequence_id"": 7,
                ""broker_time_unix_ns"": 123456,
                ""broker_time_elapsed_ns"": 654321,
                ""payload"": {
                    ""address"": ""/rusty-xr/drive/radius"",
                    ""value01"": 0.75,
                    ""peer"": ""127.0.0.1:9000"",
                    ""argument_type"": ""float""
                }
            }";

            Assert.That(RustyXrBrokerProtocol.TryParseStreamEvent(json, out var streamEvent), Is.True);
            Assert.That(streamEvent.stream, Is.EqualTo(RustyXrBrokerDriveSignal.DefaultStream));
            Assert.That(streamEvent.sequence_id, Is.EqualTo(7));
            Assert.That(streamEvent.payload.value01, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void DriveSignalReceiverAppliesTargetStreamOnly()
        {
            var target = new GameObject("broker-drive-receiver-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerDriveSignalReceiver>();

                Assert.That(receiver.ApplyStreamEventJson(StreamEventJson(RustyXrBrokerDriveSignal.DefaultStream, 1.25f)), Is.True);
                Assert.That(receiver.Value01, Is.EqualTo(1f).Within(0.0001f));

                Assert.That(receiver.ApplyStreamEventJson(StreamEventJson("latency:sample", 0.1f)), Is.False);
                Assert.That(receiver.Value01, Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void EventRouterAppliesBrokerStreamToDriveReceiver()
        {
            var target = new GameObject("broker-router-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerDriveSignalReceiver>();
                var router = target.AddComponent<RustyXrBrokerEventRouter>();
                router.ConfigureReferences(null, receiver);

                Assert.That(router.ApplyStreamEventJson(StreamEventJson(RustyXrBrokerDriveSignal.DefaultStream, 0.66f)), Is.True);
                Assert.That(router.RoutedEvents, Is.EqualTo(1));
                Assert.That(router.AppliedDriveEvents, Is.EqualTo(1));
                Assert.That(receiver.Value01, Is.EqualTo(0.66f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ButtonDriverTriggersOnRisingThresholdCrossing()
        {
            Assert.That(RustyXrBrokerButtonDriver.ShouldTrigger(0.49f, 0.5f, 0.5f, true), Is.True);
            Assert.That(RustyXrBrokerButtonDriver.ShouldTrigger(0.7f, 0.8f, 0.5f, true), Is.False);
            Assert.That(RustyXrBrokerButtonDriver.ShouldTrigger(0.7f, 0.8f, 0.5f, false), Is.True);

            var target = new GameObject("broker-button-driver-test");
            try
            {
                var driver = target.AddComponent<RustyXrBrokerButtonDriver>();
                var pulses = 0;
                driver.DrivePulseRequested += _ => pulses++;

                Assert.That(driver.ApplyBrokerDriveValue(0.25f, 0d, true), Is.False);
                Assert.That(driver.ApplyBrokerDriveValue(0.75f, 1d, true), Is.True);
                Assert.That(driver.TriggerCount, Is.EqualTo(1));
                Assert.That(pulses, Is.EqualTo(1));
                Assert.That(driver.ApplyBrokerDriveValue(0.9f, 2d, true), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        static string StreamEventJson(string stream, float value01) =>
            "{" +
            "\"type\":\"stream_event\"," +
            "\"schema\":\"rusty.xr.broker.stream_event.v1\"," +
            $"\"stream\":\"{stream}\"," +
            "\"subscription_id\":\"sub-1\"," +
            "\"sequence_id\":1," +
            "\"payload\":{" +
            "\"address\":\"/rusty-xr/drive/radius\"," +
            $"\"value01\":{value01.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            "}" +
            "}";
    }
}

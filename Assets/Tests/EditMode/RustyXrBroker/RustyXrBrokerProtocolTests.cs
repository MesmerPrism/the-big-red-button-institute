using System;
using System.IO;
using System.Text;
using TheBigRedButtonInstitute.Diagnostics;
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
                UnityEngine.Object.DestroyImmediate(target);
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
                UnityEngine.Object.DestroyImmediate(target);
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
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void DiagnosticRouteStatsCountDropsDuplicatesAndLatency()
        {
            var stats = new BigRedButtonDiagnosticRouteStats(BigRedButtonDiagnosticRouteId.DirectUnityOsc);

            stats.RecordSample(
                new BigRedButtonDiagnosticSample(1, 0.25f, 1_000_000_000L, 0L, 1_010_000_000L, "test"),
                acceptedPulse: false);
            stats.RecordSample(
                new BigRedButtonDiagnosticSample(3, 1.25f, 1_020_000_000L, 1_025_000_000L, 1_030_000_000L, "test"),
                acceptedPulse: true);
            stats.RecordSample(
                new BigRedButtonDiagnosticSample(3, 0.1f, 0L, 0L, 1_040_000_000L, "test"),
                acceptedPulse: false);

            Assert.That(stats.ReceivedSamples, Is.EqualTo(3));
            Assert.That(stats.AcceptedPulses, Is.EqualTo(1));
            Assert.That(stats.DroppedSamples, Is.EqualTo(1));
            Assert.That(stats.DuplicateOrOutOfOrderSamples, Is.EqualTo(1));
            Assert.That(stats.LastValue01, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(stats.LastSourceToUnityLatencyMs, Is.EqualTo(10d).Within(0.001d));
            Assert.That(stats.LastBrokerToUnityLatencyMs, Is.EqualTo(5d).Within(0.001d));
        }

        [Test]
        public void OscDriveParserAcceptsFloatValueAndSequence()
        {
            var packet = BuildOscPacket(
                "/rusty-xr/drive/radius",
                ",fisi",
                0.75f,
                42,
                "123456789000",
                19001);

            Assert.That(
                BigRedButtonOscDriveMessageParser.TryDecodeDriveMessage(
                    packet,
                    packet.Length,
                    "/rusty-xr/drive/radius",
                    "127.0.0.1:9000",
                    "127.0.0.1",
                    9000,
                    123456789999L,
                    out var message,
                    out var error),
                Is.True,
                error);
            Assert.That(message.Address, Is.EqualTo("/rusty-xr/drive/radius"));
            Assert.That(message.Value01, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(message.SequenceId, Is.EqualTo(42));
            Assert.That(message.ClientSendTimeUnixNs, Is.EqualTo(123456789000L));
            Assert.That(message.ReceivedTimeUnixNs, Is.EqualTo(123456789999L));
            Assert.That(message.ReplyPort, Is.EqualTo(19001));
            Assert.That(message.FirstArgumentType, Is.EqualTo("f"));
            Assert.That(message.Peer, Is.EqualTo("127.0.0.1:9000"));
            Assert.That(message.PeerHost, Is.EqualTo("127.0.0.1"));
            Assert.That(message.PeerPort, Is.EqualTo(9000));
        }

        [Test]
        public void OscDriveAcknowledgementEncoderCarriesClockFields()
        {
            var packet = BigRedButtonOscDriveMessageParser.EncodeDriveAcknowledgement(
                "/rusty-xr/drive/ack",
                7,
                0.6f,
                1_000_000_000L,
                1_005_000_000L,
                1_006_000_000L,
                acceptedPulse: true);

            var address = ReadPaddedString(packet, 0, packet.Length, out var cursor);
            var tags = ReadPaddedString(packet, cursor, packet.Length, out cursor);

            Assert.That(address, Is.EqualTo("/rusty-xr/drive/ack"));
            Assert.That(tags, Is.EqualTo(",isssfT"));
            Assert.That(ReadInt32BigEndian(packet, cursor), Is.EqualTo(7));
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

        static byte[] BuildOscPacket(string address, string typeTags, float value, int sequence, string clientSendTimeUnixNs, int replyPort)
        {
            using var stream = new MemoryStream();
            WritePaddedString(stream, address);
            WritePaddedString(stream, typeTags);
            WriteInt32BigEndian(stream, BitConverter.SingleToInt32Bits(value));
            WriteInt32BigEndian(stream, sequence);
            WritePaddedString(stream, clientSendTimeUnixNs);
            WriteInt32BigEndian(stream, replyPort);
            return stream.ToArray();
        }

        static string ReadPaddedString(byte[] data, int offset, int limit, out int nextOffset)
        {
            var cursor = offset;
            while (cursor < limit && data[cursor] != 0)
            {
                cursor++;
            }

            var value = Encoding.UTF8.GetString(data, offset, cursor - offset);
            nextOffset = offset + ((cursor - offset + 1) + ((4 - ((cursor - offset + 1) % 4)) % 4));
            return value;
        }

        static void WritePaddedString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte(0);
            while (stream.Length % 4 != 0)
            {
                stream.WriteByte(0);
            }
        }

        static void WriteInt32BigEndian(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }
    }
}

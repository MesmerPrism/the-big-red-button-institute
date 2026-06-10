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
        public void BuildPolarPmdStartCommandJsonCarriesScanTimeout()
        {
            var json = RustyXrBrokerProtocol.BuildPolarPmdStartCommandJson(
                "polar-pmd-1",
                "unity-test-client",
                "com.example.targetapp",
                "Unity Test Client",
                "1.0",
                45000);

            var envelope = JsonUtility.FromJson<RustyXrBrokerCommandEnvelope>(json);

            Assert.That(envelope.type, Is.EqualTo("command"));
            Assert.That(envelope.command, Is.EqualTo("polar_pmd.start"));
            Assert.That(envelope.@params.scan_timeout_ms, Is.EqualTo(45000));
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
            Assert.That(hello.protocol_min, Is.EqualTo(RustyXrBrokerProtocol.ProtocolVersionMin));
            Assert.That(hello.protocol_max, Is.EqualTo(RustyXrBrokerProtocol.ProtocolVersionMax));
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
        public void TryParseStreamEventAcceptsPublicBrokerHeaderShape()
        {
            const string json = @"{
                ""type"": ""stream_event"",
                ""schema"": ""rusty.xr.broker.stream_event.v1"",
                ""stream"": ""osc:/rusty-xr/drive/radius"",
                ""subscription_id"": ""sub-1"",
                ""header"": {
                    ""schema"": ""rusty.xr.broker.stream_sample_header.v1"",
                    ""stream_id"": ""osc:/rusty-xr/drive/radius"",
                    ""session_id"": ""session-001"",
                    ""source_id"": ""broker-synthetic"",
                    ""payload_kind"": ""Json"",
                    ""payload_schema"": ""rusty.xr.synthetic.wave.v1"",
                    ""sequence_number"": 8,
                    ""broker_time_elapsed_ns"": 111000,
                    ""broker_time_unix_ns"": 222000,
                    ""source_time_ns"": 99000,
                    ""source_time_unix_ns"": 220000,
                    ""dropped_before_sample"": 1,
                    ""late_before_sample"": 2
                },
                ""payload"": {
                    ""value01"": 0.62
                }
            }";

            Assert.That(RustyXrBrokerProtocol.TryParseStreamEvent(json, out var streamEvent), Is.True);
            Assert.That(streamEvent.stream, Is.EqualTo(RustyXrBrokerDriveSignal.DefaultStream));
            Assert.That(streamEvent.sequence_id, Is.EqualTo(8));
            Assert.That(streamEvent.broker_time_unix_ns, Is.EqualTo(222000));
            Assert.That(streamEvent.broker_time_elapsed_ns, Is.EqualTo(111000));
            Assert.That(streamEvent.source_time_ns, Is.EqualTo(99000));
            Assert.That(streamEvent.payload_schema, Is.EqualTo(RustyXrBrokerProtocol.SyntheticWavePayloadSchema));
            Assert.That(streamEvent.dropped_before_sample, Is.EqualTo(1));
            Assert.That(streamEvent.late_before_sample, Is.EqualTo(2));
            Assert.That(streamEvent.payload.value01, Is.EqualTo(0.62f).Within(0.0001f));
        }

        [Test]
        public void ReplayRecordFixtureShapeCanDriveSyntheticWaveReceiver()
        {
            Assert.That(
                RustyXrBrokerProtocol.TryParseReplayRecordAsStreamEvent(
                    SyntheticWaveReplayRecordJson,
                    out var streamEvent),
                Is.True);
            Assert.That(streamEvent.stream, Is.EqualTo(RustyXrBrokerDriveSignal.SyntheticWaveStream));
            Assert.That(streamEvent.sequence_id, Is.EqualTo(1));
            Assert.That(streamEvent.payload_schema, Is.EqualTo(RustyXrBrokerProtocol.SyntheticWavePayloadSchema));

            var target = new GameObject("broker-replay-wave-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerDriveSignalReceiver>();
                receiver.StreamId = RustyXrBrokerDriveSignal.SyntheticWaveStream;

                Assert.That(receiver.ApplyStreamEvent(streamEvent), Is.True);
                Assert.That(receiver.Value01, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(receiver.LastSequenceId, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ReplayRecordFixtureShapeCanDriveScreenGazeReceiver()
        {
            Assert.That(
                RustyXrBrokerProtocol.TryParseReplayRecordAsStreamEvent(
                    SyntheticEyeBlinkReplayRecordJson,
                    out var streamEvent),
                Is.True);
            Assert.That(streamEvent.stream, Is.EqualTo(RustyXrBrokerScreenGazeReceiver.DefaultStream));
            Assert.That(streamEvent.sequence_id, Is.EqualTo(5));
            Assert.That(streamEvent.payload_schema, Is.EqualTo(RustyXrBrokerProtocol.EyeScreenGazePointSchema));

            var target = new GameObject("broker-replay-eye-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerScreenGazeReceiver>();

                Assert.That(receiver.ApplyStreamEvent(streamEvent), Is.True);
                Assert.That(receiver.NormalizedPoint.x, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(receiver.NormalizedPoint.y, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(receiver.SampleValid, Is.False);
                Assert.That(receiver.Confidence01, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(receiver.LastSequenceNumber, Is.EqualTo(5));
                Assert.That(receiver.LastSampleTimeNs, Is.EqualTo(55555555));
                Assert.That(receiver.LastProviderId, Is.EqualTo("synthetic-eye-provider"));
                Assert.That(receiver.LastSourceDeviceId, Is.EqualTo("desktop-eye-source"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
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
        public void DriveSignalReceiverAppliesHeaderShapedSyntheticPayload()
        {
            var target = new GameObject("broker-header-drive-receiver-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerDriveSignalReceiver>();

                Assert.That(receiver.ApplyStreamEventJson(HeaderStreamEventJson(RustyXrBrokerDriveSignal.DefaultStream, 0.42f)), Is.True);
                Assert.That(receiver.Value01, Is.EqualTo(0.42f).Within(0.0001f));
                Assert.That(receiver.LastSequenceId, Is.EqualTo(9));
                Assert.That(receiver.LastBrokerTimeUnixNs, Is.EqualTo(123456789000L));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void SyntheticWaveStreamCanDriveReceiverWhenConfigured()
        {
            var target = new GameObject("broker-synthetic-drive-receiver-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerDriveSignalReceiver>();
                receiver.StreamId = RustyXrBrokerDriveSignal.SyntheticWaveStream;

                Assert.That(receiver.ApplyStreamEventJson(HeaderStreamEventJson(RustyXrBrokerDriveSignal.SyntheticWaveStream, 0.9f)), Is.True);
                Assert.That(receiver.Value01, Is.EqualTo(0.9f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ScreenGazeReceiverAppliesSyntheticEyePayload()
        {
            var target = new GameObject("broker-screen-gaze-receiver-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerScreenGazeReceiver>();

                Assert.That(receiver.ApplyStreamEventJson(EyeGazeStreamEventJson(0.25f, 0.75f, sampleValid: true)), Is.True);
                Assert.That(receiver.NormalizedPoint.x, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(receiver.NormalizedPoint.y, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(receiver.SampleValid, Is.True);
                Assert.That(receiver.Confidence01, Is.EqualTo(0.86f).Within(0.0001f));
                Assert.That(receiver.LastSequenceNumber, Is.EqualTo(11));
                Assert.That(receiver.LastSampleTimeNs, Is.EqualTo(555000));
                Assert.That(receiver.LastBrokerTimeUnixNs, Is.EqualTo(777000));
                Assert.That(receiver.LastProviderId, Is.EqualTo("synthetic-eye"));
                Assert.That(receiver.LastSourceDeviceId, Is.EqualTo("synthetic-screen"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ScreenGazeReceiverPreservesInvalidSyntheticSampleFlag()
        {
            var target = new GameObject("broker-screen-gaze-invalid-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerScreenGazeReceiver>();

                Assert.That(receiver.ApplyStreamEventJson(EyeGazeStreamEventJson(0.5f, 0.5f, sampleValid: false)), Is.True);
                Assert.That(receiver.SampleValid, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void EventRouterAppliesBrokerEyeStreamToGazeReceiver()
        {
            var target = new GameObject("broker-eye-router-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerScreenGazeReceiver>();
                var router = target.AddComponent<RustyXrBrokerEventRouter>();
                router.ConfigureReferences(null);
                router.ConfigureScreenGazeReferences(receiver);

                Assert.That(router.ApplyStreamEventJson(EyeGazeStreamEventJson(0.4f, 0.6f, sampleValid: true)), Is.True);
                Assert.That(router.RoutedEvents, Is.EqualTo(1));
                Assert.That(router.AppliedScreenGazeEvents, Is.EqualTo(1));
                Assert.That(receiver.NormalizedPoint.x, Is.EqualTo(0.4f).Within(0.0001f));
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
        public void BioSignalReceiverAcceptsBrokerBreathEvents()
        {
            var target = new GameObject("broker-bio-receiver-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerBioSignalReceiver>();
                var received = false;
                RustyXrBrokerBioSignalSample lastSample = default;
                receiver.BioSignalReceived += sample =>
                {
                    received = true;
                    lastSample = sample;
                };

                Assert.That(receiver.ApplyStreamEventJson(BioBreathStreamEventJson()), Is.True);
                Assert.That(received, Is.True);
                Assert.That(lastSample.StreamId, Is.EqualTo(RustyXrBrokerBioSignalReceiver.BreathStream));
                Assert.That(lastSample.SourceLabel, Is.EqualTo("polar_acc"));
                Assert.That(lastSample.Value01, Is.EqualTo(0.42f).Within(0.0001f));
                Assert.That(lastSample.SourceTimeUnixNs, Is.EqualTo(1234000L));
                Assert.That(lastSample.BrokerTimeUnixNs, Is.EqualTo(1235000L));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void EventRouterAppliesBrokerBioStreamToReceiver()
        {
            var target = new GameObject("broker-bio-router-test");
            try
            {
                var receiver = target.AddComponent<RustyXrBrokerBioSignalReceiver>();
                var router = target.AddComponent<RustyXrBrokerEventRouter>();
                router.ConfigureReferences(null);
                router.ConfigureBioSignalReferences(receiver);

                Assert.That(router.ApplyStreamEventJson(BioBreathStreamEventJson()), Is.True);
                Assert.That(router.RoutedEvents, Is.EqualTo(1));
                Assert.That(router.AppliedBioSignalEvents, Is.EqualTo(1));
                Assert.That(receiver.LastStreamId, Is.EqualTo(RustyXrBrokerBioSignalReceiver.BreathStream));
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

        static string HeaderStreamEventJson(string stream, float value01) =>
            "{" +
            "\"type\":\"stream_event\"," +
            "\"schema\":\"rusty.xr.broker.stream_event.v1\"," +
            $"\"stream\":\"{stream}\"," +
            "\"subscription_id\":\"sub-1\"," +
            "\"header\":{" +
            "\"schema\":\"rusty.xr.broker.stream_sample_header.v1\"," +
            $"\"stream_id\":\"{stream}\"," +
            "\"session_id\":\"session-001\"," +
            "\"source_id\":\"synthetic-provider\"," +
            "\"payload_kind\":\"Json\"," +
            "\"payload_schema\":\"rusty.xr.synthetic.wave.v1\"," +
            "\"sequence_number\":9," +
            "\"broker_time_elapsed_ns\":123456780000," +
            "\"broker_time_unix_ns\":123456789000," +
            "\"source_time_ns\":123450000000," +
            "\"source_time_unix_ns\":123456700000," +
            "\"dropped_before_sample\":0," +
            "\"late_before_sample\":0" +
            "}," +
            "\"payload\":{" +
            $"\"value01\":{value01.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            "}" +
            "}";

        static string BioBreathStreamEventJson() =>
            "{" +
            "\"type\":\"stream_event\"," +
            "\"schema\":\"rusty.xr.broker.stream_event.v1\"," +
            $"\"stream\":\"{RustyXrBrokerBioSignalReceiver.BreathStream}\"," +
            "\"sequence_id\":12," +
            "\"broker_time_unix_ns\":1235000," +
            "\"payload_schema\":\"rusty.xr.bio.breath.v1\"," +
            "\"payload\":{" +
            "\"schema\":\"rusty.xr.bio.breath.v1\"," +
            "\"source\":\"polar_acc\"," +
            "\"sample_time_unix_ns\":1234000," +
            "\"volume01\":0.42," +
            "\"has_volume\":true" +
            "}" +
            "}";

        static string EyeGazeStreamEventJson(float x, float y, bool sampleValid) =>
            "{" +
            "\"type\":\"stream_event\"," +
            "\"schema\":\"rusty.xr.broker.stream_event.v1\"," +
            "\"stream\":\"eye.screen.gaze_point\"," +
            "\"subscription_id\":\"sub-eye\"," +
            "\"header\":{" +
            "\"schema\":\"rusty.xr.broker.stream_sample_header.v1\"," +
            "\"stream_id\":\"eye.screen.gaze_point\"," +
            "\"session_id\":\"session-001\"," +
            "\"source_id\":\"synthetic-eye\"," +
            "\"payload_kind\":\"Json\"," +
            "\"payload_schema\":\"rusty.xr.eye.screen.gaze_point.v1\"," +
            "\"sequence_number\":11," +
            "\"broker_time_elapsed_ns\":666000," +
            "\"broker_time_unix_ns\":777000," +
            "\"source_time_ns\":555000," +
            "\"source_time_unix_ns\":776000," +
            "\"dropped_before_sample\":0," +
            "\"late_before_sample\":0" +
            "}," +
            "\"payload\":{" +
            "\"schema\":\"rusty.xr.eye.screen.gaze_point.v1\"," +
            "\"base\":{" +
            "\"provider_id\":\"synthetic-eye\"," +
            "\"source_device_id\":\"synthetic-screen\"," +
            "\"sequence_number\":11," +
            "\"sample_time_ns\":555000," +
            "\"broker_receive_time_ns\":666000," +
            "\"validity\":{" +
            $"\"sample_valid\":{(sampleValid ? "true" : "false")}," +
            "\"left_valid\":true," +
            "\"right_valid\":true," +
            "\"blink\":false," +
            $"\"tracking_lost\":{(sampleValid ? "false" : "true")}" +
            "}," +
            "\"confidence\":0.86," +
            "\"eye\":\"Combined\"," +
            "\"coordinate_space\":\"ScreenNormalized\"" +
            "}," +
            $"\"normalized_point\":{{\"x\":{x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"y\":{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}," +
            "\"display_id\":\"synthetic-display\"" +
            "}" +
            "}";

        const string SyntheticWaveReplayRecordJson = @"{
            ""type"": ""replay_record"",
            ""schema"": ""rusty.xr.broker.replay_record.v1"",
            ""session_id"": ""synthetic-broker-wave-session"",
            ""stream"": ""synthetic:wave"",
            ""header"": {
                ""schema"": ""rusty.xr.broker.stream_sample_header.v1"",
                ""stream_id"": ""synthetic:wave"",
                ""session_id"": ""synthetic-broker-wave-session"",
                ""source_id"": ""synthetic-wave-provider"",
                ""payload_kind"": ""Json"",
                ""payload_schema"": ""rusty.xr.synthetic.wave.v1"",
                ""sequence_number"": 1,
                ""broker_time_elapsed_ns"": 16666667,
                ""broker_time_unix_ns"": 0,
                ""source_time_ns"": 16666667,
                ""source_time_unix_ns"": 0,
                ""dropped_before_sample"": 0,
                ""late_before_sample"": 0
            },
            ""payload"": {
                ""sequence_number"": 1,
                ""sample_time_elapsed_ns"": 16666667,
                ""value01"": 1.0,
                ""phase01"": 0.25,
                ""valid"": true
            }
        }";

        const string SyntheticEyeBlinkReplayRecordJson = @"{
            ""type"": ""replay_record"",
            ""schema"": ""rusty.xr.broker.replay_record.v1"",
            ""session_id"": ""synthetic-eye-screen-gaze-session"",
            ""stream"": ""eye.screen.gaze_point"",
            ""header"": {
                ""schema"": ""rusty.xr.broker.stream_sample_header.v1"",
                ""stream_id"": ""eye.screen.gaze_point"",
                ""session_id"": ""synthetic-eye-screen-gaze-session"",
                ""source_id"": ""synthetic-eye-provider"",
                ""payload_kind"": ""Json"",
                ""payload_schema"": ""rusty.xr.eye.screen.gaze_point.v1"",
                ""sequence_number"": 5,
                ""broker_time_elapsed_ns"": 55555555,
                ""broker_time_unix_ns"": 0,
                ""source_time_ns"": 55555555,
                ""source_time_unix_ns"": 0,
                ""dropped_before_sample"": 0,
                ""late_before_sample"": 0
            },
            ""payload"": {
                ""schema"": ""rusty.xr.eye.screen.gaze_point.v1"",
                ""base"": {
                    ""provider_id"": ""synthetic-eye-provider"",
                    ""source_device_id"": ""desktop-eye-source"",
                    ""sequence_number"": 5,
                    ""sample_time_ns"": 55555555,
                    ""broker_receive_time_ns"": 55555555,
                    ""validity"": {
                        ""sample_valid"": false,
                        ""left_valid"": false,
                        ""right_valid"": false,
                        ""blink"": true,
                        ""tracking_lost"": true
                    },
                    ""confidence"": 0.0,
                    ""eye"": ""Combined"",
                    ""coordinate_space"": ""ScreenNormalized""
                },
                ""display_id"": ""primary-display"",
                ""normalized_point"": {
                    ""x"": 0.5,
                    ""y"": 0.5
                },
                ""screen_pixel"": null,
                ""pupil_diameter_mm"": null
            }
        }";

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

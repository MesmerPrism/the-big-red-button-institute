using NUnit.Framework;
using TheBigRedButtonInstitute.Morphospace.Manifold;

namespace TheBigRedButtonInstitute.Morphospace.Manifold.Tests
{
    public sealed class ManifoldProtocolTests
    {
        [Test]
        public void BuildCommandEnvelopeJsonUsesManifoldAuthorityShape()
        {
            var json = ManifoldProtocol.BuildCommandEnvelopeJson(
                "request.start.synthetic_wave",
                "command.module.start",
                "module.synthetic_wave_provider",
                "module.synthetic_wave_provider",
                ManifoldProtocol.EmptyCommandInputSchema,
                1,
                ManifoldProtocol.ModuleControlCapability,
                "lease.synthetic_module",
                "bounded_mutation",
                "holder.test_agent",
                1765000000000L);

            Assert.That(json, Does.Contain("\"$schema\":\"rusty.manifold.command.envelope.v1\""));
            Assert.That(ManifoldProtocol.TryParseCommandEnvelope(json, out var envelope), Is.True);
            Assert.That(envelope.request_id, Is.EqualTo("request.start.synthetic_wave"));
            Assert.That(envelope.command_id, Is.EqualTo("command.module.start"));
            Assert.That(envelope.target_scope, Is.EqualTo("module.synthetic_wave_provider"));
            Assert.That(envelope.expected_revision, Is.EqualTo(1));
            Assert.That(envelope.lease_id, Is.EqualTo("lease.synthetic_module"));
            Assert.That(envelope.holder_id, Is.EqualTo("holder.test_agent"));
        }

        [Test]
        public void BuildStreamSubscriptionRequestJsonUsesManifoldSubscriptionShape()
        {
            var json = ManifoldProtocol.BuildStreamSubscriptionRequestJson(
                "request.stream_subscription.synthetic_wave_ui",
                "subscriber.ui.synthetic_dashboard",
                "ui",
                1,
                1,
                ManifoldProtocol.SyntheticWaveStreamId,
                ManifoldProtocol.DefaultTransportId,
                30000,
                ManifoldProtocol.StreamSubscribeCapability,
                1765000000000L);

            Assert.That(json, Does.Contain("\"$schema\":\"rusty.manifold.stream.subscription_request.v1\""));
            Assert.That(ManifoldProtocol.TryParseStreamSubscriptionRequest(json, out var request), Is.True);
            Assert.That(request.request_id, Is.EqualTo("request.stream_subscription.synthetic_wave_ui"));
            Assert.That(request.subscriber_id, Is.EqualTo("subscriber.ui.synthetic_dashboard"));
            Assert.That(request.subscriber_kind, Is.EqualTo("ui"));
            Assert.That(request.expected_authority_revision, Is.EqualTo(1));
            Assert.That(request.expected_registry_revision, Is.EqualTo(1));
            Assert.That(request.stream_id, Is.EqualTo(ManifoldProtocol.SyntheticWaveStreamId));
            Assert.That(request.transport_id, Is.EqualTo(ManifoldProtocol.DefaultTransportId));
            Assert.That(request.required_capability, Is.EqualTo(ManifoldProtocol.StreamSubscribeCapability));
        }

        [Test]
        public void TryParseStreamRegistrySnapshotAcceptsManifoldFixtureShape()
        {
            Assert.That(ManifoldProtocol.TryParseStreamRegistrySnapshot(SyntheticStreamRegistryJson, out var snapshot), Is.True);
            Assert.That(snapshot.registry_revision, Is.EqualTo(1));
            Assert.That(snapshot.streams, Has.Length.EqualTo(2));
            Assert.That(snapshot.streams[0].schema, Is.EqualTo(ManifoldProtocol.StreamManifestSchema));
            Assert.That(snapshot.streams[0].stream_id, Is.EqualTo(ManifoldProtocol.SyntheticWaveStreamId));
            Assert.That(snapshot.streams[0].sample_schema, Is.EqualTo(ManifoldProtocol.ScalarF32SampleSchema));
            Assert.That(snapshot.streams[0].transport_offers[0].transport_id, Is.EqualTo(ManifoldProtocol.DefaultTransportId));
            Assert.That(snapshot.streams[0].subscription.ui_subscribable, Is.True);
        }

        [Test]
        public void TryParseCommandAckAndRejectionUseDisplaySafeManifoldResults()
        {
            const string ackJson = @"{
                ""$schema"": ""rusty.manifold.command.ack.v1"",
                ""request_id"": ""request.start.synthetic_wave"",
                ""accepted_revision"": 1,
                ""lease_id"": ""lease.synthetic_module"",
                ""authority_id"": ""authority.synthetic"",
                ""accepted_at_ms"": 1765000000001
            }";

            Assert.That(ManifoldProtocol.TryParseCommandAck(ackJson, out var ack), Is.True);
            Assert.That(ack.request_id, Is.EqualTo("request.start.synthetic_wave"));
            Assert.That(ack.accepted_revision, Is.EqualTo(1));
            Assert.That(ack.authority_id, Is.EqualTo("authority.synthetic"));

            const string rejectionJson = @"{
                ""$schema"": ""rusty.manifold.command.rejection.v1"",
                ""request_id"": ""request.start.synthetic_wave"",
                ""rejection_code"": ""issue.command.stale_revision"",
                ""message"": ""expected revision does not match current revision"",
                ""retryable"": true,
                ""current_revision"": 2
            }";

            Assert.That(ManifoldProtocol.TryParseCommandRejection(rejectionJson, out var rejection), Is.True);
            Assert.That(rejection.rejection_code, Is.EqualTo("issue.command.stale_revision"));
            Assert.That(rejection.retryable, Is.True);
            Assert.That(rejection.current_revision, Is.EqualTo(2));
        }

        [Test]
        public void ClassifySchemaKeepsUnknownSchemasOutOfManifold()
        {
            Assert.That(
                ManifoldProtocol.ClassifySchema("rusty.manifold.command.envelope.v1"),
                Is.EqualTo(ManifoldContractLane.Manifold));
            Assert.That(
                ManifoldProtocol.ClassifySchema("brb.manifold.sample.button_drive.v1"),
                Is.EqualTo(ManifoldContractLane.BrbUnityAdapter));
            Assert.That(ManifoldProtocol.ClassifySchema("example.unknown.v1"), Is.EqualTo(ManifoldContractLane.Unknown));
        }

        const string SyntheticStreamRegistryJson = @"{
            ""$schema"": ""rusty.manifold.stream.registry_snapshot.v1"",
            ""registry_revision"": 1,
            ""streams"": [
                {
                    ""$schema"": ""rusty.manifold.stream.manifest.v1"",
                    ""stream_id"": ""stream.synthetic_wave"",
                    ""source_module_id"": ""module.synthetic_wave_provider"",
                    ""semantic_family"": ""synthetic.scalar"",
                    ""sample_schema"": ""rusty.manifold.sample.scalar_f32.v1"",
                    ""rate_class"": ""periodic"",
                    ""timestamp_domains"": [
                        ""clock.host_monotonic""
                    ],
                    ""retention"": {
                        ""policy"": ""ephemeral""
                    },
                    ""sensitivity"": ""synthetic"",
                    ""transport_offers"": [
                        {
                            ""transport_id"": ""transport.in_process"",
                            ""transport"": ""in_process"",
                            ""endpoint_id"": null
                        }
                    ],
                    ""subscription"": {
                        ""ui_subscribable"": true,
                        ""max_subscribers"": 8
                    }
                },
                {
                    ""$schema"": ""rusty.manifold.stream.manifest.v1"",
                    ""stream_id"": ""stream.synthetic_rms"",
                    ""source_module_id"": ""module.synthetic_wave_processor"",
                    ""semantic_family"": ""synthetic.scalar"",
                    ""sample_schema"": ""rusty.manifold.sample.scalar_f32.v1"",
                    ""rate_class"": ""periodic"",
                    ""timestamp_domains"": [
                        ""clock.host_monotonic""
                    ],
                    ""retention"": {
                        ""policy"": ""ephemeral""
                    },
                    ""sensitivity"": ""synthetic"",
                    ""transport_offers"": [
                        {
                            ""transport_id"": ""transport.in_process"",
                            ""transport"": ""in_process"",
                            ""endpoint_id"": null
                        }
                    ],
                    ""subscription"": {
                        ""ui_subscribable"": true,
                        ""max_subscribers"": 8
                    }
                }
            ]
        }";
    }
}

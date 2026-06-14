using System;
using System.Text;
using UnityEngine;

namespace TheBigRedButtonInstitute.Morphospace.Manifold
{
    public enum ManifoldContractLane
    {
        Unknown = 0,
        Manifold = 1,
        BrbUnityAdapter = 2
    }

    public static class ManifoldProtocol
    {
        public const string CommandEnvelopeSchema = "rusty.manifold.command.envelope.v1";
        public const string CommandAckSchema = "rusty.manifold.command.ack.v1";
        public const string CommandRejectionSchema = "rusty.manifold.command.rejection.v1";
        public const string StreamRegistrySnapshotSchema = "rusty.manifold.stream.registry_snapshot.v1";
        public const string StreamManifestSchema = "rusty.manifold.stream.manifest.v1";
        public const string StreamSubscriptionRequestSchema = "rusty.manifold.stream.subscription_request.v1";
        public const string StreamSubscriptionSchema = "rusty.manifold.stream.subscription.v1";
        public const string StreamSubscriptionRejectionSchema = "rusty.manifold.stream.subscription_rejection.v1";
        public const string ScalarF32SampleSchema = "rusty.manifold.sample.scalar_f32.v1";
        public const string EmptyCommandInputSchema = "rusty.manifold.command.input.empty.v1";
        public const string StreamSubscribeCapability = "manifold.stream.subscribe";
        public const string ModuleControlCapability = "manifold.module.control";
        public const string DefaultUnityHolderId = "holder.unity.brb";
        public const string DefaultUnitySubscriberId = "subscriber.unity.brb";
        public const string DefaultTransportId = "transport.in_process";
        public const string SyntheticWaveStreamId = "stream.synthetic_wave";
        public const string BrbButtonDriveStreamId = "stream.brb.button_drive";
        public const string BrbButtonDriveSampleSchema = "brb.manifold.sample.button_drive.v1";

        public static string BuildCommandEnvelopeJson(
            string requestId,
            string commandId,
            string targetId,
            string targetScope,
            string inputSchema,
            int expectedRevision,
            string requiredCapability,
            string leaseId,
            string safetyClass,
            string holderId,
            long requestedAtMs)
        {
            var builder = new StringBuilder(384);
            builder.Append('{');
            AppendStringField(builder, "$schema", CommandEnvelopeSchema, true);
            AppendStringField(builder, "request_id", ValueOrGeneratedRequestId(requestId, "command"));
            AppendStringField(builder, "command_id", commandId);
            AppendStringField(builder, "target_id", targetId);
            AppendStringField(builder, "target_scope", targetScope);
            AppendStringField(builder, "input_schema", string.IsNullOrWhiteSpace(inputSchema) ? EmptyCommandInputSchema : inputSchema);
            AppendIntOrNullField(builder, "expected_revision", expectedRevision);
            AppendStringField(builder, "required_capability", requiredCapability);
            AppendStringOrNullField(builder, "lease_id", leaseId);
            AppendRawField(builder, "preconditions", "[]");
            AppendStringField(builder, "safety_class", string.IsNullOrWhiteSpace(safetyClass) ? "bounded_mutation" : safetyClass);
            AppendLongField(builder, "requested_at_ms", requestedAtMs > 0L ? requestedAtMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            AppendStringField(builder, "holder_id", string.IsNullOrWhiteSpace(holderId) ? DefaultUnityHolderId : holderId);
            builder.Append('}');
            return builder.ToString();
        }

        public static string BuildStreamSubscriptionRequestJson(
            string requestId,
            string subscriberId,
            string subscriberKind,
            int expectedAuthorityRevision,
            int expectedRegistryRevision,
            string streamId,
            string transportId,
            int requestedTtlMs,
            string requiredCapability,
            long requestedAtMs)
        {
            var builder = new StringBuilder(384);
            builder.Append('{');
            AppendStringField(builder, "$schema", StreamSubscriptionRequestSchema, true);
            AppendStringField(builder, "request_id", ValueOrGeneratedRequestId(requestId, "stream_subscription"));
            AppendStringField(builder, "subscriber_id", string.IsNullOrWhiteSpace(subscriberId) ? DefaultUnitySubscriberId : subscriberId);
            AppendStringField(builder, "subscriber_kind", string.IsNullOrWhiteSpace(subscriberKind) ? "ui" : subscriberKind);
            AppendIntField(builder, "expected_authority_revision", Math.Max(0, expectedAuthorityRevision));
            AppendIntField(builder, "expected_registry_revision", Math.Max(0, expectedRegistryRevision));
            AppendStringField(builder, "stream_id", streamId);
            AppendStringField(builder, "transport_id", string.IsNullOrWhiteSpace(transportId) ? DefaultTransportId : transportId);
            AppendIntField(builder, "requested_ttl_ms", Math.Max(1, requestedTtlMs));
            AppendStringField(builder, "required_capability", string.IsNullOrWhiteSpace(requiredCapability) ? StreamSubscribeCapability : requiredCapability);
            AppendLongField(builder, "requested_at_ms", requestedAtMs > 0L ? requestedAtMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            builder.Append('}');
            return builder.ToString();
        }

        public static bool TryParseCommandEnvelope(string json, out ManifoldCommandEnvelope envelope)
        {
            envelope = null;
            if (!TryParseJsonUtility(json, out envelope))
            {
                return false;
            }

            return envelope.schema == CommandEnvelopeSchema &&
                   !string.IsNullOrWhiteSpace(envelope.request_id) &&
                   !string.IsNullOrWhiteSpace(envelope.command_id) &&
                   !string.IsNullOrWhiteSpace(envelope.target_scope) &&
                   !string.IsNullOrWhiteSpace(envelope.required_capability) &&
                   !string.IsNullOrWhiteSpace(envelope.holder_id);
        }

        public static bool TryParseCommandAck(string json, out ManifoldCommandAck ack)
        {
            ack = null;
            if (!TryParseJsonUtility(json, out ack))
            {
                return false;
            }

            return ack.schema == CommandAckSchema &&
                   !string.IsNullOrWhiteSpace(ack.request_id) &&
                   !string.IsNullOrWhiteSpace(ack.authority_id);
        }

        public static bool TryParseCommandRejection(string json, out ManifoldCommandRejection rejection)
        {
            rejection = null;
            if (!TryParseJsonUtility(json, out rejection))
            {
                return false;
            }

            return rejection.schema == CommandRejectionSchema &&
                   !string.IsNullOrWhiteSpace(rejection.request_id) &&
                   !string.IsNullOrWhiteSpace(rejection.rejection_code);
        }

        public static bool TryParseStreamSubscriptionRequest(string json, out ManifoldStreamSubscriptionRequest request)
        {
            request = null;
            if (!TryParseJsonUtility(json, out request))
            {
                return false;
            }

            return request.schema == StreamSubscriptionRequestSchema &&
                   !string.IsNullOrWhiteSpace(request.request_id) &&
                   !string.IsNullOrWhiteSpace(request.subscriber_id) &&
                   !string.IsNullOrWhiteSpace(request.stream_id) &&
                   !string.IsNullOrWhiteSpace(request.transport_id);
        }

        public static bool TryParseStreamRegistrySnapshot(string json, out ManifoldStreamRegistrySnapshot snapshot)
        {
            snapshot = null;
            if (!TryParseJsonUtility(json, out snapshot))
            {
                return false;
            }

            return snapshot.schema == StreamRegistrySnapshotSchema &&
                   snapshot.registry_revision >= 0 &&
                   snapshot.streams != null;
        }

        public static ManifoldContractLane ClassifySchema(string schemaId)
        {
            if (string.IsNullOrWhiteSpace(schemaId))
            {
                return ManifoldContractLane.Unknown;
            }

            if (schemaId.StartsWith("rusty.manifold.", StringComparison.Ordinal))
            {
                return ManifoldContractLane.Manifold;
            }

            if (schemaId.StartsWith("brb.manifold.", StringComparison.Ordinal))
            {
                return ManifoldContractLane.BrbUnityAdapter;
            }

            return ManifoldContractLane.Unknown;
        }

        static bool TryParseJsonUtility<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(NormalizeSchemaKey(json));
                return value != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static string NormalizeSchemaKey(string json) => json.Replace("\"$schema\"", "\"schema\"");

        static string ValueOrGeneratedRequestId(string value, string prefix) =>
            string.IsNullOrWhiteSpace(value) ? $"request.unity.brb.{prefix}.{Guid.NewGuid():N}" : value;

        static void AppendStringField(StringBuilder builder, string name, string value, bool first = false)
        {
            if (!first)
            {
                builder.Append(',');
            }

            AppendQuoted(builder, name);
            builder.Append(':');
            AppendQuoted(builder, value ?? string.Empty);
        }

        static void AppendStringOrNullField(StringBuilder builder, string name, string value)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            if (string.IsNullOrWhiteSpace(value))
            {
                builder.Append("null");
            }
            else
            {
                AppendQuoted(builder, value);
            }
        }

        static void AppendRawField(StringBuilder builder, string name, string rawJson)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            builder.Append(rawJson);
        }

        static void AppendIntField(StringBuilder builder, string name, int value)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            builder.Append(value);
        }

        static void AppendIntOrNullField(StringBuilder builder, string name, int value)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            if (value <= 0)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append(value);
            }
        }

        static void AppendLongField(StringBuilder builder, string name, long value)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            builder.Append(value);
        }

        static void AppendQuoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var character = value[i];
                    switch (character)
                    {
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            builder.Append(character);
                            break;
                    }
                }
            }

            builder.Append('"');
        }
    }

    [Serializable]
    public sealed class ManifoldCommandEnvelope
    {
        public string schema;
        public string request_id;
        public string command_id;
        public string target_id;
        public string target_scope;
        public string input_schema;
        public int expected_revision;
        public string required_capability;
        public string lease_id;
        public ManifoldCommandPrecondition[] preconditions;
        public string safety_class;
        public long requested_at_ms;
        public string holder_id;
    }

    [Serializable]
    public sealed class ManifoldCommandPrecondition
    {
        public string precondition_id;
        public string message;
    }

    [Serializable]
    public sealed class ManifoldCommandAck
    {
        public string schema;
        public string request_id;
        public int accepted_revision;
        public string lease_id;
        public string authority_id;
        public long accepted_at_ms;
    }

    [Serializable]
    public sealed class ManifoldCommandRejection
    {
        public string schema;
        public string request_id;
        public string rejection_code;
        public string message;
        public bool retryable;
        public int current_revision;
    }

    [Serializable]
    public sealed class ManifoldStreamSubscriptionRequest
    {
        public string schema;
        public string request_id;
        public string subscriber_id;
        public string subscriber_kind;
        public int expected_authority_revision;
        public int expected_registry_revision;
        public string stream_id;
        public string transport_id;
        public int requested_ttl_ms;
        public string required_capability;
        public long requested_at_ms;
    }

    [Serializable]
    public sealed class ManifoldStreamRegistrySnapshot
    {
        public string schema;
        public int registry_revision;
        public ManifoldStreamManifest[] streams;
    }

    [Serializable]
    public sealed class ManifoldStreamManifest
    {
        public string schema;
        public string stream_id;
        public string source_module_id;
        public string semantic_family;
        public string sample_schema;
        public string rate_class;
        public string[] timestamp_domains;
        public ManifoldRetentionPolicy retention;
        public string sensitivity;
        public ManifoldTransportOffer[] transport_offers;
        public ManifoldSubscriptionPolicy subscription;
    }

    [Serializable]
    public sealed class ManifoldRetentionPolicy
    {
        public string policy;
    }

    [Serializable]
    public sealed class ManifoldTransportOffer
    {
        public string transport_id;
        public string transport;
        public string endpoint_id;
    }

    [Serializable]
    public sealed class ManifoldSubscriptionPolicy
    {
        public bool ui_subscribable;
        public int max_subscribers;
    }
}

using System;
using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    public readonly struct RustyXrBrokerBioSignalSample
    {
        public RustyXrBrokerBioSignalSample(
            string streamId,
            string payloadSchema,
            long sequenceId,
            long sourceTimeUnixNs,
            long brokerTimeUnixNs,
            float value01,
            string sourceLabel)
        {
            StreamId = streamId ?? string.Empty;
            PayloadSchema = payloadSchema ?? string.Empty;
            SequenceId = sequenceId;
            SourceTimeUnixNs = sourceTimeUnixNs;
            BrokerTimeUnixNs = brokerTimeUnixNs;
            Value01 = Mathf.Clamp01(value01);
            SourceLabel = sourceLabel ?? string.Empty;
        }

        public string StreamId { get; }
        public string PayloadSchema { get; }
        public long SequenceId { get; }
        public long SourceTimeUnixNs { get; }
        public long BrokerTimeUnixNs { get; }
        public float Value01 { get; }
        public string SourceLabel { get; }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-17)]
    public sealed class RustyXrBrokerBioSignalReceiver : MonoBehaviour
    {
        public const string PolarHeartRateStream = "bio:polar_hr_rr";
        public const string PolarEcgStream = "bio:polar_ecg";
        public const string PolarAccStream = "bio:polar_acc";
        public const string BreathStream = "bio:breath";

        static readonly string[] BuiltInStreams =
        {
            PolarHeartRateStream,
            PolarEcgStream,
            PolarAccStream,
            BreathStream
        };

        [SerializeField] string[] streamIds =
        {
            PolarHeartRateStream,
            PolarEcgStream,
            PolarAccStream,
            BreathStream
        };

        string _lastStreamId = string.Empty;
        string _lastPayloadSchema = string.Empty;
        string _lastSourceLabel = string.Empty;
        long _lastSequenceId;
        long _lastSourceTimeUnixNs;
        long _lastBrokerTimeUnixNs;
        float _lastValue01;

        public event Action<RustyXrBrokerBioSignalSample> BioSignalReceived;

        public string LastStreamId => _lastStreamId;
        public string LastPayloadSchema => _lastPayloadSchema;
        public string LastSourceLabel => _lastSourceLabel;
        public long LastSequenceId => _lastSequenceId;
        public long LastSourceTimeUnixNs => _lastSourceTimeUnixNs;
        public long LastBrokerTimeUnixNs => _lastBrokerTimeUnixNs;
        public float LastValue01 => _lastValue01;

        public void ConfigureStreams(params string[] streams)
        {
            if (streams == null || streams.Length == 0)
            {
                streamIds = Array.Empty<string>();
                return;
            }

            streamIds = streams;
        }

        public bool ApplyStreamEventJson(string json)
        {
            if (!RustyXrBrokerProtocol.TryParseStreamEvent(json, out var streamEvent))
            {
                return false;
            }

            return ApplyStreamEvent(streamEvent);
        }

        public bool ApplyStreamEvent(RustyXrBrokerStreamEvent streamEvent)
        {
            if (streamEvent == null || string.IsNullOrWhiteSpace(streamEvent.stream) || !AcceptsStream(streamEvent.stream))
            {
                return false;
            }

            var payload = streamEvent.payload;
            var payloadSchema = !string.IsNullOrWhiteSpace(streamEvent.payload_schema)
                ? streamEvent.payload_schema
                : payload != null ? payload.schema : string.Empty;
            var sourceTimeUnixNs = ResolveSourceTimeUnixNs(streamEvent, payload);
            var brokerTimeUnixNs = ResolveBrokerTimeUnixNs(streamEvent, payload);
            var value01 = ResolveValue01(streamEvent.stream, payload);
            var sourceLabel = ResolveSourceLabel(streamEvent.stream, payload);

            _lastStreamId = streamEvent.stream;
            _lastPayloadSchema = payloadSchema;
            _lastSourceLabel = sourceLabel;
            _lastSequenceId = streamEvent.sequence_id;
            _lastSourceTimeUnixNs = sourceTimeUnixNs;
            _lastBrokerTimeUnixNs = brokerTimeUnixNs;
            _lastValue01 = value01;

            BioSignalReceived?.Invoke(new RustyXrBrokerBioSignalSample(
                _lastStreamId,
                _lastPayloadSchema,
                _lastSequenceId,
                _lastSourceTimeUnixNs,
                _lastBrokerTimeUnixNs,
                _lastValue01,
                _lastSourceLabel));

            return true;
        }

        public static bool IsPolarHeartRateStream(string stream) =>
            string.Equals(stream, PolarHeartRateStream, StringComparison.Ordinal);

        public static bool IsPolarPmdStream(string stream) =>
            string.Equals(stream, PolarEcgStream, StringComparison.Ordinal) ||
            string.Equals(stream, PolarAccStream, StringComparison.Ordinal);

        public static bool IsBreathStream(string stream) =>
            string.Equals(stream, BreathStream, StringComparison.Ordinal);

        bool AcceptsStream(string stream)
        {
            var configuredStreams = streamIds == null || streamIds.Length == 0 ? BuiltInStreams : streamIds;
            for (var i = 0; i < configuredStreams.Length; i++)
            {
                if (string.Equals(stream, configuredStreams[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static long ResolveSourceTimeUnixNs(RustyXrBrokerStreamEvent streamEvent, RustyXrBrokerStreamPayload payload)
        {
            if (streamEvent.source_time_unix_ns > 0L)
            {
                return streamEvent.source_time_unix_ns;
            }

            if (payload != null && payload.sample_time_unix_ns > 0L)
            {
                return payload.sample_time_unix_ns;
            }

            return 0L;
        }

        static long ResolveBrokerTimeUnixNs(RustyXrBrokerStreamEvent streamEvent, RustyXrBrokerStreamPayload payload)
        {
            if (streamEvent.broker_time_unix_ns > 0L)
            {
                return streamEvent.broker_time_unix_ns;
            }

            if (payload == null)
            {
                return 0L;
            }

            if (payload.broker_publish_time_unix_ns > 0L)
            {
                return payload.broker_publish_time_unix_ns;
            }

            return payload.broker_receive_time_unix_ns > 0L ? payload.broker_receive_time_unix_ns : 0L;
        }

        static float ResolveValue01(string stream, RustyXrBrokerStreamPayload payload)
        {
            if (payload == null)
            {
                return 0f;
            }

            if (IsBreathStream(stream))
            {
                return Mathf.Clamp01(payload.has_volume || payload.volume01 > 0f ? payload.volume01 : payload.value01);
            }

            if (IsPolarHeartRateStream(stream))
            {
                return payload.heart_rate_bpm > 0f
                    ? Mathf.Clamp01(payload.heart_rate_bpm / 220f)
                    : Mathf.Clamp01(payload.value01);
            }

            if (IsPolarPmdStream(stream))
            {
                if (payload.value01 > 0f)
                {
                    return Mathf.Clamp01(payload.value01);
                }

                if (payload.sample_count > 0)
                {
                    return Mathf.Clamp01(payload.sample_count / 64f);
                }

                if (payload.payload_size_bytes > 0)
                {
                    return Mathf.Clamp01(payload.payload_size_bytes / 512f);
                }

                return 1f;
            }

            return Mathf.Clamp01(payload.value01);
        }

        static string ResolveSourceLabel(string stream, RustyXrBrokerStreamPayload payload)
        {
            if (payload != null)
            {
                if (!string.IsNullOrWhiteSpace(payload.source))
                {
                    return payload.source;
                }

                if (!string.IsNullOrWhiteSpace(payload.source_detail))
                {
                    return payload.source_detail;
                }
            }

            return stream;
        }
    }
}

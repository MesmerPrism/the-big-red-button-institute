using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    public sealed class RustyXrBrokerScreenGazeReceiver : MonoBehaviour
    {
        public const string DefaultStream = "eye.screen.gaze_point";

        [SerializeField] string streamId = DefaultStream;
        [SerializeField] Vector2 normalizedPoint = new(0.5f, 0.5f);
        [SerializeField] bool sampleValid;
        [SerializeField] float confidence01;

        long _lastSequenceNumber;
        long _lastSampleTimeNs;
        long _lastBrokerTimeUnixNs;
        string _lastProviderId = string.Empty;
        string _lastSourceDeviceId = string.Empty;

        public string StreamId
        {
            get => streamId;
            set => streamId = string.IsNullOrWhiteSpace(value) ? DefaultStream : value;
        }

        public Vector2 NormalizedPoint => normalizedPoint;
        public bool SampleValid => sampleValid;
        public float Confidence01 => confidence01;
        public long LastSequenceNumber => _lastSequenceNumber;
        public long LastSampleTimeNs => _lastSampleTimeNs;
        public long LastBrokerTimeUnixNs => _lastBrokerTimeUnixNs;
        public string LastProviderId => _lastProviderId;
        public string LastSourceDeviceId => _lastSourceDeviceId;

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
            if (!RustyXrBrokerScreenGaze.TryExtract(streamEvent, streamId, out var sample))
            {
                return false;
            }

            normalizedPoint = sample.NormalizedPoint;
            sampleValid = sample.SampleValid;
            confidence01 = sample.Confidence01;
            _lastSequenceNumber = sample.SequenceNumber;
            _lastSampleTimeNs = sample.SampleTimeNs;
            _lastBrokerTimeUnixNs = sample.BrokerTimeUnixNs;
            _lastProviderId = sample.ProviderId;
            _lastSourceDeviceId = sample.SourceDeviceId;
            return true;
        }
    }

    public readonly struct RustyXrBrokerScreenGazeSample
    {
        public RustyXrBrokerScreenGazeSample(
            Vector2 normalizedPoint,
            bool sampleValid,
            float confidence01,
            long sequenceNumber,
            long sampleTimeNs,
            long brokerTimeUnixNs,
            string providerId,
            string sourceDeviceId)
        {
            NormalizedPoint = normalizedPoint;
            SampleValid = sampleValid;
            Confidence01 = confidence01;
            SequenceNumber = sequenceNumber;
            SampleTimeNs = sampleTimeNs;
            BrokerTimeUnixNs = brokerTimeUnixNs;
            ProviderId = providerId ?? string.Empty;
            SourceDeviceId = sourceDeviceId ?? string.Empty;
        }

        public Vector2 NormalizedPoint { get; }
        public bool SampleValid { get; }
        public float Confidence01 { get; }
        public long SequenceNumber { get; }
        public long SampleTimeNs { get; }
        public long BrokerTimeUnixNs { get; }
        public string ProviderId { get; }
        public string SourceDeviceId { get; }
    }

    public static class RustyXrBrokerScreenGaze
    {
        public static bool TryExtract(
            RustyXrBrokerStreamEvent streamEvent,
            string expectedStream,
            out RustyXrBrokerScreenGazeSample sample)
        {
            sample = default;
            if (streamEvent == null ||
                streamEvent.payload == null ||
                streamEvent.stream != expectedStream ||
                streamEvent.payload.normalized_point == null)
            {
                return false;
            }

            var schema = !string.IsNullOrWhiteSpace(streamEvent.payload_schema)
                ? streamEvent.payload_schema
                : streamEvent.payload.schema;
            if (!string.IsNullOrWhiteSpace(schema) &&
                schema != RustyXrBrokerProtocol.EyeScreenGazePointSchema)
            {
                return false;
            }

            var point = new Vector2(
                Clamp01Finite(streamEvent.payload.normalized_point.x),
                Clamp01Finite(streamEvent.payload.normalized_point.y));
            var sampleBase = streamEvent.payload.@base;
            var sequenceNumber = sampleBase != null && sampleBase.sequence_number > 0L
                ? sampleBase.sequence_number
                : streamEvent.sequence_id;
            var sampleTimeNs = sampleBase != null && sampleBase.sample_time_ns > 0L
                ? sampleBase.sample_time_ns
                : streamEvent.source_time_ns;
            var brokerTimeUnixNs = streamEvent.broker_time_unix_ns;
            var confidence = sampleBase != null ? Clamp01Finite(sampleBase.confidence) : 0f;
            var validity = sampleBase != null ? sampleBase.validity : null;
            var sampleValid = validity == null || (validity.sample_valid && !validity.tracking_lost);

            sample = new RustyXrBrokerScreenGazeSample(
                point,
                sampleValid,
                confidence,
                sequenceNumber,
                sampleTimeNs,
                brokerTimeUnixNs,
                sampleBase != null ? sampleBase.provider_id : string.Empty,
                sampleBase != null ? sampleBase.source_device_id : string.Empty);
            return true;
        }

        static float Clamp01Finite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp01(value);
        }
    }
}

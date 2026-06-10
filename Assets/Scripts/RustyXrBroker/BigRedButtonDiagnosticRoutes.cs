using System;
using System.Collections.Generic;
using System.Text;

namespace TheBigRedButtonInstitute.Diagnostics
{
    public enum BigRedButtonDiagnosticRouteId
    {
        ManualHandOrController = 0,
        DirectUnityBlePolar = 10,
        DirectUnityBlePolarHeartRate = 11,
        DirectUnityBlePolarPmd = 12,
        DirectUnityOsc = 20,
        DirectUnityLsl = 30,
        BrokerWebSocketOsc = 40,
        BrokerWebSocketLsl = 50,
        BrokerWebSocketSynthetic = 60,
        BrokerWebSocketPolarHeartRate = 70,
        BrokerWebSocketPolarPmd = 71,
        BrokerWebSocketBreath = 72
    }

    public readonly struct BigRedButtonDiagnosticSample
    {
        public BigRedButtonDiagnosticSample(
            long sequenceId,
            float value01,
            long sourceTimeUnixNs,
            long brokerTimeUnixNs,
            long unityTimeUnixNs,
            string sourceLabel)
        {
            SequenceId = sequenceId;
            Value01 = value01;
            SourceTimeUnixNs = sourceTimeUnixNs;
            BrokerTimeUnixNs = brokerTimeUnixNs;
            UnityTimeUnixNs = unityTimeUnixNs;
            SourceLabel = sourceLabel ?? string.Empty;
        }

        public long SequenceId { get; }
        public float Value01 { get; }
        public long SourceTimeUnixNs { get; }
        public long BrokerTimeUnixNs { get; }
        public long UnityTimeUnixNs { get; }
        public string SourceLabel { get; }
    }

    public sealed class BigRedButtonDiagnosticRouteStats
    {
        double _sourceLatencyTotalMs;
        double _brokerLatencyTotalMs;
        int _sourceLatencySamples;
        int _brokerLatencySamples;
        bool _hasLastSequence;

        public BigRedButtonDiagnosticRouteStats(BigRedButtonDiagnosticRouteId routeId)
        {
            RouteId = routeId;
        }

        public BigRedButtonDiagnosticRouteId RouteId { get; }
        public long ReceivedSamples { get; private set; }
        public long AcceptedPulses { get; private set; }
        public long DroppedSamples { get; private set; }
        public long DuplicateOrOutOfOrderSamples { get; private set; }
        public long LastSequenceId { get; private set; }
        public float LastValue01 { get; private set; }
        public long LastUnityTimeUnixNs { get; private set; }
        public double LastSourceToUnityLatencyMs { get; private set; }
        public double AverageSourceToUnityLatencyMs => _sourceLatencySamples > 0 ? _sourceLatencyTotalMs / _sourceLatencySamples : 0d;
        public double LastBrokerToUnityLatencyMs { get; private set; }
        public double AverageBrokerToUnityLatencyMs => _brokerLatencySamples > 0 ? _brokerLatencyTotalMs / _brokerLatencySamples : 0d;

        public void RecordSample(BigRedButtonDiagnosticSample sample, bool acceptedPulse)
        {
            var unityTimeUnixNs = sample.UnityTimeUnixNs > 0 ? sample.UnityTimeUnixNs : UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            ReceivedSamples++;
            if (acceptedPulse)
            {
                AcceptedPulses++;
            }

            if (sample.SequenceId > 0)
            {
                if (_hasLastSequence)
                {
                    if (sample.SequenceId == LastSequenceId)
                    {
                        DuplicateOrOutOfOrderSamples++;
                    }
                    else if (sample.SequenceId < LastSequenceId)
                    {
                        DuplicateOrOutOfOrderSamples++;
                    }
                    else if (sample.SequenceId > LastSequenceId + 1)
                    {
                        DroppedSamples += sample.SequenceId - LastSequenceId - 1;
                    }
                }

                _hasLastSequence = true;
                LastSequenceId = sample.SequenceId;
            }

            LastValue01 = Clamp01(sample.Value01);
            LastUnityTimeUnixNs = unityTimeUnixNs;

            if (sample.SourceTimeUnixNs > 0 && unityTimeUnixNs >= sample.SourceTimeUnixNs)
            {
                LastSourceToUnityLatencyMs = NanosecondsToMilliseconds(unityTimeUnixNs - sample.SourceTimeUnixNs);
                _sourceLatencyTotalMs += LastSourceToUnityLatencyMs;
                _sourceLatencySamples++;
            }

            if (sample.BrokerTimeUnixNs > 0 && unityTimeUnixNs >= sample.BrokerTimeUnixNs)
            {
                LastBrokerToUnityLatencyMs = NanosecondsToMilliseconds(unityTimeUnixNs - sample.BrokerTimeUnixNs);
                _brokerLatencyTotalMs += LastBrokerToUnityLatencyMs;
                _brokerLatencySamples++;
            }
        }

        public void Reset()
        {
            ReceivedSamples = 0;
            AcceptedPulses = 0;
            DroppedSamples = 0;
            DuplicateOrOutOfOrderSamples = 0;
            LastSequenceId = 0;
            LastValue01 = 0f;
            LastUnityTimeUnixNs = 0;
            LastSourceToUnityLatencyMs = 0d;
            LastBrokerToUnityLatencyMs = 0d;
            _sourceLatencyTotalMs = 0d;
            _brokerLatencyTotalMs = 0d;
            _sourceLatencySamples = 0;
            _brokerLatencySamples = 0;
            _hasLastSequence = false;
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        static double NanosecondsToMilliseconds(long nanoseconds) => nanoseconds / 1_000_000d;

        static long UnixTimeNanoseconds(DateTimeOffset value)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return (value.ToUniversalTime().Ticks - epoch.Ticks) * 100L;
        }
    }

    public sealed class BigRedButtonDiagnosticRouteTable
    {
        readonly Dictionary<BigRedButtonDiagnosticRouteId, BigRedButtonDiagnosticRouteStats> _routes = new();

        public BigRedButtonDiagnosticRouteStats GetStats(BigRedButtonDiagnosticRouteId routeId)
        {
            if (!_routes.TryGetValue(routeId, out var stats))
            {
                stats = new BigRedButtonDiagnosticRouteStats(routeId);
                _routes.Add(routeId, stats);
            }

            return stats;
        }

        public void RecordSample(BigRedButtonDiagnosticRouteId routeId, BigRedButtonDiagnosticSample sample, bool acceptedPulse)
        {
            GetStats(routeId).RecordSample(sample, acceptedPulse);
        }

        public IReadOnlyCollection<BigRedButtonDiagnosticRouteStats> Routes => _routes.Values;

        public void Reset()
        {
            foreach (var route in _routes.Values)
            {
                route.Reset();
            }
        }

        public string BuildCompactSummary()
        {
            var builder = new StringBuilder();
            foreach (var route in _routes.Values)
            {
                if (route.ReceivedSamples <= 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(" | ");
                }

                builder
                    .Append(RouteLabel(route.RouteId))
                    .Append(": rx ")
                    .Append(route.ReceivedSamples)
                    .Append(" pulse ")
                    .Append(route.AcceptedPulses)
                    .Append(" drop ")
                    .Append(route.DroppedSamples);
            }

            return builder.Length > 0 ? builder.ToString() : "no diagnostic samples";
        }

        public static string RouteLabel(BigRedButtonDiagnosticRouteId routeId)
        {
            return routeId switch
            {
                BigRedButtonDiagnosticRouteId.ManualHandOrController => "manual",
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolar => "direct BLE/Polar button",
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarHeartRate => "direct BLE/Polar HR",
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarPmd => "direct BLE/Polar PMD",
                BigRedButtonDiagnosticRouteId.DirectUnityOsc => "direct OSC",
                BigRedButtonDiagnosticRouteId.DirectUnityLsl => "direct LSL",
                BigRedButtonDiagnosticRouteId.BrokerWebSocketOsc => "broker OSC/WebSocket",
                BigRedButtonDiagnosticRouteId.BrokerWebSocketLsl => "broker LSL/WebSocket",
                BigRedButtonDiagnosticRouteId.BrokerWebSocketSynthetic => "broker synthetic/WebSocket",
                BigRedButtonDiagnosticRouteId.BrokerWebSocketPolarHeartRate => "broker Polar HR/WebSocket",
                BigRedButtonDiagnosticRouteId.BrokerWebSocketPolarPmd => "broker Polar PMD/WebSocket",
                BigRedButtonDiagnosticRouteId.BrokerWebSocketBreath => "broker breath/WebSocket",
                _ => routeId.ToString()
            };
        }
    }
}

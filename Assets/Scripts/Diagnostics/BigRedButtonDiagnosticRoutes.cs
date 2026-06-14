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
        DirectUnityLsl = 30
    }

    public readonly struct BigRedButtonDiagnosticSample
    {
        public BigRedButtonDiagnosticSample(
            long sequenceId,
            float value01,
            long sourceTimeUnixNs,
            long authorityTimeUnixNs,
            long unityTimeUnixNs,
            string sourceLabel)
        {
            SequenceId = sequenceId;
            Value01 = value01;
            SourceTimeUnixNs = sourceTimeUnixNs;
            AuthorityTimeUnixNs = authorityTimeUnixNs;
            UnityTimeUnixNs = unityTimeUnixNs;
            SourceLabel = sourceLabel ?? string.Empty;
        }

        public long SequenceId { get; }
        public float Value01 { get; }
        public long SourceTimeUnixNs { get; }
        public long AuthorityTimeUnixNs { get; }
        public long UnityTimeUnixNs { get; }
        public string SourceLabel { get; }
    }

    public sealed class BigRedButtonDiagnosticRouteStats
    {
        double _sourceLatencyTotalMs;
        double _authorityLatencyTotalMs;
        int _sourceLatencySamples;
        int _authorityLatencySamples;
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
        public double LastAuthorityToUnityLatencyMs { get; private set; }
        public double AverageAuthorityToUnityLatencyMs => _authorityLatencySamples > 0 ? _authorityLatencyTotalMs / _authorityLatencySamples : 0d;

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

            if (sample.AuthorityTimeUnixNs > 0 && unityTimeUnixNs >= sample.AuthorityTimeUnixNs)
            {
                LastAuthorityToUnityLatencyMs = NanosecondsToMilliseconds(unityTimeUnixNs - sample.AuthorityTimeUnixNs);
                _authorityLatencyTotalMs += LastAuthorityToUnityLatencyMs;
                _authorityLatencySamples++;
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
            LastAuthorityToUnityLatencyMs = 0d;
            _sourceLatencyTotalMs = 0d;
            _authorityLatencyTotalMs = 0d;
            _sourceLatencySamples = 0;
            _authorityLatencySamples = 0;
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
                _ => routeId.ToString()
            };
        }
    }
}

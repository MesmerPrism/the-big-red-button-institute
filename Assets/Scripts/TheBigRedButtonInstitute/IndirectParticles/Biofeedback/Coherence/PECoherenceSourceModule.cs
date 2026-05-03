using System;
using TheBigRedButtonInstitute.IndirectParticles;
using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Coherence
{
    public abstract class PECoherenceSourceModule : MonoBehaviour, IPECoherenceSignalModule
    {
        [Header("Module Identity")]
        [SerializeField] private string moduleId = string.Empty;

        [Header("Output Signals")]
        [SerializeField] private bool publishToSignalRegistry = true;
        [SerializeField] private string coherenceSignalName = string.Empty;
        [SerializeField] private string stateSignalName = string.Empty;
        [SerializeField] private string trackingSignalName = string.Empty;
        [SerializeField] private string confidenceSignalName = string.Empty;

        [Header("Fallback")]
        [SerializeField] private bool emitFallbackWhenUnavailable = true;
        [Range(0f, 1f)]
        [SerializeField] private float fallbackCoherence01 = 0.5f;

        public event Action<PECoherenceSample> SampleUpdated;
        public event Action<float> ValueChanged;

        public string ModuleId => string.IsNullOrWhiteSpace(moduleId) ? name : moduleId.Trim();
        public bool IsProcessingEnabled { get; private set; } = true;
        public bool IsConnected { get; private set; }
        public bool HasSample { get; private set; }
        public PECoherenceTrackingState TrackingState { get; private set; } = PECoherenceTrackingState.Unavailable;
        public float CurrentCoherence01 { get; private set; }
        public float Confidence01 { get; private set; }
        public float CurrentHeartbeatBpm { get; private set; }
        public float CurrentHeartbeatIbiMs { get; private set; }

        protected bool EmitFallbackWhenUnavailable => emitFallbackWhenUnavailable;
        protected float FallbackCoherence01 => Mathf.Clamp01(fallbackCoherence01);

        public void SetModuleIdIfEmpty(string value)
        {
            if (!string.IsNullOrWhiteSpace(moduleId))
                return;

            if (string.IsNullOrWhiteSpace(value))
                return;

            moduleId = value.Trim();
        }

        protected void SetOutputSignalNamesIfEmpty(
            string coherenceSignal,
            string stateSignal,
            string trackingSignal,
            string confidenceSignal)
        {
            if (string.IsNullOrWhiteSpace(coherenceSignalName))
                coherenceSignalName = NormalizeSignalName(coherenceSignal);
            if (string.IsNullOrWhiteSpace(stateSignalName))
                stateSignalName = NormalizeSignalName(stateSignal);
            if (string.IsNullOrWhiteSpace(trackingSignalName))
                trackingSignalName = NormalizeSignalName(trackingSignal);
            if (string.IsNullOrWhiteSpace(confidenceSignalName))
                confidenceSignalName = NormalizeSignalName(confidenceSignal);
        }

        public void SetProcessingEnabled(bool enabled)
        {
            if (IsProcessingEnabled == enabled)
                return;

            IsProcessingEnabled = enabled;
            OnProcessingEnabledChanged(enabled);

            if (!enabled)
                PublishUnavailable(forceEvent: true);
        }

        protected virtual void OnProcessingEnabledChanged(bool enabled)
        {
        }

        protected void PublishUnavailable(bool forceEvent)
        {
            if (!emitFallbackWhenUnavailable)
            {
                PublishSample(
                    PECoherenceTrackingState.Unavailable,
                    isConnected: false,
                    hasSample: false,
                    coherence01: 0f,
                    confidence01: 0f,
                    heartbeatBpm: 0f,
                    heartbeatIbiMs: 0f,
                    forceEvent: forceEvent);
                return;
            }

            PublishSample(
                PECoherenceTrackingState.Unavailable,
                isConnected: false,
                hasSample: true,
                coherence01: FallbackCoherence01,
                confidence01: 0f,
                heartbeatBpm: 0f,
                heartbeatIbiMs: 0f,
                forceEvent: forceEvent);
        }

        protected void PublishSample(
            PECoherenceTrackingState trackingState,
            bool isConnected,
            bool hasSample,
            float coherence01,
            float confidence01,
            float heartbeatBpm,
            float heartbeatIbiMs,
            bool forceEvent = false)
        {
            float clampedCoherence = Mathf.Clamp01(coherence01);
            float clampedConfidence = Mathf.Clamp01(confidence01);
            float clampedBpm = Mathf.Clamp(heartbeatBpm, 0f, 260f);
            float clampedIbiMs = Mathf.Max(0f, heartbeatIbiMs);

            bool changed =
                forceEvent ||
                TrackingState != trackingState ||
                IsConnected != isConnected ||
                HasSample != hasSample ||
                Mathf.Abs(CurrentCoherence01 - clampedCoherence) >= 0.0001f ||
                Mathf.Abs(Confidence01 - clampedConfidence) >= 0.0001f ||
                Mathf.Abs(CurrentHeartbeatBpm - clampedBpm) >= 0.001f ||
                Mathf.Abs(CurrentHeartbeatIbiMs - clampedIbiMs) >= 0.001f;

            TrackingState = trackingState;
            IsConnected = isConnected;
            HasSample = hasSample;
            CurrentCoherence01 = clampedCoherence;
            Confidence01 = clampedConfidence;
            CurrentHeartbeatBpm = clampedBpm;
            CurrentHeartbeatIbiMs = clampedIbiMs;

            if (!changed)
                return;

            PECoherenceSample sample = new PECoherenceSample
            {
                Timestamp = Time.unscaledTimeAsDouble,
                ElapsedSeconds = Time.unscaledTime,
                ModuleId = ModuleId,
                TrackingState = TrackingState,
                IsConnected = IsConnected,
                HasSample = HasSample,
                Coherence01 = CurrentCoherence01,
                Confidence01 = Confidence01,
                HeartbeatBpm = CurrentHeartbeatBpm,
                HeartbeatIbiMs = CurrentHeartbeatIbiMs
            };

            if (HasSample)
                ValueChanged?.Invoke(CurrentCoherence01);
            SampleUpdated?.Invoke(sample);
            PublishSignals();
        }

        private void PublishSignals()
        {
            if (!publishToSignalRegistry)
                return;

            string coherenceKey = NormalizeSignalName(coherenceSignalName);
            if (!string.IsNullOrEmpty(coherenceKey))
                PEBiofeedbackSignalRegistry.Publish(coherenceKey, CurrentCoherence01);

            string stateKey = NormalizeSignalName(stateSignalName);
            if (!string.IsNullOrEmpty(stateKey))
                PEBiofeedbackSignalRegistry.Publish(stateKey, MapTrackingStateTo01(TrackingState));

            string trackingKey = NormalizeSignalName(trackingSignalName);
            if (!string.IsNullOrEmpty(trackingKey))
            {
                float tracking01 =
                    TrackingState == PECoherenceTrackingState.Tracking &&
                    HasSample &&
                    IsConnected
                        ? 1f
                        : 0f;
                PEBiofeedbackSignalRegistry.Publish(trackingKey, tracking01);
            }

            string confidenceKey = NormalizeSignalName(confidenceSignalName);
            if (!string.IsNullOrEmpty(confidenceKey))
                PEBiofeedbackSignalRegistry.Publish(confidenceKey, Confidence01);
        }

        private static float MapTrackingStateTo01(PECoherenceTrackingState trackingState)
        {
            switch (trackingState)
            {
                case PECoherenceTrackingState.Tracking:
                    return 1f;
                case PECoherenceTrackingState.Stale:
                    return 0.5f;
                case PECoherenceTrackingState.Unavailable:
                default:
                    return 0f;
            }
        }

        private static string NormalizeSignalName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

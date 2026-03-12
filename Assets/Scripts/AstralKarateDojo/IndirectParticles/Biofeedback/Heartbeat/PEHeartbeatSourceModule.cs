using System;
using AstralKarateDojo.IndirectParticles;
using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Heartbeat
{
    public abstract class PEHeartbeatSourceModule : MonoBehaviour, IPEHeartbeatSignalModule
    {
        [Header("Module Identity")]
        [SerializeField] private string moduleId = string.Empty;

        [Header("Output Signals")]
        [SerializeField] private bool publishToSignalRegistry = true;
        [SerializeField] private string pulseSignalName = string.Empty;
        [SerializeField] private string stateSignalName = string.Empty;
        [SerializeField] private string trackingSignalName = string.Empty;
        [SerializeField] private string beatSignalName = string.Empty;
        [SerializeField] private string realBeatSignalName = string.Empty;

        [Header("Fallback")]
        [SerializeField] private bool emitFallbackWhenUnavailable = true;
        [Range(0f, 1f)]
        [SerializeField] private float fallbackPulse01 = 0.2f;
        [Range(20f, 220f)]
        [SerializeField] private float fallbackBpm = 60f;

        public event Action<PEHeartbeatSample> SampleUpdated;
        public event Action<float> ValueChanged;

        public string ModuleId => string.IsNullOrWhiteSpace(moduleId) ? name : moduleId.Trim();
        public bool IsProcessingEnabled { get; private set; } = true;
        public bool IsConnected { get; private set; }
        public bool HasSample { get; private set; }
        public bool BeatDetectedThisFrame { get; private set; }
        public bool RealBeatDetectedThisFrame { get; private set; }
        public PEHeartbeatTrackingState TrackingState { get; private set; } = PEHeartbeatTrackingState.Unavailable;
        public float CurrentBpm { get; private set; }
        public float CurrentIbiMs { get; private set; }
        public float CurrentPulse01 { get; private set; }
        public float Confidence01 { get; private set; }

        protected bool EmitFallbackWhenUnavailable => emitFallbackWhenUnavailable;
        protected float FallbackPulse01 => Mathf.Clamp01(fallbackPulse01);
        protected float FallbackBpm => Mathf.Clamp(fallbackBpm, 20f, 220f);

        public void SetModuleIdIfEmpty(string value)
        {
            if (!string.IsNullOrWhiteSpace(moduleId))
                return;

            if (string.IsNullOrWhiteSpace(value))
                return;

            moduleId = value.Trim();
        }

        protected void SetOutputSignalNamesIfEmpty(
            string pulseSignal,
            string stateSignal,
            string trackingSignal,
            string beatSignal,
            string realBeatSignal)
        {
            if (string.IsNullOrWhiteSpace(pulseSignalName))
                pulseSignalName = NormalizeSignalName(pulseSignal);
            if (string.IsNullOrWhiteSpace(stateSignalName))
                stateSignalName = NormalizeSignalName(stateSignal);
            if (string.IsNullOrWhiteSpace(trackingSignalName))
                trackingSignalName = NormalizeSignalName(trackingSignal);
            if (string.IsNullOrWhiteSpace(beatSignalName))
                beatSignalName = NormalizeSignalName(beatSignal);
            if (string.IsNullOrWhiteSpace(realBeatSignalName))
                realBeatSignalName = NormalizeSignalName(realBeatSignal);
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
                    PEHeartbeatTrackingState.Unavailable,
                    isConnected: false,
                    hasSample: false,
                    beatDetectedThisFrame: false,
                    realBeatDetectedThisFrame: false,
                    bpm: 0f,
                    ibiMs: 0f,
                    pulse01: 0f,
                    confidence01: 0f,
                    forceEvent: forceEvent);
                return;
            }

            float ibiMs = FallbackBpm > 0f ? 60000f / FallbackBpm : 0f;
            PublishSample(
                PEHeartbeatTrackingState.Unavailable,
                isConnected: false,
                hasSample: true,
                beatDetectedThisFrame: false,
                realBeatDetectedThisFrame: false,
                bpm: FallbackBpm,
                ibiMs: ibiMs,
                pulse01: FallbackPulse01,
                confidence01: 0f,
                forceEvent: forceEvent);
        }

        protected void PublishSample(
            PEHeartbeatTrackingState trackingState,
            bool isConnected,
            bool hasSample,
            bool beatDetectedThisFrame,
            bool realBeatDetectedThisFrame,
            float bpm,
            float ibiMs,
            float pulse01,
            float confidence01,
            bool forceEvent = false)
        {
            float clampedBpm = Mathf.Clamp(bpm, 0f, 260f);
            float clampedIbiMs = Mathf.Max(0f, ibiMs);
            float clampedPulse = Mathf.Clamp01(pulse01);
            float clampedConfidence = Mathf.Clamp01(confidence01);

            bool changed =
                forceEvent ||
                TrackingState != trackingState ||
                IsConnected != isConnected ||
                HasSample != hasSample ||
                BeatDetectedThisFrame != beatDetectedThisFrame ||
                RealBeatDetectedThisFrame != realBeatDetectedThisFrame ||
                Mathf.Abs(CurrentBpm - clampedBpm) >= 0.001f ||
                Mathf.Abs(CurrentIbiMs - clampedIbiMs) >= 0.001f ||
                Mathf.Abs(CurrentPulse01 - clampedPulse) >= 0.0001f ||
                Mathf.Abs(Confidence01 - clampedConfidence) >= 0.0001f;

            TrackingState = trackingState;
            IsConnected = isConnected;
            HasSample = hasSample;
            BeatDetectedThisFrame = beatDetectedThisFrame;
            RealBeatDetectedThisFrame = realBeatDetectedThisFrame;
            CurrentBpm = clampedBpm;
            CurrentIbiMs = clampedIbiMs;
            CurrentPulse01 = clampedPulse;
            Confidence01 = clampedConfidence;

            if (!changed)
                return;

            PEHeartbeatSample sample = new PEHeartbeatSample
            {
                Timestamp = Time.unscaledTimeAsDouble,
                ElapsedSeconds = Time.unscaledTime,
                ModuleId = ModuleId,
                TrackingState = TrackingState,
                IsConnected = IsConnected,
                HasSample = HasSample,
                BeatDetectedThisFrame = BeatDetectedThisFrame,
                RealBeatDetectedThisFrame = RealBeatDetectedThisFrame,
                Bpm = CurrentBpm,
                IbiMs = CurrentIbiMs,
                Pulse01 = CurrentPulse01,
                Confidence01 = Confidence01
            };

            if (HasSample)
                ValueChanged?.Invoke(CurrentPulse01);
            SampleUpdated?.Invoke(sample);
            PublishSignals();
        }

        private void PublishSignals()
        {
            if (!publishToSignalRegistry)
                return;

            string pulseKey = NormalizeSignalName(pulseSignalName);
            if (!string.IsNullOrEmpty(pulseKey))
                PEBiofeedbackSignalRegistry.Publish(pulseKey, CurrentPulse01);

            string stateKey = NormalizeSignalName(stateSignalName);
            if (!string.IsNullOrEmpty(stateKey))
                PEBiofeedbackSignalRegistry.Publish(stateKey, MapTrackingStateTo01(TrackingState));

            string trackingKey = NormalizeSignalName(trackingSignalName);
            if (!string.IsNullOrEmpty(trackingKey))
            {
                float tracking01 =
                    TrackingState == PEHeartbeatTrackingState.Tracking &&
                    HasSample &&
                    IsConnected
                        ? 1f
                        : 0f;
                PEBiofeedbackSignalRegistry.Publish(trackingKey, tracking01);
            }

            string beatKey = NormalizeSignalName(beatSignalName);
            if (!string.IsNullOrEmpty(beatKey))
                PEBiofeedbackSignalRegistry.Publish(beatKey, BeatDetectedThisFrame ? 1f : 0f);

            string realBeatKey = NormalizeSignalName(realBeatSignalName);
            if (!string.IsNullOrEmpty(realBeatKey))
                PEBiofeedbackSignalRegistry.Publish(realBeatKey, RealBeatDetectedThisFrame ? 1f : 0f);
        }

        private static float MapTrackingStateTo01(PEHeartbeatTrackingState trackingState)
        {
            switch (trackingState)
            {
                case PEHeartbeatTrackingState.Tracking:
                    return 1f;
                case PEHeartbeatTrackingState.Stale:
                    return 0.5f;
                case PEHeartbeatTrackingState.Unavailable:
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

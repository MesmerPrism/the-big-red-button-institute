using System;
using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Breathing
{
    public abstract class PEBreathingSourceModule : MonoBehaviour, IPEBreathingSignalModule
    {
        [Header("Module Identity")]
        [SerializeField] private string moduleId = string.Empty;

        [Header("Output Signals")]
        [SerializeField] private bool publishToSignalRegistry = true;
        [SerializeField] private string volumeSignalName = string.Empty;
        [SerializeField] private string stateSignalName = string.Empty;
        [SerializeField] private string trackingSignalName = string.Empty;

        [Header("Fallback")]
        [SerializeField] private bool emitFallbackWhenUnavailable = true;
        [Range(0f, 1f)]
        [SerializeField] private float fallbackVolume01 = 0.5f;

        public event Action<PEBreathingSample> SampleUpdated;
        public event Action<float> ValueChanged;

        public string ModuleId => string.IsNullOrWhiteSpace(moduleId) ? name : moduleId.Trim();
        public bool IsProcessingEnabled { get; private set; } = true;
        public bool HasSample { get; private set; }
        public bool IsCalibrated { get; private set; }
        public PEBreathingState CurrentState { get; private set; } = PEBreathingState.BadTracking;
        public float CurrentVolume01 { get; private set; } = 0.5f;

        protected bool EmitFallbackWhenUnavailable => emitFallbackWhenUnavailable;
        protected float FallbackVolume01 => Mathf.Clamp01(fallbackVolume01);

        public void SetProcessingEnabled(bool enabled)
        {
            if (IsProcessingEnabled == enabled)
                return;

            IsProcessingEnabled = enabled;
            OnProcessingEnabledChanged(enabled);

            if (!enabled)
                PublishUnavailable(forceEvent: true);
        }

        public void SetModuleIdIfEmpty(string value)
        {
            if (!string.IsNullOrWhiteSpace(moduleId))
                return;

            if (string.IsNullOrWhiteSpace(value))
                return;

            moduleId = value.Trim();
        }

        protected void SetOutputSignalNamesIfEmpty(string volumeSignal, string stateSignal, string trackingSignal)
        {
            if (string.IsNullOrWhiteSpace(volumeSignalName))
                volumeSignalName = NormalizeSignalName(volumeSignal);
            if (string.IsNullOrWhiteSpace(stateSignalName))
                stateSignalName = NormalizeSignalName(stateSignal);
            if (string.IsNullOrWhiteSpace(trackingSignalName))
                trackingSignalName = NormalizeSignalName(trackingSignal);
        }

        protected virtual void OnProcessingEnabledChanged(bool enabled)
        {
        }

        protected void PublishUnavailable(bool forceEvent)
        {
            if (!emitFallbackWhenUnavailable)
            {
                PublishSample(
                    PEBreathingState.BadTracking,
                    FallbackVolume01,
                    isCalibrated: false,
                    hasSample: false,
                    forceEvent: forceEvent);
                return;
            }

            PublishSample(
                PEBreathingState.BadTracking,
                FallbackVolume01,
                isCalibrated: false,
                hasSample: true,
                forceEvent: forceEvent);
        }

        protected void PublishSample(
            PEBreathingState state,
            float volume01,
            bool isCalibrated,
            bool hasSample,
            bool forceEvent = false)
        {
            float clampedVolume = Mathf.Clamp01(volume01);

            bool stateChanged = CurrentState != state;
            bool volumeChanged = Mathf.Abs(CurrentVolume01 - clampedVolume) >= 0.0001f;
            bool calibratedChanged = IsCalibrated != isCalibrated;
            bool hasSampleChanged = HasSample != hasSample;
            bool changed = forceEvent || stateChanged || volumeChanged || calibratedChanged || hasSampleChanged;

            CurrentState = state;
            CurrentVolume01 = clampedVolume;
            IsCalibrated = isCalibrated;
            HasSample = hasSample;

            if (!changed)
                return;

            PEBreathingSample sample = new PEBreathingSample
            {
                Timestamp = Time.unscaledTimeAsDouble,
                ElapsedSeconds = Time.unscaledTime,
                ModuleId = ModuleId,
                State = CurrentState,
                Volume01 = CurrentVolume01,
                IsCalibrated = IsCalibrated,
                HasSample = HasSample
            };

            if (HasSample)
                ValueChanged?.Invoke(CurrentVolume01);
            SampleUpdated?.Invoke(sample);
            PublishSignals();
        }

        private void PublishSignals()
        {
            if (!publishToSignalRegistry)
                return;

            string volumeKey = NormalizeSignalName(volumeSignalName);
            if (!string.IsNullOrEmpty(volumeKey))
                PEBiofeedbackSignalRegistry.Publish(volumeKey, CurrentVolume01);

            string stateKey = NormalizeSignalName(stateSignalName);
            if (!string.IsNullOrEmpty(stateKey))
                PEBiofeedbackSignalRegistry.Publish(stateKey, MapStateTo01(CurrentState));

            string trackingKey = NormalizeSignalName(trackingSignalName);
            if (!string.IsNullOrEmpty(trackingKey))
                PEBiofeedbackSignalRegistry.Publish(
                    trackingKey,
                    HasSample && IsCalibrated ? 1f : 0f);
        }

        private static float MapStateTo01(PEBreathingState state)
        {
            switch (state)
            {
                case PEBreathingState.Inhaling:
                    return 1f;
                case PEBreathingState.Exhaling:
                    return 0f;
                case PEBreathingState.Pausing:
                case PEBreathingState.BadTracking:
                default:
                    return 0.5f;
            }
        }

        private static string NormalizeSignalName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

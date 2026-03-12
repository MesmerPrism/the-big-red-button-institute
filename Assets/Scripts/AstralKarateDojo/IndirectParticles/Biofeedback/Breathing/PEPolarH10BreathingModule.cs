using AstralKarateDojo.IndirectParticles;
using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Breathing
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-38)]
    public sealed class PEPolarH10BreathingModule : PEBreathingSourceModule
    {
        public enum InputMapping
        {
            Normalized01 = 0,
            RawMinMax = 1
        }

        [Header("Input Stream")]
        [SerializeField] private bool consumePublishedSignal = true;
        [SerializeField] private string publishedVolumeSignalName = "polar_acc_breath_volume";
        [SerializeField] private string publishedTrackingSignalName = "polar_acc_breath_tracking";
        [SerializeField] private bool requireCalibration = true;

        [Header("Mapping")]
        [SerializeField] private InputMapping inputMapping = InputMapping.Normalized01;
        [SerializeField] private float rawMin = 0f;
        [SerializeField] private float rawMax = 1f;
        [SerializeField] private bool invert01 = false;

        [Header("State Derivation")]
        [Range(0.0001f, 0.25f)]
        [SerializeField] private float deltaThreshold = 0.003f;
        [Min(0.05f)]
        [SerializeField] private float staleTimeoutSeconds = 1.0f;

        private PEBreathingStreamProcessor _streamProcessor;
        private bool _missingInputWarningLogged;

        private void Awake()
        {
            SetModuleIdIfEmpty("breathing_polar_h10");
            SetOutputSignalNamesIfEmpty(
                "breathing_polar_h10",
                "breathing_polar_h10_state",
                "breathing_polar_h10_tracking");
            _streamProcessor.Reset();
        }

        private void OnEnable()
        {
            _streamProcessor.Reset();
            TryPublishFromSignals(forceEvent: true);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(publishedVolumeSignalName))
                publishedVolumeSignalName = "polar_acc_breath_volume";
            if (string.IsNullOrWhiteSpace(publishedTrackingSignalName))
                publishedTrackingSignalName = "polar_acc_breath_tracking";

            deltaThreshold = Mathf.Max(0.0001f, deltaThreshold);
            staleTimeoutSeconds = Mathf.Max(0.05f, staleTimeoutSeconds);
        }

        private void Update()
        {
            if (!IsProcessingEnabled)
                return;

            bool hadSignal = TryPublishFromSignals(forceEvent: false);
            if (hadSignal)
                return;

            if (!_streamProcessor.HasReceivedVolume)
            {
                PublishUnavailable(forceEvent: false);
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (_streamProcessor.IsStale(now, staleTimeoutSeconds))
                PublishUnavailable(forceEvent: false);
        }

        protected override void OnProcessingEnabledChanged(bool enabled)
        {
            if (!enabled)
                return;

            _streamProcessor.Reset();
            TryPublishFromSignals(forceEvent: true);
        }

        private bool TryPublishFromSignals(bool forceEvent)
        {
            if (!IsProcessingEnabled)
            {
                PublishUnavailable(forceEvent);
                return false;
            }

            if (!TryReadInputSignal(out float rawInput, out bool trackingReady))
            {
                WarnMissingInputSignalIfNeeded();
                PublishUnavailable(forceEvent);
                return false;
            }

            ClearMissingInputWarning();

            bool calibrated = !requireCalibration || trackingReady;
            bool processed = _streamProcessor.TryProcess(
                new PEBreathingStreamProcessor.UpdateInput(
                    rawInput: rawInput,
                    isCalibrated: calibrated,
                    timestamp: Time.unscaledTimeAsDouble,
                    useRawMinMaxMapping: inputMapping == InputMapping.RawMinMax,
                    rawMin: rawMin,
                    rawMax: rawMax,
                    invert01: invert01,
                    deltaThreshold: deltaThreshold,
                    staleTimeoutSeconds: staleTimeoutSeconds),
                out PEBreathingStreamProcessor.UpdateOutput output);

            if (!processed)
            {
                PublishUnavailable(forceEvent);
                return true;
            }

            PublishSample(
                output.State,
                output.Volume01,
                output.IsCalibrated,
                output.HasSample,
                forceEvent: forceEvent);

            return true;
        }

        private bool TryReadInputSignal(out float volumeInput, out bool trackingReady)
        {
            volumeInput = 0f;
            trackingReady = !requireCalibration;

            if (!consumePublishedSignal)
                return false;

            string volumeName = NormalizeSignalNameOrFallback(publishedVolumeSignalName, "polar_acc_breath_volume");
            if (!PEBiofeedbackSignalRegistry.TryGetValue01(volumeName, out volumeInput))
                return false;

            if (!requireCalibration)
                return true;

            string trackingName = NormalizeSignalNameOrFallback(publishedTrackingSignalName, "polar_acc_breath_tracking");
            if (!PEBiofeedbackSignalRegistry.TryGetValue01(trackingName, out float tracking01))
            {
                trackingReady = false;
                return true;
            }

            trackingReady = tracking01 >= 0.5f;
            return true;
        }

        private void WarnMissingInputSignalIfNeeded()
        {
            if (_missingInputWarningLogged)
                return;

            _missingInputWarningLogged = true;
            string volumeName = NormalizeSignalNameOrFallback(publishedVolumeSignalName, "polar_acc_breath_volume");
            Debug.LogWarning(
                $"[PEPolarH10BreathingModule] Input signal '{volumeName}' is missing. Ensure transport publishes the stream before enabling Polar breathing.",
                this);
        }

        private void ClearMissingInputWarning()
        {
            _missingInputWarningLogged = false;
        }

        private static string NormalizeSignalNameOrFallback(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Trim();
        }
    }
}

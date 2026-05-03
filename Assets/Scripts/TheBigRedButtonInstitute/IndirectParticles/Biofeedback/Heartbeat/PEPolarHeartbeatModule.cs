using TheBigRedButtonInstitute.IndirectParticles;
using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-36)]
    public sealed class PEPolarHeartbeatModule : PEHeartbeatSourceModule
    {
        [Header("Input Streams")]
        [SerializeField] private bool requireConnection = true;
        [SerializeField] private string publishedConnectedSignalName = "polar_hr_connected";
        [SerializeField] private string publishedBpmSignalName = "polar_hr_bpm";
        [SerializeField] private string publishedRrIntervalSignalName = "polar_hr_rr_ms";
        [SerializeField] private string publishedRrBeatCountSignalName = "polar_hr_rr_beat_count";

        [Header("Signal Policy")]
        [SerializeField] private bool preferRrIntervalsForBpm = true;
        [Min(0.1f)]
        [SerializeField] private float staleTimeoutSeconds = 2.0f;
        [Min(0f)]
        [SerializeField] private float bpmSmoothingSpeed = 6f;

        [Header("Pulse")]
        [Range(0f, 1f)]
        [SerializeField] private float pulseBaseline01 = 0.10f;
        [Range(0f, 1f)]
        [SerializeField] private float pulsePeak01 = 1.0f;
        [Min(0.01f)]
        [SerializeField] private float pulseDecaySeconds = 0.25f;

        private PEHeartbeatPulseShaper _pulseShaper;
        private bool _isConnected;
        private bool _hasData;
        private double _lastDataAt;
        private float _targetBpm;
        private float _smoothedBpm;
        private float _currentIbiMs;
        private int _pendingBeatTriggers;
        private bool _hasBeatCounter;
        private float _lastBeatCounter;

        private bool _missingConnectedSignalWarningLogged;
        private bool _missingDataSignalsWarningLogged;

        private void Awake()
        {
            SetModuleIdIfEmpty("heartbeat_polar");
            SetOutputSignalNamesIfEmpty(
                "heartbeat_polar",
                "heartbeat_polar_state",
                "heartbeat_polar_tracking",
                "heartbeat_polar_beat",
                "heartbeat_polar_real_beat");
            _pulseShaper.Reset(pulseBaseline01);
        }

        private void OnEnable()
        {
            ResetRuntimeState();
            PublishSnapshot(forceEvent: true);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(publishedConnectedSignalName))
                publishedConnectedSignalName = "polar_hr_connected";
            if (string.IsNullOrWhiteSpace(publishedBpmSignalName))
                publishedBpmSignalName = "polar_hr_bpm";
            if (string.IsNullOrWhiteSpace(publishedRrIntervalSignalName))
                publishedRrIntervalSignalName = "polar_hr_rr_ms";
            if (string.IsNullOrWhiteSpace(publishedRrBeatCountSignalName))
                publishedRrBeatCountSignalName = "polar_hr_rr_beat_count";

            staleTimeoutSeconds = Mathf.Max(0.1f, staleTimeoutSeconds);
            pulseDecaySeconds = Mathf.Max(0.01f, pulseDecaySeconds);
        }

        private void Update()
        {
            if (!IsProcessingEnabled)
                return;

            if (!TryReadConnected(out bool connected))
            {
                WarnMissingConnectedSignalIfNeeded();
                PublishUnavailable(forceEvent: false);
                return;
            }

            ClearMissingConnectedSignalWarning();

            if (!connected && _isConnected)
                ResetRuntimeState();
            _isConnected = connected;

            if (_isConnected || !requireConnection)
            {
                if (!TryConsumeHeartbeatData())
                {
                    WarnMissingDataSignalsIfNeeded();
                    PublishUnavailable(forceEvent: false);
                    return;
                }

                ClearMissingDataSignalsWarning();
            }
            else
            {
                ClearMissingDataSignalsWarning();
            }

            PublishSnapshot(forceEvent: false);
        }

        protected override void OnProcessingEnabledChanged(bool enabled)
        {
            if (!enabled)
                return;

            ResetRuntimeState();
            PublishSnapshot(forceEvent: true);
        }

        private bool TryReadConnected(out bool connected)
        {
            connected = false;
            string signalName = NormalizeSignalNameOrFallback(publishedConnectedSignalName, "polar_hr_connected");
            if (!PEBiofeedbackRawSignalRegistry.TryGetValue(signalName, out float value))
                return false;

            connected = value >= 0.5f;
            return true;
        }

        private bool TryConsumeHeartbeatData()
        {
            double now = Time.unscaledTimeAsDouble;

            string bpmName = NormalizeSignalNameOrFallback(publishedBpmSignalName, "polar_hr_bpm");
            string rrName = NormalizeSignalNameOrFallback(publishedRrIntervalSignalName, "polar_hr_rr_ms");
            string beatCountName = NormalizeSignalNameOrFallback(publishedRrBeatCountSignalName, "polar_hr_rr_beat_count");

            bool hasBpm = PEBiofeedbackRawSignalRegistry.TryGetValue(bpmName, out float bpmRaw);
            bool hasRr = PEBiofeedbackRawSignalRegistry.TryGetValue(rrName, out float rrMsRaw);
            bool hasBeatCount = PEBiofeedbackRawSignalRegistry.TryGetValue(beatCountName, out float beatCountRaw);

            if (!hasBpm && !hasRr && !hasBeatCount)
                return false;

            if (hasBeatCount)
            {
                float safeCounter = Mathf.Max(0f, beatCountRaw);
                if (!_hasBeatCounter)
                {
                    _lastBeatCounter = safeCounter;
                    _hasBeatCounter = true;
                }
                else
                {
                    float diffFloat = safeCounter - _lastBeatCounter;
                    if (diffFloat > 0f)
                    {
                        int beatDelta = Mathf.Max(1, Mathf.RoundToInt(diffFloat));
                        _pendingBeatTriggers += beatDelta;
                        _hasData = true;
                        _lastDataAt = now;
                    }
                    else if (diffFloat < -0.5f)
                    {
                        _pendingBeatTriggers = 0;
                    }

                    _lastBeatCounter = safeCounter;
                }
            }

            if (hasRr && rrMsRaw > 0f && !float.IsNaN(rrMsRaw) && !float.IsInfinity(rrMsRaw))
            {
                _currentIbiMs = Mathf.Clamp(rrMsRaw, 200f, 3000f);
                float rrBpm = Mathf.Clamp(60000f / _currentIbiMs, 20f, 260f);
                if (preferRrIntervalsForBpm || _targetBpm <= 0f)
                    _targetBpm = rrBpm;

                _hasData = true;
                _lastDataAt = now;
            }

            if (hasBpm && bpmRaw > 0f && !float.IsNaN(bpmRaw) && !float.IsInfinity(bpmRaw))
            {
                float bpm = Mathf.Clamp(bpmRaw, 20f, 260f);
                _targetBpm = bpm;
                if (_currentIbiMs <= 0f)
                    _currentIbiMs = 60000f / bpm;

                _hasData = true;
                _lastDataAt = now;
            }

            return true;
        }

        private void PublishSnapshot(bool forceEvent)
        {
            if (!IsProcessingEnabled)
            {
                PublishUnavailable(forceEvent);
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            bool fresh = _hasData && (now - _lastDataAt) <= Mathf.Max(0.1f, staleTimeoutSeconds);

            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, bpmSmoothingSpeed) * Mathf.Max(0f, Time.unscaledDeltaTime));
            if (_smoothedBpm <= 0f)
                _smoothedBpm = _targetBpm;
            else if (_targetBpm > 0f)
                _smoothedBpm = Mathf.Lerp(_smoothedBpm, _targetBpm, blend);

            if (_smoothedBpm > 0f && (_currentIbiMs <= 0f || !preferRrIntervalsForBpm))
                _currentIbiMs = 60000f / _smoothedBpm;

            PEHeartbeatPulseShaper.UpdateOutput pulseOutput = _pulseShaper.Update(
                new PEHeartbeatPulseShaper.UpdateInput(
                    now: now,
                    targetBpm: _smoothedBpm,
                    explicitBeatTriggers: _pendingBeatTriggers,
                    detectBeatsFromPulseInput: false,
                    pulseInput01: 0f,
                    pulseBeatThreshold01: 0.7f,
                    pulseRefractorySeconds: 0.3f,
                    bpmSmoothingSpeed: bpmSmoothingSpeed,
                    pulseDecaySeconds: pulseDecaySeconds,
                    pulseBaseline01: pulseBaseline01,
                    pulsePeak01: pulsePeak01));
            bool realBeatDetectedThisFrame = _pendingBeatTriggers > 0;
            _pendingBeatTriggers = 0;

            PEHeartbeatTrackingState state;
            if (!_isConnected && requireConnection)
                state = PEHeartbeatTrackingState.Unavailable;
            else if (!fresh)
                state = PEHeartbeatTrackingState.Stale;
            else
                state = PEHeartbeatTrackingState.Tracking;

            bool hasSample = _hasData || EmitFallbackWhenUnavailable;
            float confidence = state == PEHeartbeatTrackingState.Tracking
                ? (preferRrIntervalsForBpm && _currentIbiMs > 0f ? 1f : 0.7f)
                : 0f;

            PublishSample(
                state,
                isConnected: _isConnected,
                hasSample: hasSample,
                beatDetectedThisFrame: pulseOutput.BeatDetectedThisFrame,
                realBeatDetectedThisFrame: realBeatDetectedThisFrame,
                bpm: _smoothedBpm,
                ibiMs: _currentIbiMs,
                pulse01: pulseOutput.Pulse01,
                confidence01: confidence,
                forceEvent: forceEvent);
        }

        private void ResetRuntimeState()
        {
            _hasData = false;
            _lastDataAt = 0.0;
            _targetBpm = 0f;
            _smoothedBpm = 0f;
            _currentIbiMs = 0f;
            _pendingBeatTriggers = 0;
            _hasBeatCounter = false;
            _lastBeatCounter = 0f;
            _pulseShaper.Reset(pulseBaseline01);
        }

        private void WarnMissingConnectedSignalIfNeeded()
        {
            if (_missingConnectedSignalWarningLogged)
                return;

            _missingConnectedSignalWarningLogged = true;
            string signalName = NormalizeSignalNameOrFallback(publishedConnectedSignalName, "polar_hr_connected");
            Debug.LogWarning(
                $"[PEPolarHeartbeatModule] Published transport signal '{signalName}' was not found. Verify Polar heart-rate transport routing and signal name.",
                this);
        }

        private void ClearMissingConnectedSignalWarning()
        {
            _missingConnectedSignalWarningLogged = false;
        }

        private void WarnMissingDataSignalsIfNeeded()
        {
            if (_missingDataSignalsWarningLogged)
                return;

            _missingDataSignalsWarningLogged = true;
            string bpmName = NormalizeSignalNameOrFallback(publishedBpmSignalName, "polar_hr_bpm");
            string rrName = NormalizeSignalNameOrFallback(publishedRrIntervalSignalName, "polar_hr_rr_ms");
            string beatCountName = NormalizeSignalNameOrFallback(publishedRrBeatCountSignalName, "polar_hr_rr_beat_count");
            Debug.LogWarning(
                $"[PEPolarHeartbeatModule] No published Polar heartbeat data streams were found. Expected '{bpmName}', '{rrName}', or '{beatCountName}'.",
                this);
        }

        private void ClearMissingDataSignalsWarning()
        {
            _missingDataSignalsWarningLogged = false;
        }

        private static string NormalizeSignalNameOrFallback(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Trim();
        }
    }
}

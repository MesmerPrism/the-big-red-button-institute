using System;
using TheBigRedButtonInstitute.IndirectParticles;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat;
using UnityEngine;
using UnityEngine.Serialization;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Coherence
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-33)]
    public sealed class PEHeartbeatCoherenceModule : PECoherenceSourceModule
    {
        [Header("Heartbeat Published Signals")]
        [SerializeField] private string publishedHeartbeatStateSignalName = "heartbeat_polar_state";
        [FormerlySerializedAs("publishedHeartbeatBeatSignalName")]
        [SerializeField] private string publishedHeartbeatRealBeatSignalName = "heartbeat_polar_real_beat";
        [Range(0f, 1f)]
        [SerializeField] private float beatThreshold01 = 0.5f;
        [SerializeField] private bool acceptStaleHeartbeatState = true;

        [Header("Coherence Window")]
        [Min(4)]
        [SerializeField] private int minimumIbiSamples = 20;
        [Min(16f)]
        [SerializeField] private float coherenceWindowSeconds = 64f;
        [Min(0f)]
        [SerializeField] private float coherenceSmoothingSpeed = 4f;
        [Min(0.1f)]
        [SerializeField] private float staleTimeoutSeconds = 3f;

        [Header("Debug")]
        [SerializeField] private bool logDebug = false;

        private readonly PECoherenceRrWindowCalculator _calculator = new PECoherenceRrWindowCalculator();

        private bool _hasHeartbeatState;
        private PEHeartbeatTrackingState _heartbeatTrackingState = PEHeartbeatTrackingState.Unavailable;

        private bool _hasCoherence;
        private double _lastCoherenceAt;
        private float _targetCoherence01;
        private float _targetConfidence01;
        private float _smoothedCoherence01;
        private bool _hasSmoothedCoherence;

        private bool _lastBeatHigh;
        private double _lastAcceptedBeatAt;
        private float _lastAcceptedIbiMs;

        private bool _missingHeartbeatStateSignalWarningLogged;
        private string _missingHeartbeatStateSignalName = string.Empty;
        private bool _missingHeartbeatBeatSignalWarningLogged;
        private string _missingHeartbeatBeatSignalName = string.Empty;

        private void Awake()
        {
            SetModuleIdIfEmpty("coherence_heartbeat");
            SetOutputSignalNamesIfEmpty(
                "coherence_heartbeat",
                "coherence_heartbeat_state",
                "coherence_heartbeat_tracking",
                "coherence_heartbeat_confidence");
        }

        private void OnEnable()
        {
            ResetRuntimeState();
            PublishSnapshot(forceEvent: true);
        }

        private void OnValidate()
        {
            minimumIbiSamples = Mathf.Max(4, minimumIbiSamples);
            coherenceWindowSeconds = Mathf.Max(16f, coherenceWindowSeconds);
            staleTimeoutSeconds = Mathf.Max(0.1f, staleTimeoutSeconds);
            beatThreshold01 = Mathf.Clamp01(beatThreshold01);
        }

        public string HeartbeatStateSignalName => NormalizeSignalName(publishedHeartbeatStateSignalName);
        public string HeartbeatRealBeatSignalName => NormalizeSignalName(publishedHeartbeatRealBeatSignalName);

        public void SetHeartbeatInputSignals(string stateSignalName, string realBeatSignalName)
        {
            publishedHeartbeatStateSignalName = NormalizeSignalName(stateSignalName);
            publishedHeartbeatRealBeatSignalName = NormalizeSignalName(realBeatSignalName);
            _missingHeartbeatStateSignalWarningLogged = false;
            _missingHeartbeatStateSignalName = string.Empty;
            _missingHeartbeatBeatSignalWarningLogged = false;
            _missingHeartbeatBeatSignalName = string.Empty;
        }

        private void Update()
        {
            if (!IsProcessingEnabled)
                return;

            if (!TryReadHeartbeatState(out PEHeartbeatTrackingState heartbeatState, out string stateSignalName))
            {
                WarnMissingHeartbeatStateSignalIfNeeded(stateSignalName);
                PublishUnavailable(forceEvent: false);
                return;
            }

            ClearMissingHeartbeatStateSignalWarning();

            _hasHeartbeatState = true;
            _heartbeatTrackingState = heartbeatState;

            if (!TryReadHeartbeatRealBeat(out float beat01, out string beatSignalName))
            {
                WarnMissingHeartbeatRealBeatSignalIfNeeded(beatSignalName);
                PublishUnavailable(forceEvent: false);
                return;
            }

            ClearMissingHeartbeatRealBeatSignalWarning();

            bool beatHigh = beat01 >= beatThreshold01;
            bool beatRising = beatHigh && !_lastBeatHigh;
            _lastBeatHigh = beatHigh;

            if (beatRising && IsHeartbeatStateAccepted(heartbeatState))
                HandleAcceptedBeat(Time.unscaledTimeAsDouble);

            PublishSnapshot(forceEvent: false);
        }

        protected override void OnProcessingEnabledChanged(bool enabled)
        {
            if (!enabled)
                return;

            ResetRuntimeState();
            PublishSnapshot(forceEvent: true);
        }

        private bool TryReadHeartbeatState(out PEHeartbeatTrackingState heartbeatState, out string signalName)
        {
            heartbeatState = PEHeartbeatTrackingState.Unavailable;
            signalName = NormalizeSignalName(publishedHeartbeatStateSignalName);
            if (string.IsNullOrEmpty(signalName))
                return false;

            if (!PEBiofeedbackSignalRegistry.TryGetValue01(signalName, out float state01))
                return false;

            heartbeatState = DecodeHeartbeatTrackingState(state01);
            return true;
        }

        private bool TryReadHeartbeatRealBeat(out float beat01, out string signalName)
        {
            beat01 = 0f;
            signalName = NormalizeSignalName(publishedHeartbeatRealBeatSignalName);
            if (string.IsNullOrEmpty(signalName))
                return false;

            if (!PEBiofeedbackSignalRegistry.TryGetValue01(signalName, out float beatValue))
                return false;

            beat01 = Mathf.Clamp01(beatValue);
            return true;
        }

        private bool IsHeartbeatStateAccepted(PEHeartbeatTrackingState state)
        {
            if (state == PEHeartbeatTrackingState.Tracking)
                return true;

            return acceptStaleHeartbeatState && state == PEHeartbeatTrackingState.Stale;
        }

        private void HandleAcceptedBeat(double now)
        {
            if (_lastAcceptedBeatAt > 0d)
            {
                float ibiMs = (float)((now - _lastAcceptedBeatAt) * 1000.0d);
                if (ibiMs >= 250f)
                {
                    _lastAcceptedIbiMs = ibiMs;

                    if (_calculator.PushIbi(
                        now,
                        ibiMs,
                        coherenceWindowSeconds,
                        minimumIbiSamples,
                        out float coherence01,
                        out float confidence01))
                    {
                        _targetCoherence01 = coherence01;
                        _targetConfidence01 = confidence01;
                        _hasCoherence = true;
                        _lastCoherenceAt = now;

                        if (logDebug)
                        {
                            Debug.Log(
                                $"[PEHeartbeatCoherenceModule] coherence={coherence01:0.000} conf={confidence01:0.00} samples={_calculator.SampleCount}",
                                this);
                        }
                    }
                }
            }

            _lastAcceptedBeatAt = now;
        }

        private void PublishSnapshot(bool forceEvent)
        {
            if (!IsProcessingEnabled || !_hasHeartbeatState)
            {
                PublishUnavailable(forceEvent);
                return;
            }

            bool connected = _heartbeatTrackingState != PEHeartbeatTrackingState.Unavailable;
            double now = Time.unscaledTimeAsDouble;
            bool fresh = _hasCoherence && (now - _lastCoherenceAt) <= Mathf.Max(0.1f, staleTimeoutSeconds);

            float nextCoherence = _targetCoherence01;
            if (!_hasSmoothedCoherence)
            {
                _smoothedCoherence01 = nextCoherence;
                _hasSmoothedCoherence = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-Mathf.Max(0f, coherenceSmoothingSpeed) * Mathf.Max(0f, Time.unscaledDeltaTime));
                _smoothedCoherence01 = Mathf.Lerp(_smoothedCoherence01, nextCoherence, blend);
            }

            PECoherenceTrackingState coherenceState;
            if (!connected)
                coherenceState = PECoherenceTrackingState.Unavailable;
            else if (!fresh)
                coherenceState = PECoherenceTrackingState.Stale;
            else
                coherenceState = PECoherenceTrackingState.Tracking;

            bool hasSample = _hasCoherence || EmitFallbackWhenUnavailable;
            float coherence01 = _hasCoherence ? _smoothedCoherence01 : FallbackCoherence01;
            float confidence01 = fresh ? _targetConfidence01 : 0f;
            float heartbeatIbiMs = _lastAcceptedIbiMs > 0f ? _lastAcceptedIbiMs : 0f;
            float heartbeatBpm = heartbeatIbiMs > 0f ? Mathf.Clamp(60000f / heartbeatIbiMs, 0f, 260f) : 0f;

            PublishSample(
                coherenceState,
                isConnected: connected,
                hasSample: hasSample,
                coherence01: coherence01,
                confidence01: confidence01,
                heartbeatBpm: heartbeatBpm,
                heartbeatIbiMs: heartbeatIbiMs,
                forceEvent: forceEvent);
        }

        private void ResetRuntimeState()
        {
            _calculator.Reset();
            _hasHeartbeatState = false;
            _heartbeatTrackingState = PEHeartbeatTrackingState.Unavailable;

            _hasCoherence = false;
            _lastCoherenceAt = 0d;
            _targetCoherence01 = 0f;
            _targetConfidence01 = 0f;
            _smoothedCoherence01 = 0f;
            _hasSmoothedCoherence = false;

            _lastBeatHigh = false;
            _lastAcceptedBeatAt = 0d;
            _lastAcceptedIbiMs = 0f;

            _missingHeartbeatStateSignalWarningLogged = false;
            _missingHeartbeatStateSignalName = string.Empty;
            _missingHeartbeatBeatSignalWarningLogged = false;
            _missingHeartbeatBeatSignalName = string.Empty;
        }

        private void WarnMissingHeartbeatStateSignalIfNeeded(string signalName)
        {
            string safeName = string.IsNullOrEmpty(signalName) ? "<empty>" : signalName;
            if (_missingHeartbeatStateSignalWarningLogged &&
                string.Equals(_missingHeartbeatStateSignalName, safeName, StringComparison.Ordinal))
                return;

            _missingHeartbeatStateSignalWarningLogged = true;
            _missingHeartbeatStateSignalName = safeName;
            Debug.LogWarning(
                $"[PEHeartbeatCoherenceModule] Published heartbeat state signal '{safeName}' was not found. No fallback will be used; verify upstream publisher wiring and signal name.",
                this);
        }

        private void ClearMissingHeartbeatStateSignalWarning()
        {
            _missingHeartbeatStateSignalWarningLogged = false;
            _missingHeartbeatStateSignalName = string.Empty;
        }

        private void WarnMissingHeartbeatRealBeatSignalIfNeeded(string signalName)
        {
            string safeName = string.IsNullOrEmpty(signalName) ? "<empty>" : signalName;
            if (_missingHeartbeatBeatSignalWarningLogged &&
                string.Equals(_missingHeartbeatBeatSignalName, safeName, StringComparison.Ordinal))
                return;

            _missingHeartbeatBeatSignalWarningLogged = true;
            _missingHeartbeatBeatSignalName = safeName;
            Debug.LogWarning(
                $"[PEHeartbeatCoherenceModule] Published heartbeat real-beat signal '{safeName}' was not found. No fallback will be used; verify upstream publisher wiring and signal name.",
                this);
        }

        private void ClearMissingHeartbeatRealBeatSignalWarning()
        {
            _missingHeartbeatBeatSignalWarningLogged = false;
            _missingHeartbeatBeatSignalName = string.Empty;
        }

        private static PEHeartbeatTrackingState DecodeHeartbeatTrackingState(float state01)
        {
            if (state01 >= 0.75f)
                return PEHeartbeatTrackingState.Tracking;
            if (state01 >= 0.25f)
                return PEHeartbeatTrackingState.Stale;
            return PEHeartbeatTrackingState.Unavailable;
        }

        private static string NormalizeSignalName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

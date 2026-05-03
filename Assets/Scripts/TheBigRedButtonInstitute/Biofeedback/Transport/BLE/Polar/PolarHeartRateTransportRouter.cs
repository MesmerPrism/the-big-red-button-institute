using TheBigRedButtonInstitute.IndirectParticles;
using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-41)]
    public sealed class PolarHeartRateTransportRouter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PolarUnifiedModule unifiedModule;

        [Header("Raw Signals")]
        [SerializeField] private bool publishToRawSignalRegistry = true;
        [SerializeField] private string rawSignalPrefix = "polar_hr";
        [SerializeField] private bool publishPerSample = true;

        [Header("Logging")]
        [SerializeField] private bool logDebug = false;

        private bool _subscribed;
        private bool _missingReferenceWarningLogged;
        private float _rrBeatAccumulator;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            _rrBeatAccumulator = 0f;
            PublishConnectionSignal(isConnected: false);
            PublishHeartbeatDefaults();
        }

        private void Subscribe()
        {
            Unsubscribe();
            if (!HasRequiredReferences())
                return;

            unifiedModule.ConnectionChanged += HandleConnectionChanged;
            unifiedModule.HeartRateReceived += HandleHeartRateReceived;
            unifiedModule.RrIntervalsReceived += HandleRrIntervalsReceived;
            _subscribed = true;

            if (logDebug)
                Debug.Log("[PolarHeartRateTransportRouter] Subscribed to Polar transport events.", this);

            HandleConnectionChanged(unifiedModule.IsConnected);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || unifiedModule == null)
                return;

            unifiedModule.ConnectionChanged -= HandleConnectionChanged;
            unifiedModule.HeartRateReceived -= HandleHeartRateReceived;
            unifiedModule.RrIntervalsReceived -= HandleRrIntervalsReceived;
            _subscribed = false;

            if (logDebug)
                Debug.Log("[PolarHeartRateTransportRouter] Unsubscribed from Polar transport events.", this);
        }

        private bool HasRequiredReferences()
        {
            if (unifiedModule != null)
                return true;

            if (_missingReferenceWarningLogged)
                return false;

            _missingReferenceWarningLogged = true;
            Debug.LogWarning(
                "[PolarHeartRateTransportRouter] Missing required reference: unifiedModule. Assign it explicitly in the inspector.",
                this);
            return false;
        }

        private void HandleConnectionChanged(bool connected)
        {
            if (!connected)
                _rrBeatAccumulator = 0f;

            PublishConnectionSignal(connected);
            if (!connected)
                PublishHeartbeatDefaults();
        }

        private void HandleHeartRateReceived(ushort bpmRaw)
        {
            if (!publishToRawSignalRegistry)
                return;

            string prefix = NormalizePrefix();
            if (string.IsNullOrEmpty(prefix))
                return;

            float bpm = Mathf.Clamp(bpmRaw, 0f, 260f);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_bpm", bpm);

            if (bpm > 0f)
                PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_ibi_ms", 60000f / bpm);
        }

        private void HandleRrIntervalsReceived(float[] rrIntervalsMs)
        {
            if (!publishToRawSignalRegistry || rrIntervalsMs == null || rrIntervalsMs.Length == 0)
                return;

            string prefix = NormalizePrefix();
            if (string.IsNullOrEmpty(prefix))
                return;

            float lastRrMs = 0f;
            float sumRrMs = 0f;
            int validCount = 0;

            for (int i = 0; i < rrIntervalsMs.Length; i++)
            {
                float rrMs = rrIntervalsMs[i];
                if (rrMs <= 0f || float.IsNaN(rrMs) || float.IsInfinity(rrMs))
                    continue;

                lastRrMs = rrMs;
                sumRrMs += rrMs;
                validCount++;

                if (!publishPerSample)
                    continue;

                PublishRrSample(prefix, rrMs);
            }

            if (validCount <= 0)
                return;

            if (!publishPerSample)
            {
                float avgRrMs = sumRrMs / validCount;
                PublishRrSample(prefix, avgRrMs);
            }

            _rrBeatAccumulator += validCount;
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_ms", lastRrMs);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_beat_count", _rrBeatAccumulator);
        }

        private void PublishRrSample(string prefix, float rrMs)
        {
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_ms", rrMs);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_ibi_ms", rrMs);
            if (rrMs > 0f)
                PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_bpm", Mathf.Clamp(60000f / rrMs, 0f, 260f));
        }

        private void PublishConnectionSignal(bool isConnected)
        {
            if (!publishToRawSignalRegistry)
                return;

            string prefix = NormalizePrefix();
            if (string.IsNullOrEmpty(prefix))
                return;

            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_connected", isConnected ? 1f : 0f);
        }

        private void PublishHeartbeatDefaults()
        {
            if (!publishToRawSignalRegistry)
                return;

            string prefix = NormalizePrefix();
            if (string.IsNullOrEmpty(prefix))
                return;

            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_bpm", 0f);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_ibi_ms", 0f);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_ms", 0f);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_bpm", 0f);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_rr_beat_count", _rrBeatAccumulator);
        }

        private string NormalizePrefix()
        {
            return string.IsNullOrWhiteSpace(rawSignalPrefix) ? string.Empty : rawSignalPrefix.Trim();
        }
    }
}

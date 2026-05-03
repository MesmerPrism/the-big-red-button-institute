using TheBigRedButtonInstitute.IndirectParticles;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Breathing;
using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class PolarAccTransportRouter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PolarUnifiedModule unifiedModule;
        [SerializeField] private PolarAccBreathingTracker breathingTracker;

        [Header("Raw Signals")]
        [SerializeField] private bool publishToRawSignalRegistry = true;
        [SerializeField] private string rawSignalPrefix = "polar_acc";
        [SerializeField] private bool publishPerSample = true;

        [Header("Logging")]
        [SerializeField] private bool logDebug = false;

        private bool _subscribed;
        private bool _missingReferenceWarningLogged;
        private bool _missingBreathingTrackerWarningLogged;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (breathingTracker != null)
                breathingTracker.SetTransportConnected(false);
            PublishConnectionSignal(isConnected: false);
        }

        private void Subscribe()
        {
            Unsubscribe();
            if (!HasRequiredReferences())
                return;

            unifiedModule.ConnectionChanged += HandleConnectionChanged;
            unifiedModule.AccFrameReceived += HandleAccFrameReceived;
            unifiedModule.PmdDataReceived += HandleRawPmdData;
            _subscribed = true;
            if (logDebug)
                Debug.Log("[PolarAccTransportRouter] Subscribed to Polar transport events.", this);

            HandleConnectionChanged(unifiedModule.IsConnected);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || unifiedModule == null)
                return;

            unifiedModule.ConnectionChanged -= HandleConnectionChanged;
            unifiedModule.AccFrameReceived -= HandleAccFrameReceived;
            unifiedModule.PmdDataReceived -= HandleRawPmdData;
            _subscribed = false;
            if (logDebug)
                Debug.Log("[PolarAccTransportRouter] Unsubscribed from Polar transport events.", this);
        }

        private bool HasRequiredReferences()
        {
            if (unifiedModule != null)
            {
                WarnMissingBreathingTrackerIfNeeded();
                return true;
            }

            if (_missingReferenceWarningLogged)
                return false;

            _missingReferenceWarningLogged = true;
            Debug.LogWarning(
                "[PolarAccTransportRouter] Missing required reference: unifiedModule. " +
                "Assign it explicitly in the inspector.",
                this);
            return false;
        }

        private void WarnMissingBreathingTrackerIfNeeded()
        {
            if (breathingTracker != null || _missingBreathingTrackerWarningLogged)
                return;

            _missingBreathingTrackerWarningLogged = true;
            Debug.LogWarning(
                "[PolarAccTransportRouter] Optional reference 'breathingTracker' is missing. " +
                "Raw transport signals will still publish, but ACC forwarding to PolarAccBreathingTracker is disabled.",
                this);
        }

        private void HandleConnectionChanged(bool connected)
        {
            if (breathingTracker != null)
                breathingTracker.SetTransportConnected(connected);

            PublishConnectionSignal(connected);
        }

        private void HandleAccFrameReceived(PolarPmdAccFrame frame)
        {
            if (breathingTracker != null)
                breathingTracker.SubmitAccFrame(frame);

            if (!publishToRawSignalRegistry || frame.Samples == null || frame.Samples.Length == 0)
                return;

            if (publishPerSample)
            {
                for (int i = 0; i < frame.Samples.Length; i++)
                    PublishSample(frame.Samples[i]);
                return;
            }

            PublishSample(frame.Samples[frame.Samples.Length - 1]);
        }

        private void HandleRawPmdData(byte[] payload)
        {
            if (breathingTracker != null)
                breathingTracker.SubmitRawPmdPacket(payload);
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

        private void PublishSample(PolarAccSampleMg sample)
        {
            string prefix = NormalizePrefix();
            if (string.IsNullOrEmpty(prefix))
                return;

            Vector3 g = sample.ToG();
            float x = g.x;
            float y = g.y;
            float z = g.z;

            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_x_g", x);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_y_g", y);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_z_g", z);
            PEBiofeedbackRawSignalRegistry.Publish($"{prefix}_mag_g", Mathf.Sqrt((x * x) + (y * y) + (z * z)));
        }

        private string NormalizePrefix()
        {
            return string.IsNullOrWhiteSpace(rawSignalPrefix) ? string.Empty : rawSignalPrefix.Trim();
        }
    }
}

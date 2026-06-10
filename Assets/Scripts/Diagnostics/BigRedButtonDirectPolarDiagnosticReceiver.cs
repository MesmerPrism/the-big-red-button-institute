using System;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar;
using UnityEngine;

namespace TheBigRedButtonInstitute.Diagnostics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-18)]
    public sealed class BigRedButtonDirectPolarDiagnosticReceiver : MonoBehaviour
    {
        const long UnixEpochTicks = 621355968000000000L;

        [SerializeField] PolarH10RuntimeManager polarRuntimeManager;
        [SerializeField] PolarUnifiedModule polarUnifiedModule;
        [SerializeField] BigRedButtonDiagnosticComparisonController comparisonController;
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool recordHeartRateNotifications = true;
        [SerializeField] bool recordRrIntervalNotifications = true;
        [SerializeField] bool recordDecodedPmdFrames = true;
        [SerializeField] bool recordRawPmdPackets;

        PolarUnifiedModule _subscribedModule;
        long _heartRateSequence;
        long _pmdSequence;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            Subscribe();
        }

        void Update()
        {
            if (!autoResolveReferences)
            {
                return;
            }

            var previousModule = polarUnifiedModule;
            ResolveReferences(forceRefresh: false);
            if (!ReferenceEquals(previousModule, polarUnifiedModule))
            {
                Unsubscribe(previousModule);
                Subscribe();
            }
        }

        void OnDisable()
        {
            Unsubscribe(_subscribedModule);
        }

        public void ConfigureReferences(
            PolarH10RuntimeManager runtimeManager,
            BigRedButtonDiagnosticComparisonController controller)
        {
            polarRuntimeManager = runtimeManager;
            comparisonController = controller;
            polarUnifiedModule = runtimeManager != null ? runtimeManager.PolarUnifiedModule : polarUnifiedModule;
            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        void Subscribe()
        {
            if (polarUnifiedModule == null || ReferenceEquals(_subscribedModule, polarUnifiedModule))
            {
                return;
            }

            Unsubscribe(_subscribedModule);
            polarUnifiedModule.HeartRateReceived += HandleHeartRateReceived;
            polarUnifiedModule.RrIntervalsReceived += HandleRrIntervalsReceived;
            polarUnifiedModule.PmdDataReceived += HandlePmdDataReceived;
            polarUnifiedModule.EcgFrameReceived += HandleEcgFrameReceived;
            polarUnifiedModule.AccFrameReceived += HandleAccFrameReceived;
            _subscribedModule = polarUnifiedModule;
        }

        void Unsubscribe(PolarUnifiedModule module)
        {
            if (module == null)
            {
                _subscribedModule = null;
                return;
            }

            module.HeartRateReceived -= HandleHeartRateReceived;
            module.RrIntervalsReceived -= HandleRrIntervalsReceived;
            module.PmdDataReceived -= HandlePmdDataReceived;
            module.EcgFrameReceived -= HandleEcgFrameReceived;
            module.AccFrameReceived -= HandleAccFrameReceived;

            if (ReferenceEquals(_subscribedModule, module))
            {
                _subscribedModule = null;
            }
        }

        void HandleHeartRateReceived(ushort bpm)
        {
            if (!recordHeartRateNotifications)
            {
                return;
            }

            RecordDirectPolarSample(
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarHeartRate,
                ++_heartRateSequence,
                Mathf.Clamp01(bpm / 220f),
                0L,
                "polar-hr");
        }

        void HandleRrIntervalsReceived(float[] rrIntervalsMs)
        {
            if (!recordRrIntervalNotifications || rrIntervalsMs == null || rrIntervalsMs.Length == 0)
            {
                return;
            }

            var lastRrMs = 0f;
            for (var i = 0; i < rrIntervalsMs.Length; i++)
            {
                var rrMs = rrIntervalsMs[i];
                if (rrMs > 0f && !float.IsNaN(rrMs) && !float.IsInfinity(rrMs))
                {
                    lastRrMs = rrMs;
                }
            }

            if (lastRrMs <= 0f)
            {
                return;
            }

            RecordDirectPolarSample(
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarHeartRate,
                ++_heartRateSequence,
                Mathf.Clamp01((60000f / lastRrMs) / 220f),
                0L,
                "polar-rr");
        }

        void HandlePmdDataReceived(byte[] payload)
        {
            if (!recordRawPmdPackets || payload == null)
            {
                return;
            }

            RecordDirectPolarSample(
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarPmd,
                ++_pmdSequence,
                Mathf.Clamp01(payload.Length / 512f),
                0L,
                "polar-pmd-raw");
        }

        void HandleEcgFrameReceived(PolarPmdEcgFrame frame)
        {
            if (!recordDecodedPmdFrames)
            {
                return;
            }

            RecordDirectPolarSample(
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarPmd,
                ++_pmdSequence,
                NormalizeEcgValue(frame),
                TicksToUnixTimeNanoseconds(frame.ReceivedUtcTicks),
                "polar-pmd-ecg");
        }

        void HandleAccFrameReceived(PolarPmdAccFrame frame)
        {
            if (!recordDecodedPmdFrames)
            {
                return;
            }

            RecordDirectPolarSample(
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolarPmd,
                ++_pmdSequence,
                NormalizeAccValue(frame),
                TicksToUnixTimeNanoseconds(frame.ReceivedUtcTicks),
                "polar-pmd-acc");
        }

        void RecordDirectPolarSample(
            BigRedButtonDiagnosticRouteId routeId,
            long sequence,
            float value01,
            long sourceTimeUnixNs,
            string label)
        {
            if (comparisonController == null)
            {
                ResolveReferences(forceRefresh: false);
            }

            if (comparisonController == null)
            {
                return;
            }

            comparisonController.RecordRouteSample(
                routeId,
                new BigRedButtonDiagnosticSample(
                    sequence,
                    value01,
                    sourceTimeUnixNs,
                    0L,
                    BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow),
                    label),
                acceptedPulse: false);
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (polarRuntimeManager == null || forceRefresh)
            {
                polarRuntimeManager = Application.isPlaying
                    ? PolarH10RuntimeManager.EnsureRuntimeExists()
                    : FindAnyObjectByType<PolarH10RuntimeManager>();
            }

            if (polarUnifiedModule == null || forceRefresh)
            {
                polarUnifiedModule = polarRuntimeManager != null && polarRuntimeManager.PolarUnifiedModule != null
                    ? polarRuntimeManager.PolarUnifiedModule
                    : FindAnyObjectByType<PolarUnifiedModule>();
            }

            if (comparisonController == null || forceRefresh)
            {
                comparisonController = GetComponent<BigRedButtonDiagnosticComparisonController>() ??
                                       FindAnyObjectByType<BigRedButtonDiagnosticComparisonController>();
            }
        }

        static float NormalizeAccValue(PolarPmdAccFrame frame)
        {
            if (frame.Samples == null || frame.Samples.Length == 0)
            {
                return 0f;
            }

            var g = frame.Samples[0].ToG();
            return Mathf.Clamp01(g.magnitude / 4f);
        }

        static float NormalizeEcgValue(PolarPmdEcgFrame frame)
        {
            if (frame.MicroVolts == null || frame.MicroVolts.Length == 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(Mathf.Abs(frame.MicroVolts[0]) / 10000f);
        }

        static long TicksToUnixTimeNanoseconds(long utcTicks)
        {
            if (utcTicks <= UnixEpochTicks)
            {
                return 0L;
            }

            return (utcTicks - UnixEpochTicks) * 100L;
        }
    }
}

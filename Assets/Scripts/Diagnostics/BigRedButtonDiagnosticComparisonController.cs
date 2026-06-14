using System;
using System.Collections.Generic;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.VR;
using UnityEngine;

namespace TheBigRedButtonInstitute.Diagnostics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-18)]
    public sealed class BigRedButtonDiagnosticComparisonController : MonoBehaviour
    {
        readonly BigRedButtonDiagnosticRouteTable _routes = new();

        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] PolarHeartbeatButtonDriver polarHeartbeatButtonDriver;
        [SerializeField] BigRedButtonDirectPolarDiagnosticReceiver directPolarReceiver;
        [SerializeField] BigRedButtonDirectOscDriveReceiver directOscReceiver;
        [SerializeField] BigRedButtonDirectLslDriveReceiver directLslReceiver;
        [SerializeField] bool autoResolveReferences = true;

        long _polarSequence;
        bool _polarSubscribed;

        public BigRedButtonDiagnosticRouteTable Routes => _routes;
        public string CompactSummary => _routes.BuildCompactSummary();

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
            ConfigureChildren();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            ConfigureChildren();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        public void ConfigureReferences(
            QuestVrInputManager manager,
            PolarHeartbeatButtonDriver polarDriver,
            BigRedButtonDirectPolarDiagnosticReceiver polarReceiver,
            BigRedButtonDirectOscDriveReceiver oscReceiver,
            BigRedButtonDirectLslDriveReceiver lslReceiver)
        {
            inputManager = manager;
            polarHeartbeatButtonDriver = polarDriver;
            directPolarReceiver = polarReceiver;
            directOscReceiver = oscReceiver;
            directLslReceiver = lslReceiver;
            ConfigureChildren();
        }

        public void RecordRouteSample(
            BigRedButtonDiagnosticRouteId routeId,
            BigRedButtonDiagnosticSample sample,
            bool acceptedPulse)
        {
            _routes.RecordSample(routeId, sample, acceptedPulse);
        }

        public IReadOnlyList<string> BuildHudLines()
        {
            var lines = new List<string>();
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.DirectUnityOsc);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.DirectUnityBlePolar);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.DirectUnityBlePolarHeartRate);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.DirectUnityBlePolarPmd);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.DirectUnityLsl);
            return lines;
        }

        public static long UnixTimeNanoseconds(DateTimeOffset value)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return (value.ToUniversalTime().Ticks - epoch.Ticks) * 100L;
        }

        void Subscribe()
        {
            if (!_polarSubscribed && polarHeartbeatButtonDriver != null)
            {
                polarHeartbeatButtonDriver.HeartbeatPulseAccepted += HandlePolarHeartbeatPulseAccepted;
                _polarSubscribed = true;
            }
        }

        void Unsubscribe()
        {
            if (_polarSubscribed && polarHeartbeatButtonDriver != null)
            {
                polarHeartbeatButtonDriver.HeartbeatPulseAccepted -= HandlePolarHeartbeatPulseAccepted;
            }

            _polarSubscribed = false;
        }

        void HandlePolarHeartbeatPulseAccepted(float bpm)
        {
            RecordRouteSample(
                BigRedButtonDiagnosticRouteId.DirectUnityBlePolar,
                new BigRedButtonDiagnosticSample(
                    ++_polarSequence,
                    Mathf.Clamp01(bpm / 220f),
                    0L,
                    0L,
                    UnixTimeNanoseconds(DateTimeOffset.UtcNow),
                    "polar-heartbeat"),
                acceptedPulse: true);
        }

        void AppendRouteLine(List<string> lines, BigRedButtonDiagnosticRouteId routeId)
        {
            var stats = _routes.GetStats(routeId);
            var label = BigRedButtonDiagnosticRouteTable.RouteLabel(routeId);
            lines.Add($"{label}: rx {stats.ReceivedSamples:N0} pulse {stats.AcceptedPulses:N0} drop {stats.DroppedSamples:N0} dup {stats.DuplicateOrOutOfOrderSamples:N0}");
        }

        void ConfigureChildren()
        {
            if (directPolarReceiver != null)
            {
                directPolarReceiver.ConfigureReferences(
                    FindAnyObjectByType<PolarH10RuntimeManager>(),
                    this);
            }

            if (directOscReceiver != null)
            {
                directOscReceiver.ConfigureReferences(inputManager, this);
            }

            if (directLslReceiver != null)
            {
                directLslReceiver.ConfigureReferences(inputManager, this);
            }
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (inputManager == null || forceRefresh)
            {
                inputManager = GetComponent<QuestVrInputManager>() ?? FindAnyObjectByType<QuestVrInputManager>();
            }

            if (polarHeartbeatButtonDriver == null || forceRefresh)
            {
                polarHeartbeatButtonDriver = GetComponent<PolarHeartbeatButtonDriver>() ?? FindAnyObjectByType<PolarHeartbeatButtonDriver>();
            }

            if (directPolarReceiver == null || forceRefresh)
            {
                directPolarReceiver = GetComponent<BigRedButtonDirectPolarDiagnosticReceiver>() ?? FindAnyObjectByType<BigRedButtonDirectPolarDiagnosticReceiver>();
                if (directPolarReceiver == null && Application.isPlaying)
                {
                    directPolarReceiver = gameObject.AddComponent<BigRedButtonDirectPolarDiagnosticReceiver>();
                }
            }

            if (directOscReceiver == null || forceRefresh)
            {
                directOscReceiver = GetComponent<BigRedButtonDirectOscDriveReceiver>() ?? FindAnyObjectByType<BigRedButtonDirectOscDriveReceiver>();
            }

            if (directLslReceiver == null || forceRefresh)
            {
                directLslReceiver = GetComponent<BigRedButtonDirectLslDriveReceiver>() ?? FindAnyObjectByType<BigRedButtonDirectLslDriveReceiver>();
            }
        }
    }
}

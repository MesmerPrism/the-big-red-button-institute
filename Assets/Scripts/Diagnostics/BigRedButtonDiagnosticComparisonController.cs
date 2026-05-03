using System;
using System.Collections.Generic;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.RustyXrBroker;
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
        [SerializeField] RustyXrBrokerButtonDriver brokerButtonDriver;
        [SerializeField] RustyXrBrokerDriveSignalReceiver brokerDriveReceiver;
        [SerializeField] BigRedButtonDirectOscDriveReceiver directOscReceiver;
        [SerializeField] bool autoResolveReferences = true;

        long _polarSequence;
        bool _brokerSubscribed;
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
            RustyXrBrokerButtonDriver brokerDriver,
            RustyXrBrokerDriveSignalReceiver brokerReceiver,
            BigRedButtonDirectOscDriveReceiver oscReceiver)
        {
            inputManager = manager;
            polarHeartbeatButtonDriver = polarDriver;
            brokerButtonDriver = brokerDriver;
            brokerDriveReceiver = brokerReceiver;
            directOscReceiver = oscReceiver;
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
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.BrokerWebSocketOsc);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.DirectUnityLsl);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.BrokerWebSocketLsl);
            AppendRouteLine(lines, BigRedButtonDiagnosticRouteId.BrokerWebSocketSynthetic);
            return lines;
        }

        public static long UnixTimeNanoseconds(DateTimeOffset value)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return (value.ToUniversalTime().Ticks - epoch.Ticks) * 100L;
        }

        void Subscribe()
        {
            if (!_brokerSubscribed && brokerButtonDriver != null)
            {
                brokerButtonDriver.DrivePulseRequested += HandleBrokerDrivePulseRequested;
                _brokerSubscribed = true;
            }

            if (!_polarSubscribed && polarHeartbeatButtonDriver != null)
            {
                polarHeartbeatButtonDriver.HeartbeatPulseAccepted += HandlePolarHeartbeatPulseAccepted;
                _polarSubscribed = true;
            }
        }

        void Unsubscribe()
        {
            if (_brokerSubscribed && brokerButtonDriver != null)
            {
                brokerButtonDriver.DrivePulseRequested -= HandleBrokerDrivePulseRequested;
            }

            if (_polarSubscribed && polarHeartbeatButtonDriver != null)
            {
                polarHeartbeatButtonDriver.HeartbeatPulseAccepted -= HandlePolarHeartbeatPulseAccepted;
            }

            _brokerSubscribed = false;
            _polarSubscribed = false;
        }

        void HandleBrokerDrivePulseRequested(float value01)
        {
            var sequence = brokerDriveReceiver != null ? brokerDriveReceiver.LastSequenceId : 0L;
            var brokerTime = brokerDriveReceiver != null ? brokerDriveReceiver.LastBrokerTimeUnixNs : 0L;
            var route = brokerDriveReceiver != null &&
                        string.Equals(brokerDriveReceiver.LastStreamId, RustyXrBrokerDriveSignal.DefaultStream, StringComparison.Ordinal)
                ? BigRedButtonDiagnosticRouteId.BrokerWebSocketOsc
                : BigRedButtonDiagnosticRouteId.BrokerWebSocketSynthetic;

            RecordRouteSample(
                route,
                new BigRedButtonDiagnosticSample(
                    sequence,
                    value01,
                    0L,
                    brokerTime,
                    UnixTimeNanoseconds(DateTimeOffset.UtcNow),
                    brokerDriveReceiver != null ? brokerDriveReceiver.LastStreamId : "broker"),
                acceptedPulse: true);
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
            if (directOscReceiver != null)
            {
                directOscReceiver.ConfigureReferences(inputManager, this);
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

            if (brokerButtonDriver == null || forceRefresh)
            {
                brokerButtonDriver = GetComponent<RustyXrBrokerButtonDriver>() ?? FindAnyObjectByType<RustyXrBrokerButtonDriver>();
            }

            if (brokerDriveReceiver == null || forceRefresh)
            {
                brokerDriveReceiver = GetComponent<RustyXrBrokerDriveSignalReceiver>() ?? FindAnyObjectByType<RustyXrBrokerDriveSignalReceiver>();
            }

            if (directOscReceiver == null || forceRefresh)
            {
                directOscReceiver = GetComponent<BigRedButtonDirectOscDriveReceiver>() ?? FindAnyObjectByType<BigRedButtonDirectOscDriveReceiver>();
            }
        }
    }
}

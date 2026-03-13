using AstralKarateDojo.IndirectParticles.Biofeedback.Heartbeat;
using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-19)]
    public sealed class PolarHeartbeatButtonDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PolarH10RuntimeManager polarRuntimeManager;
        [SerializeField] TheBigRedButtonInstitute.VR.QuestVrInputManager inputManager;
        [SerializeField] BigRedButtonAnimationTester buttonAnimationTester;

        [Header("Triggering")]
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool requireTrackingState = true;
        [SerializeField] bool requireRealBeatSignal = true;
        [SerializeField, Range(0f, 1f)] float minimumConfidence01 = 0.5f;
        [SerializeField, Min(0.05f)] float minimumBeatIntervalSeconds = 0.25f;

        bool _subscribed;
        double _lastAcceptedBeatTimestamp;
        int _triggerCount;
        string _driveState = "idle";

        public int TriggerCount => _triggerCount;
        public string DriveStateLabel => string.IsNullOrWhiteSpace(_driveState) ? "idle" : _driveState;

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

            var previousRuntime = polarRuntimeManager;
            ResolveReferences(forceRefresh: false);
            if (!ReferenceEquals(previousRuntime, polarRuntimeManager))
            {
                Unsubscribe(previousRuntime);
                Subscribe();
            }
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        public void ConfigureReferences(
            PolarH10RuntimeManager runtimeManager,
            TheBigRedButtonInstitute.VR.QuestVrInputManager manager,
            BigRedButtonAnimationTester tester)
        {
            polarRuntimeManager = runtimeManager;
            inputManager = manager;
            buttonAnimationTester = tester;
        }

        void Subscribe()
        {
            if (_subscribed || polarRuntimeManager == null)
            {
                return;
            }

            polarRuntimeManager.HeartbeatSampleUpdated += HandleHeartbeatSampleUpdated;
            _subscribed = true;
            _driveState = polarRuntimeManager.IsPolarConnected ? "armed" : "waiting for connection";
        }

        void Unsubscribe()
        {
            Unsubscribe(polarRuntimeManager);
        }

        void Unsubscribe(PolarH10RuntimeManager runtimeManager)
        {
            if (!_subscribed || runtimeManager == null)
            {
                _subscribed = false;
                return;
            }

            runtimeManager.HeartbeatSampleUpdated -= HandleHeartbeatSampleUpdated;
            _subscribed = false;
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

            if (inputManager == null || forceRefresh)
            {
                inputManager = GetComponent<TheBigRedButtonInstitute.VR.QuestVrInputManager>();
                if (inputManager == null)
                {
                    inputManager = FindAnyObjectByType<TheBigRedButtonInstitute.VR.QuestVrInputManager>();
                }
            }

            if ((buttonAnimationTester == null || forceRefresh) && inputManager != null)
            {
                buttonAnimationTester = inputManager.ButtonAnimationTester;
            }

            if (buttonAnimationTester == null || forceRefresh)
            {
                buttonAnimationTester = FindAnyObjectByType<BigRedButtonAnimationTester>();
            }
        }

        void HandleHeartbeatSampleUpdated(PEHeartbeatSample sample)
        {
            if (!sample.IsConnected)
            {
                _driveState = "waiting for connection";
                return;
            }

            if (requireTrackingState && sample.TrackingState != PEHeartbeatTrackingState.Tracking)
            {
                _driveState = "waiting for stable tracking";
                return;
            }

            if (sample.Confidence01 < minimumConfidence01)
            {
                _driveState = "waiting for confidence";
                return;
            }

            var beatDetected = requireRealBeatSignal ? sample.RealBeatDetectedThisFrame : sample.BeatDetectedThisFrame;
            if (!beatDetected)
            {
                _driveState = $"armed @ {sample.Bpm:0} bpm";
                return;
            }

            if (_lastAcceptedBeatTimestamp > 0d &&
                (sample.Timestamp - _lastAcceptedBeatTimestamp) < minimumBeatIntervalSeconds)
            {
                return;
            }

            ResolveReferences(forceRefresh: false);

            if (inputManager != null)
            {
                if (inputManager.TriggerButtonPressFromRuntime())
                {
                    _lastAcceptedBeatTimestamp = sample.Timestamp;
                    _triggerCount++;
                    _driveState = $"pulsing @ {sample.Bpm:0} bpm";
                }

                return;
            }

            if (buttonAnimationTester == null)
            {
                _driveState = "button animation missing";
                return;
            }

            buttonAnimationTester.PlayPressed();
            _lastAcceptedBeatTimestamp = sample.Timestamp;
            _triggerCount++;
            _driveState = $"pulsing @ {sample.Bpm:0} bpm";
        }
    }
}

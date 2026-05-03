using TheBigRedButtonInstitute.RustyXrBroker;
using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-15)]
    public sealed class QuestVrRustyXrBrokerButtonBridge : MonoBehaviour
    {
        [SerializeField] RustyXrBrokerButtonDriver brokerButtonDriver;
        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool useFullButtonPress = true;

        int _acceptedPulses;
        string _lastState = "idle";

        public int AcceptedPulses => _acceptedPulses;
        public string LastState => string.IsNullOrWhiteSpace(_lastState) ? "idle" : _lastState;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            if (brokerButtonDriver != null)
            {
                brokerButtonDriver.DrivePulseRequested += HandleDrivePulseRequested;
            }
        }

        void OnDisable()
        {
            if (brokerButtonDriver != null)
            {
                brokerButtonDriver.DrivePulseRequested -= HandleDrivePulseRequested;
            }
        }

        public void ConfigureReferences(RustyXrBrokerButtonDriver driver, QuestVrInputManager manager)
        {
            brokerButtonDriver = driver;
            inputManager = manager;
        }

        void HandleDrivePulseRequested(float value01)
        {
            ResolveReferences(forceRefresh: false);
            if (inputManager == null)
            {
                _lastState = "input missing";
                return;
            }

            var triggered = useFullButtonPress
                ? inputManager.TriggerButtonPressFromRuntime()
                : inputManager.TriggerButtonBlinkFromRuntime();

            if (triggered)
            {
                _acceptedPulses++;
                _lastState = $"pressed {value01:0.00}";
                Debug.Log($"[QuestVrRustyXrBrokerButtonBridge] broker pulse drove button value01={value01:0.000}", this);
            }
            else
            {
                _lastState = "button missing";
                Debug.LogWarning($"[QuestVrRustyXrBrokerButtonBridge] broker pulse could not drive button value01={value01:0.000}", this);
            }
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (brokerButtonDriver == null || forceRefresh)
            {
                brokerButtonDriver = GetComponent<RustyXrBrokerButtonDriver>() ?? FindAnyObjectByType<RustyXrBrokerButtonDriver>();
            }

            if (inputManager == null || forceRefresh)
            {
                inputManager = GetComponent<QuestVrInputManager>() ?? FindAnyObjectByType<QuestVrInputManager>();
            }
        }
    }
}

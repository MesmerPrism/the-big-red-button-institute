using System;
using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-16)]
    public sealed class RustyXrBrokerButtonDriver : MonoBehaviour
    {
        [SerializeField] RustyXrBrokerDriveSignalReceiver driveSignalReceiver;
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField, Range(0f, 1f)] float triggerThreshold01 = 0.5f;
        [SerializeField, Min(0f)] float minimumTriggerIntervalSeconds = 0.25f;
        [SerializeField] bool triggerOnRisingEdgeOnly = true;

        float _previousValue01;
        double _lastTriggerTime = -1d;
        int _triggerCount;
        string _driveState = "idle";

        public event Action<float> DrivePulseRequested;

        public float CurrentValue01 => driveSignalReceiver != null ? driveSignalReceiver.Value01 : 0f;
        public int TriggerCount => _triggerCount;
        public string DriveStateLabel => string.IsNullOrWhiteSpace(_driveState) ? "idle" : _driveState;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
            _previousValue01 = CurrentValue01;
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            _previousValue01 = CurrentValue01;
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);
            ApplyBrokerDriveValue(CurrentValue01, Time.unscaledTimeAsDouble, true);
        }

        public void ConfigureReferences(RustyXrBrokerDriveSignalReceiver receiver)
        {
            driveSignalReceiver = receiver;
            _previousValue01 = CurrentValue01;
        }

        public bool ApplyBrokerDriveValue(float value01, double nowSeconds, bool notify)
        {
            var clamped = Mathf.Clamp01(value01);
            var shouldTrigger = ShouldTrigger(_previousValue01, clamped, triggerThreshold01, triggerOnRisingEdgeOnly);
            _previousValue01 = clamped;

            if (!shouldTrigger)
            {
                _driveState = $"armed {clamped:0.00}";
                return false;
            }

            if (_lastTriggerTime >= 0d && nowSeconds - _lastTriggerTime < minimumTriggerIntervalSeconds)
            {
                _driveState = $"throttled {clamped:0.00}";
                return false;
            }

            _lastTriggerTime = nowSeconds;
            _triggerCount++;
            _driveState = $"pulse {clamped:0.00}";
            if (notify)
            {
                DrivePulseRequested?.Invoke(clamped);
            }

            return true;
        }

        public static bool ShouldTrigger(float previousValue01, float nextValue01, float threshold01, bool risingEdgeOnly)
        {
            var threshold = Mathf.Clamp01(threshold01);
            var previous = Mathf.Clamp01(previousValue01);
            var next = Mathf.Clamp01(nextValue01);
            if (risingEdgeOnly)
            {
                return previous < threshold && next >= threshold;
            }

            return next >= threshold;
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (driveSignalReceiver == null || forceRefresh)
            {
                driveSignalReceiver = GetComponent<RustyXrBrokerDriveSignalReceiver>() ?? FindAnyObjectByType<RustyXrBrokerDriveSignalReceiver>();
            }
        }
    }
}

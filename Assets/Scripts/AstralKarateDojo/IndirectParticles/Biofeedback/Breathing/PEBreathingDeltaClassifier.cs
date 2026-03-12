using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Breathing
{
    internal struct PEBreathingDeltaClassifier
    {
        private bool _hasLastSample;
        private float _lastVolume01;
        private double _lastTimestamp;

        public void Reset()
        {
            _hasLastSample = false;
            _lastVolume01 = 0f;
            _lastTimestamp = 0.0;
        }

        public PEBreathingState Classify(
            float volume01,
            bool hasSignal,
            double timestamp,
            float deltaThreshold,
            float staleTimeoutSeconds)
        {
            if (!hasSignal)
                return PEBreathingState.BadTracking;

            float clampedVolume = Mathf.Clamp01(volume01);
            float absStaleTimeout = Mathf.Max(0.05f, staleTimeoutSeconds);
            float absDeltaThreshold = Mathf.Max(0.0001f, deltaThreshold);

            if (!_hasLastSample)
            {
                _hasLastSample = true;
                _lastVolume01 = clampedVolume;
                _lastTimestamp = timestamp;
                return PEBreathingState.Pausing;
            }

            if ((timestamp - _lastTimestamp) > absStaleTimeout)
            {
                _lastVolume01 = clampedVolume;
                _lastTimestamp = timestamp;
                return PEBreathingState.BadTracking;
            }

            float delta = clampedVolume - _lastVolume01;
            _lastVolume01 = clampedVolume;
            _lastTimestamp = timestamp;

            if (delta > absDeltaThreshold)
                return PEBreathingState.Inhaling;
            if (delta < -absDeltaThreshold)
                return PEBreathingState.Exhaling;

            return PEBreathingState.Pausing;
        }
    }
}

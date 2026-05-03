using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat
{
    internal struct PEHeartbeatPeakDetector
    {
        private bool _wasAboveThreshold;
        private double _lastBeatTime;
        private float _estimatedBpm;

        public float EstimatedBpm => _estimatedBpm;

        public void Reset()
        {
            _wasAboveThreshold = false;
            _lastBeatTime = 0.0;
            _estimatedBpm = 0f;
        }

        public bool UpdateFromPulse(
            float pulse01,
            double now,
            float threshold,
            float refractorySeconds,
            float bpmSmoothingSpeed)
        {
            bool above = pulse01 >= Mathf.Clamp01(threshold);
            bool risingCrossing = above && !_wasAboveThreshold;
            _wasAboveThreshold = above;

            if (!risingCrossing)
                return false;

            double safeRefractory = Mathf.Max(0.1f, refractorySeconds);
            if (_lastBeatTime > 0.0 && (now - _lastBeatTime) < safeRefractory)
                return false;

            if (_lastBeatTime > 0.0)
            {
                float dt = (float)(now - _lastBeatTime);
                if (dt > 0.0001f)
                {
                    float instantBpm = Mathf.Clamp(60f / dt, 20f, 260f);
                    float blend = 1f - Mathf.Exp(-Mathf.Max(0f, bpmSmoothingSpeed) * Mathf.Max(0f, Time.unscaledDeltaTime));
                    _estimatedBpm = _estimatedBpm <= 0f
                        ? instantBpm
                        : Mathf.Lerp(_estimatedBpm, instantBpm, blend);
                }
            }

            _lastBeatTime = now;
            return true;
        }
    }
}

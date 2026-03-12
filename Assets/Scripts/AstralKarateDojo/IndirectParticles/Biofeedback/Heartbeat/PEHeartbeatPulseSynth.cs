using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Heartbeat
{
    internal struct PEHeartbeatPulseSynth
    {
        private float _pulse01;
        private bool _beatThisFrame;

        public bool BeatDetectedThisFrame => _beatThisFrame;
        public float CurrentPulse01 => _pulse01;

        public void Reset(float baselinePulse01 = 0f)
        {
            _pulse01 = Mathf.Clamp01(baselinePulse01);
            _beatThisFrame = false;
        }

        public void TriggerBeatNow(double now, float pulsePeak01 = 1f)
        {
            _ = now;
            _pulse01 = Mathf.Clamp01(pulsePeak01);
            _beatThisFrame = true;
        }

        public void Update(double now, float decaySeconds, float baselinePulse01, float pulsePeak01)
        {
            _ = now;
            _ = pulsePeak01;
            _beatThisFrame = false;

            float safeBaseline = Mathf.Clamp01(baselinePulse01);
            float safeDecay = Mathf.Max(0.01f, decaySeconds);

            if (_pulse01 > safeBaseline)
            {
                float dt = Mathf.Max(0f, Time.unscaledDeltaTime);
                float blend = 1f - Mathf.Exp(-dt / safeDecay);
                _pulse01 = Mathf.Lerp(_pulse01, safeBaseline, blend);
            }
            else
            {
                _pulse01 = safeBaseline;
            }
        }
    }
}

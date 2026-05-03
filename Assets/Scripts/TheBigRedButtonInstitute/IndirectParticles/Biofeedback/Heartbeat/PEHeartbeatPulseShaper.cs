using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat
{
    internal struct PEHeartbeatPulseShaper
    {
        public readonly struct UpdateInput
        {
            public readonly double Now;
            public readonly float TargetBpm;
            public readonly int ExplicitBeatTriggers;
            public readonly bool DetectBeatsFromPulseInput;
            public readonly float PulseInput01;
            public readonly float PulseBeatThreshold01;
            public readonly float PulseRefractorySeconds;
            public readonly float BpmSmoothingSpeed;
            public readonly float PulseDecaySeconds;
            public readonly float PulseBaseline01;
            public readonly float PulsePeak01;

            public UpdateInput(
                double now,
                float targetBpm,
                int explicitBeatTriggers,
                bool detectBeatsFromPulseInput,
                float pulseInput01,
                float pulseBeatThreshold01,
                float pulseRefractorySeconds,
                float bpmSmoothingSpeed,
                float pulseDecaySeconds,
                float pulseBaseline01,
                float pulsePeak01)
            {
                Now = now;
                TargetBpm = targetBpm;
                ExplicitBeatTriggers = Mathf.Max(0, explicitBeatTriggers);
                DetectBeatsFromPulseInput = detectBeatsFromPulseInput;
                PulseInput01 = Mathf.Clamp01(pulseInput01);
                PulseBeatThreshold01 = Mathf.Clamp01(pulseBeatThreshold01);
                PulseRefractorySeconds = Mathf.Max(0.1f, pulseRefractorySeconds);
                BpmSmoothingSpeed = Mathf.Max(0f, bpmSmoothingSpeed);
                PulseDecaySeconds = Mathf.Max(0.01f, pulseDecaySeconds);
                PulseBaseline01 = Mathf.Clamp01(pulseBaseline01);
                PulsePeak01 = Mathf.Clamp01(pulsePeak01);
            }
        }

        public readonly struct UpdateOutput
        {
            public readonly float Pulse01;
            public readonly bool BeatDetectedThisFrame;
            public readonly bool BeatDetectedFromPulseInput;
            public readonly float EstimatedBpmFromPulseInput;

            public UpdateOutput(
                float pulse01,
                bool beatDetectedThisFrame,
                bool beatDetectedFromPulseInput,
                float estimatedBpmFromPulseInput)
            {
                Pulse01 = Mathf.Clamp01(pulse01);
                BeatDetectedThisFrame = beatDetectedThisFrame;
                BeatDetectedFromPulseInput = beatDetectedFromPulseInput;
                EstimatedBpmFromPulseInput = Mathf.Max(0f, estimatedBpmFromPulseInput);
            }
        }

        private PEHeartbeatPulseSynth _pulseSynth;
        private PEHeartbeatPeakDetector _peakDetector;

        public float CurrentPulse01 => _pulseSynth.CurrentPulse01;

        public void Reset(float baselinePulse01)
        {
            _pulseSynth.Reset(Mathf.Clamp01(baselinePulse01));
            _peakDetector.Reset();
        }

        public UpdateOutput Update(UpdateInput input)
        {
            bool beatFromPulseInput = false;
            float estimatedPulseBpm = 0f;

            if (input.DetectBeatsFromPulseInput)
            {
                beatFromPulseInput = _peakDetector.UpdateFromPulse(
                    input.PulseInput01,
                    input.Now,
                    input.PulseBeatThreshold01,
                    input.PulseRefractorySeconds,
                    input.BpmSmoothingSpeed);
                estimatedPulseBpm = _peakDetector.EstimatedBpm;
            }

            _pulseSynth.Update(
                input.Now,
                input.PulseDecaySeconds,
                input.PulseBaseline01,
                input.PulsePeak01);

            if (input.ExplicitBeatTriggers > 0 || beatFromPulseInput)
                _pulseSynth.TriggerBeatNow(input.Now, input.PulsePeak01);

            return new UpdateOutput(
                pulse01: _pulseSynth.CurrentPulse01,
                beatDetectedThisFrame: _pulseSynth.BeatDetectedThisFrame || beatFromPulseInput,
                beatDetectedFromPulseInput: beatFromPulseInput,
                estimatedBpmFromPulseInput: estimatedPulseBpm);
        }
    }
}

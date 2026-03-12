using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Breathing
{
    internal struct PEBreathingStreamProcessor
    {
        public readonly struct UpdateInput
        {
            public readonly float RawInput;
            public readonly bool IsCalibrated;
            public readonly double Timestamp;
            public readonly bool UseRawMinMaxMapping;
            public readonly float RawMin;
            public readonly float RawMax;
            public readonly bool Invert01;
            public readonly float DeltaThreshold;
            public readonly float StaleTimeoutSeconds;

            public UpdateInput(
                float rawInput,
                bool isCalibrated,
                double timestamp,
                bool useRawMinMaxMapping,
                float rawMin,
                float rawMax,
                bool invert01,
                float deltaThreshold,
                float staleTimeoutSeconds)
            {
                RawInput = rawInput;
                IsCalibrated = isCalibrated;
                Timestamp = timestamp;
                UseRawMinMaxMapping = useRawMinMaxMapping;
                RawMin = rawMin;
                RawMax = rawMax;
                Invert01 = invert01;
                DeltaThreshold = deltaThreshold;
                StaleTimeoutSeconds = staleTimeoutSeconds;
            }
        }

        public readonly struct UpdateOutput
        {
            public readonly float Volume01;
            public readonly PEBreathingState State;
            public readonly bool IsCalibrated;
            public readonly bool HasSample;

            public UpdateOutput(
                float volume01,
                PEBreathingState state,
                bool isCalibrated,
                bool hasSample)
            {
                Volume01 = Mathf.Clamp01(volume01);
                State = state;
                IsCalibrated = isCalibrated;
                HasSample = hasSample;
            }
        }

        private PEBreathingDeltaClassifier _classifier;
        private bool _hasReceivedVolume;
        private double _lastCalibratedSampleTimestamp;

        public bool HasReceivedVolume => _hasReceivedVolume;

        public void Reset()
        {
            _classifier.Reset();
            _hasReceivedVolume = false;
            _lastCalibratedSampleTimestamp = 0.0;
        }

        public bool IsStale(double now, float staleTimeoutSeconds)
        {
            if (!_hasReceivedVolume)
                return false;

            return (now - _lastCalibratedSampleTimestamp) > Mathf.Max(0.05f, staleTimeoutSeconds);
        }

        public bool TryProcess(UpdateInput input, out UpdateOutput output)
        {
            float mappedVolume01 = MapToNormalized01(
                input.RawInput,
                input.UseRawMinMaxMapping,
                input.RawMin,
                input.RawMax,
                input.Invert01);

            _hasReceivedVolume = true;

            if (!input.IsCalibrated)
            {
                output = new UpdateOutput(
                    volume01: mappedVolume01,
                    state: PEBreathingState.BadTracking,
                    isCalibrated: false,
                    hasSample: false);
                return false;
            }

            _lastCalibratedSampleTimestamp = input.Timestamp;
            PEBreathingState state = _classifier.Classify(
                mappedVolume01,
                hasSignal: true,
                timestamp: _lastCalibratedSampleTimestamp,
                deltaThreshold: input.DeltaThreshold,
                staleTimeoutSeconds: input.StaleTimeoutSeconds);

            output = new UpdateOutput(
                volume01: mappedVolume01,
                state: state,
                isCalibrated: true,
                hasSample: true);
            return true;
        }

        private static float MapToNormalized01(
            float raw,
            bool useRawMinMaxMapping,
            float rawMin,
            float rawMax,
            bool invert01)
        {
            float value01;
            if (useRawMinMaxMapping)
            {
                if (Mathf.Approximately(rawMin, rawMax))
                    value01 = 0.5f;
                else
                    value01 = Mathf.InverseLerp(rawMin, rawMax, raw);
            }
            else
            {
                value01 = raw;
            }

            value01 = Mathf.Clamp01(value01);
            if (invert01)
                value01 = 1f - value01;

            return value01;
        }
    }
}

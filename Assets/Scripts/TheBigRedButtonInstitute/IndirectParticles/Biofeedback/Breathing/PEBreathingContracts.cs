using System;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Breathing
{
    public enum PEBreathingState
    {
        Inhaling = 0,
        Exhaling = 1,
        Pausing = 2,
        BadTracking = 3
    }

    [Serializable]
    public struct PEBreathingSample
    {
        public double Timestamp;
        public float ElapsedSeconds;
        public string ModuleId;
        public PEBreathingState State;
        public float Volume01;
        public bool IsCalibrated;
        public bool HasSample;
    }

    public interface IPEBreathingSignalModule
    {
        string ModuleId { get; }
        bool IsProcessingEnabled { get; }
        bool HasSample { get; }
        bool IsCalibrated { get; }
        PEBreathingState CurrentState { get; }
        float CurrentVolume01 { get; }
        event Action<PEBreathingSample> SampleUpdated;
        void SetProcessingEnabled(bool enabled);
    }
}

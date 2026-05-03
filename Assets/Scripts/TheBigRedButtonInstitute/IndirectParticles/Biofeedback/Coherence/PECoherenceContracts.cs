using System;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Coherence
{
    public enum PECoherenceTrackingState
    {
        Unavailable = 0,
        Stale = 1,
        Tracking = 2
    }

    [Serializable]
    public struct PECoherenceSample
    {
        public double Timestamp;
        public float ElapsedSeconds;
        public string ModuleId;
        public PECoherenceTrackingState TrackingState;
        public bool IsConnected;
        public bool HasSample;
        public float Coherence01;
        public float Confidence01;
        public float HeartbeatBpm;
        public float HeartbeatIbiMs;
    }

    public interface IPECoherenceSignalModule
    {
        string ModuleId { get; }
        bool IsProcessingEnabled { get; }
        bool IsConnected { get; }
        bool HasSample { get; }
        PECoherenceTrackingState TrackingState { get; }
        float CurrentCoherence01 { get; }
        float Confidence01 { get; }
        float CurrentHeartbeatBpm { get; }
        float CurrentHeartbeatIbiMs { get; }
        event Action<PECoherenceSample> SampleUpdated;
        void SetProcessingEnabled(bool enabled);
    }
}

using System;

namespace TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat
{
    public enum PEHeartbeatTrackingState
    {
        Unavailable = 0,
        Stale = 1,
        Tracking = 2
    }

    [Serializable]
    public struct PEHeartbeatSample
    {
        public double Timestamp;
        public float ElapsedSeconds;
        public string ModuleId;
        public PEHeartbeatTrackingState TrackingState;
        public bool IsConnected;
        public bool HasSample;
        public bool BeatDetectedThisFrame;
        public bool RealBeatDetectedThisFrame;
        public float Bpm;
        public float IbiMs;
        public float Pulse01;
        public float Confidence01;
    }

    public interface IPEHeartbeatSignalModule
    {
        string ModuleId { get; }
        bool IsProcessingEnabled { get; }
        bool IsConnected { get; }
        bool HasSample { get; }
        bool BeatDetectedThisFrame { get; }
        bool RealBeatDetectedThisFrame { get; }
        PEHeartbeatTrackingState TrackingState { get; }
        float CurrentBpm { get; }
        float CurrentIbiMs { get; }
        float CurrentPulse01 { get; }
        float Confidence01 { get; }
        event Action<PEHeartbeatSample> SampleUpdated;
        void SetProcessingEnabled(bool enabled);
    }
}

using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    public sealed class RustyXrBrokerDriveSignalReceiver : MonoBehaviour
    {
        [SerializeField] string streamId = RustyXrBrokerDriveSignal.DefaultStream;
        [SerializeField, Range(0f, 1f)] float value01;

        public string StreamId
        {
            get => streamId;
            set => streamId = string.IsNullOrWhiteSpace(value) ? RustyXrBrokerDriveSignal.DefaultStream : value;
        }

        public float Value01 => value01;

        public bool ApplyStreamEventJson(string json)
        {
            if (!RustyXrBrokerProtocol.TryParseStreamEvent(json, out var streamEvent))
            {
                return false;
            }

            return ApplyStreamEvent(streamEvent);
        }

        public bool ApplyStreamEvent(RustyXrBrokerStreamEvent streamEvent)
        {
            if (!RustyXrBrokerDriveSignal.TryExtractValue01(streamEvent, streamId, out var nextValue))
            {
                return false;
            }

            value01 = nextValue;
            return true;
        }
    }

    public static class RustyXrBrokerDriveSignal
    {
        public const string DefaultStream = "osc:/rusty-xr/drive/radius";
        public const string DefaultAddress = "/rusty-xr/drive/radius";

        public static bool TryExtractValue01(
            RustyXrBrokerStreamEvent streamEvent,
            string expectedStream,
            out float value01)
        {
            value01 = 0f;
            if (streamEvent == null ||
                streamEvent.payload == null ||
                streamEvent.stream != expectedStream ||
                streamEvent.payload.address != DefaultAddress)
            {
                return false;
            }

            value01 = Mathf.Clamp01(streamEvent.payload.value01);
            return true;
        }
    }
}

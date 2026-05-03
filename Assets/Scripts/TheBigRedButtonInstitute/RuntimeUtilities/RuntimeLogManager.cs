using UnityEngine;

namespace TheBigRedButtonInstitute.RuntimeUtilities
{
    /// <summary>
    /// Minimal compatibility shim for BLE transport imports.
    /// This project does not use routed runtime log channels, so channels stay disabled.
    /// </summary>
    public sealed class RuntimeLogManager : MonoBehaviour
    {
        public enum RuntimeLogChannel
        {
            BleVerboseMessages = 0,
            BleUnityMirror = 1,
            BleAndroidMirror = 2
        }

        public static bool IsRuntimeLoggingEnabled => false;

        public static bool IsLogChannelEnabled(RuntimeLogChannel channel)
        {
            _ = channel;
            return false;
        }
    }
}

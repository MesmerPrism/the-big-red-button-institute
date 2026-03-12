using UnityEngine;

namespace AstralKarateDojo.RuntimeUtilities
{
    /// <summary>
    /// Minimal compatibility shim for Astral BLE transport imports.
    /// This project does not use Astral's runtime log routing, so channels stay disabled.
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

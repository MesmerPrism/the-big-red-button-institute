using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

namespace AstralKarateDojo.Biofeedback.Transport.BLE
{
    /// <summary>
    /// Sole owner of runtime permission requests and user-facing flows for BLE.
    /// Transport (BleCentral) only queries readiness.
    ///
    /// Notes:
    /// - Android 12+ requires BLUETOOTH_SCAN/CONNECT, and some OEMs still gate scans on location.
    /// - Android 11 and below require ACCESS_FINE_LOCATION and Location toggle ON for scanning.
    /// - For a unified BLE + Classic permission flow, use BluetoothPermissionsBootstrap instead.
    /// </summary>
    public class BlePermissionBootstrap : MonoBehaviour
    {
        [Header("Startup")]
        [SerializeField] private bool autoRequestOnStart = false;

        [Header("Android 12+ (API 31+)")]
        [Tooltip("Request ADVERTISE if your app actually advertises.")]
        public bool requestAdvertise = false;

        [Tooltip("Also request ACCESS_FINE_LOCATION on Android 12+ to cope with OEM stacks that still gate scans.")]
        public bool requestLocationOn12Plus = true;

        [Header("Android 11 and below")]
        [Tooltip("Open Location settings on <12 if location is OFF after grant.")]
        public bool promptLocationToggleOnLegacy = true;

        private void Start()
        {
            if (autoRequestOnStart)
                StartCoroutine(EnsureBlePermissions());
        }

        /// <summary>
        /// Starts (or restarts) the permission request flow.
        /// </summary>
        public void EnsureBlePermissionsNow()
        {
            StopAllCoroutines();
            StartCoroutine(EnsureBlePermissions());
        }

        private IEnumerator EnsureBlePermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            while (!Application.isFocused) yield return null;
            yield return new WaitForSeconds(0.25f);

            int sdkInt;
            using (var v = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkInt = v.GetStatic<int>("SDK_INT");

            var desired = new List<string>();

            if (sdkInt >= 31)
            {
                desired.Add("android.permission.BLUETOOTH_SCAN");
                desired.Add("android.permission.BLUETOOTH_CONNECT");
                if (requestAdvertise) desired.Add("android.permission.BLUETOOTH_ADVERTISE");
                if (requestLocationOn12Plus) desired.Add("android.permission.ACCESS_FINE_LOCATION");
            }
            else
            {
                desired.Add("android.permission.ACCESS_FINE_LOCATION");
            }

            var toRequest = new List<string>();
            foreach (var p in desired)
            {
                if (!Permission.HasUserAuthorizedPermission(p))
                    toRequest.Add(p);
            }

            if (toRequest.Count > 0)
            {
                Permission.RequestUserPermissions(toRequest.ToArray());
                // Unity doesn't expose a completion callback. Yield a few frames before re-check.
                yield return null;
                yield return null;
            }

            // Post-grant sanity checks
            if (sdkInt < 31 && promptLocationToggleOnLegacy)
            {
                if (Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION") && !IsLocationEnabled())
                {
                    Debug.LogWarning("[BLE] Location permission granted but Location is OFF. Opening settings.");
                    OpenLocationSettings();
                }
            }

            // Detect permanently denied and offer to open app settings.
            bool anyBlocked = false;
            foreach (var p in desired)
            {
                bool granted = Permission.HasUserAuthorizedPermission(p);
                bool rationale = Permission.ShouldShowRequestPermissionRationale(p);
                if (!granted && !rationale) anyBlocked = true;
            }
            if (anyBlocked)
            {
                Debug.LogWarning("[BLE] One or more permissions permanently denied. Opening app settings.");
                OpenAppSettings();
            }
#else
            yield break;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool IsLocationEnabled()
        {
            using var ctxCls = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = ctxCls.GetStatic<AndroidJavaObject>("currentActivity");
            using var locMgr = activity.Call<AndroidJavaObject>("getSystemService", "location");
            return locMgr != null && locMgr.Call<bool>("isLocationEnabled");
        }

        private static void OpenLocationSettings()
        {
            using var intent = new AndroidJavaObject("android.content.Intent", "android.settings.LOCATION_SOURCE_SETTINGS");
            intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
            new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity")
                .Call("startActivity", intent);
        }

        private static void OpenAppSettings()
        {
            using var uriCls = new AndroidJavaClass("android.net.Uri");
            using var intent = new AndroidJavaObject(
                "android.content.Intent",
                "android.settings.APPLICATION_DETAILS_SETTINGS",
                uriCls.CallStatic<AndroidJavaObject>("fromParts",
                    "package",
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                        .GetStatic<AndroidJavaObject>("currentActivity")
                        .Call<string>("getPackageName"),
                    null)
            );
            intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
            new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity")
                .Call("startActivity", intent);
        }
#endif
    }
}

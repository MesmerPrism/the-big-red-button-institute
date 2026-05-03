using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

namespace TheBigRedButtonInstitute.Biofeedback.Transport.Bluetooth
{
    /// <summary>
    /// Unified Bluetooth permission bootstrap for BLE and Classic.
    /// Intended as a single entry point so modules don't each own permission UI.
    ///
    /// Notes:
    /// - Android 12+ requires BLUETOOTH_SCAN/CONNECT (and ADVERTISE if used).
    /// - Android 11 and below requires ACCESS_FINE_LOCATION and Location toggle for BLE scans.
    /// </summary>
    public class BluetoothPermissionsBootstrap : MonoBehaviour
    {
        public enum BluetoothPermissionProfile
        {
            BleOnly,
            ClassicOnly,
            BleAndClassic
        }

        [Header("Startup")]
        [SerializeField] private bool autoRequestOnStart = false;
        [Tooltip("Which stack to request permissions for on Start.")]
        [SerializeField] private BluetoothPermissionProfile autoProfile = BluetoothPermissionProfile.BleAndClassic;

        [Header("BLE (Android 12+)")]
        [SerializeField] private bool requestBleScan = true;
        [SerializeField] private bool requestBleConnect = true;
        [SerializeField] private bool requestBleAdvertise = false;

        [Header("Classic (Android 12+)")]
        [SerializeField] private bool requestClassicConnect = true;
        [SerializeField] private bool requestClassicScan = false;

        [Header("Location / Legacy")]
        [Tooltip("On Android 12+, request location to cope with OEM stacks that still gate scans.")]
        [SerializeField] private bool requestLocationOn12Plus = true;
        [Tooltip("On Android 11 and below, BLE scans require ACCESS_FINE_LOCATION.")]
        [SerializeField] private bool requestLocationOnLegacy = true;
        [Tooltip("Open Location settings on <12 if location is OFF after grant.")]
        [SerializeField] private bool promptLocationToggleOnLegacy = true;

        private void Start()
        {
            if (autoRequestOnStart)
                StartCoroutine(EnsureBluetoothPermissions(autoProfile));
        }

        /// <summary>
        /// Starts (or restarts) the permission request flow.
        /// </summary>
        public void EnsureBluetoothPermissionsNow()
        {
            EnsureBluetoothPermissionsNow(autoProfile);
        }

        /// <summary>
        /// Starts (or restarts) the permission request flow for a specific profile.
        /// </summary>
        public void EnsureBluetoothPermissionsNow(BluetoothPermissionProfile profile)
        {
            StopAllCoroutines();
            StartCoroutine(EnsureBluetoothPermissions(profile));
        }

        private IEnumerator EnsureBluetoothPermissions(BluetoothPermissionProfile profile)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            while (!Application.isFocused) yield return null;
            yield return new WaitForSeconds(0.25f);

            int sdkInt;
            using (var v = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkInt = v.GetStatic<int>("SDK_INT");

            bool includeBle = profile != BluetoothPermissionProfile.ClassicOnly;
            bool includeClassic = profile != BluetoothPermissionProfile.BleOnly;

            var desired = new List<string>();

            if (sdkInt >= 31)
            {
                bool needsScan = (includeBle && requestBleScan) || (includeClassic && requestClassicScan);
                bool needsConnect = (includeBle && requestBleConnect) || (includeClassic && requestClassicConnect);

                if (needsScan)
                    desired.Add("android.permission.BLUETOOTH_SCAN");
                if (needsConnect)
                    desired.Add("android.permission.BLUETOOTH_CONNECT");
                if (includeBle && requestBleAdvertise)
                    desired.Add("android.permission.BLUETOOTH_ADVERTISE");

                if (includeBle && requestLocationOn12Plus && requestBleScan)
                    desired.Add("android.permission.ACCESS_FINE_LOCATION");
            }
            else
            {
                if (includeBle && requestLocationOnLegacy)
                    desired.Add("android.permission.ACCESS_FINE_LOCATION");
            }

            if (desired.Count == 0)
                yield break;

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
            if (sdkInt < 31 && includeBle && requestLocationOnLegacy && promptLocationToggleOnLegacy)
            {
                if (Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION") && !IsLocationEnabled())
                {
                    Debug.LogWarning("[Bluetooth] Location permission granted but Location is OFF. Opening settings.");
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
                Debug.LogWarning("[Bluetooth] One or more permissions permanently denied. Opening app settings.");
                OpenAppSettings();
            }
#else
            yield break;
#endif
        }

#if !UNITY_ANDROID || UNITY_EDITOR
        // Android-specific request toggles stay serialized in Editor so build configs can be authored there.
        private void SuppressPlatformFieldWarnings()
        {
            _ = requestBleScan;
            _ = requestBleConnect;
            _ = requestBleAdvertise;
            _ = requestClassicConnect;
            _ = requestClassicScan;
            _ = requestLocationOn12Plus;
            _ = requestLocationOnLegacy;
            _ = promptLocationToggleOnLegacy;
        }
#endif

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

using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar
{
    /// <summary>
    /// Simple persistence for last paired Polar device (PlayerPrefs).
    /// Used by both HR and PMD modules to attempt a fast reconnect.
    /// </summary>
    public static class PolarDeviceStore
    {
        private const string DeviceNameKey = "polar_device_name";
        private const string DeviceAddressKey = "polar_device_address";

        public static void Save(string deviceName, string deviceAddress)
        {
            if (string.IsNullOrEmpty(deviceAddress)) return;
            PlayerPrefs.SetString(DeviceNameKey, deviceName ?? string.Empty);
            PlayerPrefs.SetString(DeviceAddressKey, deviceAddress);
            PlayerPrefs.Save();
        }

        public static bool TryLoad(out string deviceName, out string deviceAddress)
        {
            deviceName = string.Empty;
            deviceAddress = string.Empty;

            if (PlayerPrefs.HasKey(DeviceAddressKey))
                deviceAddress = PlayerPrefs.GetString(DeviceAddressKey);
            if (PlayerPrefs.HasKey(DeviceNameKey))
                deviceName = PlayerPrefs.GetString(DeviceNameKey);

            return !string.IsNullOrEmpty(deviceAddress);
        }

        public static void Clear()
        {
            if (PlayerPrefs.HasKey(DeviceNameKey)) PlayerPrefs.DeleteKey(DeviceNameKey);
            if (PlayerPrefs.HasKey(DeviceAddressKey)) PlayerPrefs.DeleteKey(DeviceAddressKey);
        }
    }
}


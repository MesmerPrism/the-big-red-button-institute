using System;

namespace AstralKarateDojo.Biofeedback.Transport.BLE.Commands
{
    /// <summary>
    /// Command to scan for nearby BLE devices.
    /// Emits DiscoveredDevice events while scanning and completes on FinishedDiscovering.
    /// </summary>
    public sealed class DiscoverDevices : BleCommand
    {
        private const int DefaultDiscoverTimeMs = 10000;
        private readonly int _discoverTimeMs;
        private DeviceDiscovered _onDeviceDiscovered;
        private readonly DeviceDiscovered _onDeviceDiscoveredHandler;

        public DiscoverDevices(int discoverTimeMs = DefaultDiscoverTimeMs) : base(true, false)
        {
            _discoverTimeMs = Math.Max(1000, discoverTimeMs);
        }

        public DiscoverDevices(Action<string, string> onDeviceDiscovered, int discoverTimeMs = DefaultDiscoverTimeMs) : base(true, false)
        {
            _discoverTimeMs = Math.Max(1000, discoverTimeMs);
            _onDeviceDiscoveredHandler = new DeviceDiscovered(onDeviceDiscovered);
            _onDeviceDiscovered += _onDeviceDiscoveredHandler;
        }

        public override void Start() => BleCentral.SendCommand("scanBleDevices", _discoverTimeMs);

        public override void End()
        {
            BleCentral.SendCommand("stopScanBleDevices");
            _onDeviceDiscovered -= _onDeviceDiscoveredHandler;
        }

        public override bool CommandReceived(BleObject obj)
        {
            if (string.Equals(obj.Command, "DiscoveredDevice", StringComparison.OrdinalIgnoreCase))
                _onDeviceDiscovered?.Invoke(obj.Device, obj.Name);

            return string.Equals(obj.Command, "FinishedDiscovering", StringComparison.OrdinalIgnoreCase);
        }

        public delegate void DeviceDiscovered(string deviceAddress, string deviceName);
    }
}

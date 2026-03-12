using System;

namespace AstralKarateDojo.Biofeedback.Transport.Bluetooth
{
    /// <summary>
    /// Minimal interface for connection-aware device modules.
    /// Keeps BLE and Classic modules consistent without merging their stacks.
    /// Optional helper for UI or status panels.
    /// </summary>
    public interface IBluetoothDeviceModule
    {
        bool IsConnected { get; }
        string DeviceName { get; }
        string DeviceAddress { get; }
        event Action<bool> ConnectionChanged;
    }
}

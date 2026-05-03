using System;
using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar
{
    /// <summary>
    /// Unity adapter for the PolarPmdBridge native Android plugin.
    /// Provides reliable PMD notifications (ECG/ACC) via proper CCCD writes.
    /// This is the integration point with the native AAR.
    /// </summary>
    public class PolarPmdAdapter : MonoBehaviour
    {
        public static PolarPmdAdapter Instance { get; private set; }

        public event Action<bool> OnInitialized;
        public event Action<string> OnConnected;
        public event Action<string> OnDisconnected;
        public event Action OnHrServiceFound;
        public event Action OnHrEnabled;
        public event Action<byte[]> OnHrData;
        public event Action OnPmdServiceFound;
        public event Action OnPmdCtrlEnabled;
        public event Action OnPmdDataEnabled;
        public event Action OnPmdReady;
        public event Action<byte[]> OnPmdCtrlData;
        public event Action<byte[]> OnPmdData;
        public event Action<string> OnError;

        public bool IsInitialized { get; private set; }
        public bool IsHrEnabled { get; private set; }
        public bool IsPmdReady { get; private set; }
        public bool IsPmdCtrlEnabled { get; private set; }
        public bool IsPmdDataEnabled { get; private set; }
        public bool IsPmdServiceFound { get; private set; }

        public int HrNotificationCount { get; private set; }
        public int PmdCtrlNotificationCount { get; private set; }
        public int PmdDataNotificationCount { get; private set; }
        public int CommandsWritten { get; private set; }
        public int WriteSuccessCount { get; private set; }
        public int TotalNativeMessages { get; private set; }
        public string LastError { get; private set; }
        public string LastNativeState { get; private set; } = "NotStarted";
        public int LastRequestedMtu { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass _bridgeClass;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PolarPmdAdapter] Duplicate instance destroyed");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Required name for UnitySendMessage from native plugin.
            if (gameObject.name != "PolarPmdAdapter")
            {
                Debug.Log($"[PolarPmdAdapter] Renaming GameObject from '{gameObject.name}' to 'PolarPmdAdapter' for Android callbacks");
                gameObject.name = "PolarPmdAdapter";
            }

            // DontDestroyOnLoad requires a root object; detach if this component is nested in scene hierarchy.
            if (transform.parent != null)
                transform.SetParent(null, worldPositionStays: true);

            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Initialize the native PMD bridge. Call once after BLE permissions are granted.
        /// </summary>
        public void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridgeClass = new AndroidJavaClass("com.viscereality.polarpmd.PolarPmdBridge");
                _bridgeClass.CallStatic("staticInitialize");
                Debug.Log("[PolarPmdAdapter] Native bridge initialization requested");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PolarPmdAdapter] Failed to initialize native bridge: {ex.Message}");
                LastError = ex.Message;
                OnError?.Invoke(ex.Message);
            }
#else
            Debug.Log("[PolarPmdAdapter] Initialize called (editor/non-Android - no-op)");
            IsInitialized = true;
            OnInitialized?.Invoke(true);
#endif
        }

        /// <summary>
        /// Connect to a Polar device and subscribe to PMD notifications.
        /// </summary>
        public void ConnectAndSubscribePmd(string deviceAddress)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_bridgeClass == null)
            {
                Debug.LogError("[PolarPmdAdapter] Bridge not initialized");
                return;
            }
            _bridgeClass.CallStatic("staticConnectAndSubscribePmd", deviceAddress);
#else
            Debug.Log($"[PolarPmdAdapter] ConnectAndSubscribePmd({deviceAddress}) - no-op in editor");
#endif
        }

        /// <summary>
        /// Attempts to request a larger MTU on the active PMD GATT connection via reflection.
        /// Returns true if the request was issued to Android.
        /// Note: MTU request is best-effort and depends on device/OS support.
        /// </summary>
        public bool RequestMtu(int mtu)
        {
            LastRequestedMtu = mtu;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridgeClass = new AndroidJavaClass("com.viscereality.polarpmd.PolarPmdBridge");
                using var bridgeInstance = bridgeClass.CallStatic<AndroidJavaObject>("getInstance");
                if (bridgeInstance == null)
                {
                    Debug.LogWarning("[PolarPmdAdapter] RequestMtu failed: bridge instance is null");
                    return false;
                }

                using var clazz = bridgeInstance.Call<AndroidJavaObject>("getClass");
                using var field = clazz.Call<AndroidJavaObject>("getDeclaredField", "bluetoothGatt");
                field.Call("setAccessible", true);
                using var gatt = field.Call<AndroidJavaObject>("get", bridgeInstance);
                if (gatt == null)
                {
                    Debug.LogWarning("[PolarPmdAdapter] RequestMtu failed: bluetoothGatt is null");
                    return false;
                }

                bool issued = gatt.Call<bool>("requestMtu", mtu);
                Debug.Log($"[PolarPmdAdapter] requestMtu({mtu}) => {issued}");
                return issued;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PolarPmdAdapter] RequestMtu failed: {ex.Message}");
                LastError = ex.Message;
                return false;
            }
#else
            Debug.Log($"[PolarPmdAdapter] RequestMtu({mtu}) - no-op in editor");
            return false;
#endif
        }

        /// <summary>
        /// Write a command to the PMD Control Point characteristic.
        /// </summary>
        public void WritePmdCommand(byte[] data)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_bridgeClass == null)
            {
                Debug.LogError("[PolarPmdAdapter] Bridge not initialized");
                return;
            }
            string base64 = Convert.ToBase64String(data);
            _bridgeClass.CallStatic("staticWritePmdCommand", base64);
#else
            Debug.Log($"[PolarPmdAdapter] WritePmdCommand({data.Length} bytes) - no-op in editor");
#endif
        }

        public void Disconnect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_bridgeClass == null) return;
            _bridgeClass.CallStatic("staticDisconnect");
#else
            Debug.Log("[PolarPmdAdapter] Disconnect - no-op in editor");
#endif
            IsPmdReady = false;
        }

        /// <summary>
        /// Called from the native Android plugin via UnitySendMessage.
        /// </summary>
        public void OnPmdMessage(string jsonMessage)
        {
            try
            {
                var msg = JsonUtility.FromJson<PmdMessage>(jsonMessage);
                TotalNativeMessages++;
                LastNativeState = msg.command;
                Debug.Log($"[PolarPmdAdapter] OnPmdMessage #{TotalNativeMessages}: {msg.command}");

                // Map native bridge commands to Unity events/state.
                switch (msg.command)
                {
                    case "Initialized":
                        IsInitialized = msg.data == "true";
                        OnInitialized?.Invoke(IsInitialized);
                        break;

                    case "Connecting":
                    case "PmdConnecting":
                        break;

                    case "Connected":
                        OnConnected?.Invoke(msg.data);
                        break;

                    case "Disconnected":
                        IsPmdReady = false;
                        IsHrEnabled = false;
                        IsPmdCtrlEnabled = false;
                        IsPmdDataEnabled = false;
                        IsPmdServiceFound = false;
                        OnDisconnected?.Invoke(msg.data);
                        break;

                    case "HrServiceFound":
                        OnHrServiceFound?.Invoke();
                        break;

                    case "HrEnabled":
                        IsHrEnabled = true;
                        OnHrEnabled?.Invoke();
                        break;

                    case "HrData":
                        HrNotificationCount++;
                        byte[] hrData = Convert.FromBase64String(msg.data);
                        OnHrData?.Invoke(hrData);
                        break;

                    case "PmdServiceFound":
                        IsPmdServiceFound = true;
                        OnPmdServiceFound?.Invoke();
                        break;

                    case "PmdCtrlEnabled":
                        IsPmdCtrlEnabled = true;
                        OnPmdCtrlEnabled?.Invoke();
                        break;

                    case "PmdDataEnabled":
                        IsPmdDataEnabled = true;
                        OnPmdDataEnabled?.Invoke();
                        break;

                    case "PmdReady":
                        IsPmdReady = true;
                        OnPmdReady?.Invoke();
                        break;

                    case "PmdCtrlData":
                        PmdCtrlNotificationCount++;
                        byte[] ctrlData = Convert.FromBase64String(msg.data);
                        OnPmdCtrlData?.Invoke(ctrlData);
                        break;

                    case "PmdDataData":
                        PmdDataNotificationCount++;
                        byte[] streamData = Convert.FromBase64String(msg.data);
                        OnPmdData?.Invoke(streamData);
                        break;

                    case "PmdCommandWritten":
                        CommandsWritten++;
                        break;

                    case "WriteSuccess":
                        WriteSuccessCount++;
                        break;

                    case "Error":
                        LastError = msg.data;
                        Debug.LogError($"[PolarPmdAdapter] Error from native: {msg.data}");
                        OnError?.Invoke(msg.data);
                        break;

                    default:
                        Debug.LogWarning($"[PolarPmdAdapter] Unknown command: {msg.command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PolarPmdAdapter] Failed to parse message '{jsonMessage}': {ex.Message}");
            }
        }

        [Serializable]
        private class PmdMessage
        {
            public string command = string.Empty;
            public string data = string.Empty;
        }
    }
}


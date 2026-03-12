using System;
using System.Collections.Generic;
using System.Reflection;
using AstralKarateDojo.Biofeedback.Transport.BLE;
using AstralKarateDojo.Biofeedback.Transport.BLE.Polar;
using AstralKarateDojo.Biofeedback.Transport.Bluetooth;
using AstralKarateDojo.IndirectParticles.Biofeedback.Breathing;
using AstralKarateDojo.IndirectParticles.Biofeedback.Coherence;
using AstralKarateDojo.IndirectParticles.Biofeedback.Heartbeat;
using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-79)]
    public sealed class PolarH10RuntimeManager : MonoBehaviour
    {
        const string RuntimeRootName = "Polar H10 Runtime";
        const string BleRuntimeName = "BLE Runtime";
        const string PermissionsName = "Bluetooth Permissions";
        const string PolarAdapterName = "Polar PMD Adapter";
        const string PolarRuntimeName = "Polar H10 Modules";

        static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        readonly List<string> _recentDiscoveredDevices = new();
        bool _eventsSubscribed;
        bool _pendingConnectWhenReady;
        bool _pendingScanWhenReady;
        string _statusMessage = "idle";

        [Header("Startup")]
        [SerializeField] bool bootstrapOnAwake = true;
        [SerializeField] bool autoConnectOnAndroidStart = true;
        [SerializeField] bool autoRequestPermissionsOnAndroidStart = true;
        [SerializeField, Min(1)] int maxRecentDiscoveredDevices = 4;

        [Header("References")]
        [SerializeField] Transform headsetForwardReference;
        [SerializeField] BluetoothPermissionsBootstrap bluetoothPermissions;
        [SerializeField] BleAdapter bleAdapter;
        [SerializeField] BleCentral bleCentral;
        [SerializeField] PolarPmdAdapter polarPmdAdapter;
        [SerializeField] PolarUnifiedModule polarUnifiedModule;
        [SerializeField] PolarHeartRateTransportRouter heartRateRouter;
        [SerializeField] PolarAccTransportRouter accTransportRouter;
        [SerializeField] PolarAccBreathingTracker accBreathingTracker;
        [SerializeField] PEPolarH10BreathingModule breathingModule;
        [SerializeField] PEPolarHeartbeatModule heartbeatModule;
        [SerializeField] PEHeartbeatCoherenceModule coherenceModule;

        public static PolarH10RuntimeManager Instance { get; private set; }

        public bool IsBleInitialized => bleCentral != null && bleCentral.IsInitialized;
        public bool IsBleReady => bleCentral != null && bleCentral.IsReady;
        public bool IsPolarConnected => polarUnifiedModule != null && polarUnifiedModule.IsConnected;
        public string ConnectedDeviceName => polarUnifiedModule != null && !string.IsNullOrWhiteSpace(polarUnifiedModule.ConnectedName)
            ? polarUnifiedModule.ConnectedName
            : "n/a";
        public string ConnectedDeviceAddress => polarUnifiedModule != null && !string.IsNullOrWhiteSpace(polarUnifiedModule.ConnectedAddress)
            ? polarUnifiedModule.ConnectedAddress
            : "n/a";
        public string StatusMessage => string.IsNullOrWhiteSpace(_statusMessage) ? "idle" : _statusMessage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureRuntimeAfterSceneLoad()
        {
            EnsureRuntimeExists();
        }

        public static PolarH10RuntimeManager EnsureRuntimeExists()
        {
            if (!Application.isPlaying)
            {
                return null;
            }

            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindAnyObjectByType<PolarH10RuntimeManager>();
            if (existing != null)
            {
                return existing;
            }

            var runtimeObject = new GameObject(RuntimeRootName);
            return runtimeObject.AddComponent<PolarH10RuntimeManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (bootstrapOnAwake)
            {
                EnsureRuntimeGraph();
            }
        }

        void Start()
        {
            if (!Application.isPlaying || Application.isEditor)
            {
                return;
            }

            if (Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            if (autoConnectOnAndroidStart)
            {
                BeginConnectFlow(autoRequestPermissionsOnAndroidStart);
            }
        }

        void OnEnable()
        {
            if (bootstrapOnAwake && (bleCentral == null || polarUnifiedModule == null))
            {
                EnsureRuntimeGraph();
            }

            SubscribeToRuntimeEvents();
        }

        void Update()
        {
            if (headsetForwardReference == null)
            {
                ResolveHeadsetReference();
            }
        }

        void OnDisable()
        {
            UnsubscribeFromRuntimeEvents();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void BeginConnectFlow(bool requestPermissions)
        {
            EnsureRuntimeGraph();

            if (requestPermissions && bluetoothPermissions != null)
            {
                bluetoothPermissions.EnsureBluetoothPermissionsNow(
                    BluetoothPermissionsBootstrap.BluetoothPermissionProfile.BleOnly);
                _statusMessage = "requesting BLE permissions";
            }
            else
            {
                _statusMessage = "preparing BLE runtime";
            }

            if (!PrepareBleRuntime())
            {
                return;
            }

            _pendingConnectWhenReady = true;
            _pendingScanWhenReady = false;

            if (bleCentral.IsReady)
            {
                TriggerPendingConnect();
            }
        }

        public void BeginScanFlow(bool requestPermissions)
        {
            EnsureRuntimeGraph();

            if (requestPermissions && bluetoothPermissions != null)
            {
                bluetoothPermissions.EnsureBluetoothPermissionsNow(
                    BluetoothPermissionsBootstrap.BluetoothPermissionProfile.BleOnly);
                _statusMessage = "requesting BLE permissions";
            }
            else
            {
                _statusMessage = "preparing BLE scan";
            }

            if (!PrepareBleRuntime())
            {
                return;
            }

            _pendingConnectWhenReady = false;
            _pendingScanWhenReady = true;
            _recentDiscoveredDevices.Clear();

            if (bleCentral.IsReady)
            {
                TriggerPendingScan();
            }
        }

        public void ClearSavedDevice()
        {
            PolarDeviceStore.Clear();
            _statusMessage = "cleared saved Polar device";
        }

        public string GetBleStateLabel()
        {
            if (bleCentral == null)
            {
                return "missing";
            }

            if (bleCentral.IsReady)
            {
                return "ready";
            }

            if (bleCentral.IsInitialized)
            {
                return "initializing";
            }

            return "idle";
        }

        public string GetConnectionStateLabel()
        {
            if (polarUnifiedModule == null)
            {
                return "missing";
            }

            if (polarUnifiedModule.IsConnected)
            {
                return "connected";
            }

            if (_pendingScanWhenReady)
            {
                return "scanning";
            }

            if (_pendingConnectWhenReady)
            {
                return "connecting";
            }

            return "idle";
        }

        public string GetHeartbeatLabel()
        {
            if (heartbeatModule == null)
            {
                return "n/a";
            }

            if (!heartbeatModule.IsConnected && heartbeatModule.CurrentBpm <= 0f)
            {
                return "waiting";
            }

            if (heartbeatModule.CurrentBpm <= 0f)
            {
                return "connected";
            }

            return $"{heartbeatModule.CurrentBpm:0} bpm / {heartbeatModule.CurrentIbiMs:0} ms";
        }

        public string GetBreathingLabel()
        {
            if (breathingModule == null)
            {
                return "n/a";
            }

            string tracking = breathingModule.IsCalibrated ? "tracking" : "calibrating";
            return $"{FormatBreathingState(breathingModule.CurrentState)} / {breathingModule.CurrentVolume01:0.00} / {tracking}";
        }

        public string GetCoherenceLabel()
        {
            if (coherenceModule == null)
            {
                return "n/a";
            }

            return $"{coherenceModule.CurrentCoherence01:0.00} / conf {coherenceModule.Confidence01:0.00}";
        }

        public string GetRecentDevicesLabel()
        {
            if (_recentDiscoveredDevices.Count == 0)
            {
                return "none";
            }

            return string.Join(", ", _recentDiscoveredDevices);
        }

        public string BuildPlainStatusSummary()
        {
            return
                $"BLE {GetBleStateLabel()} | Polar {GetConnectionStateLabel()} | Device {ConnectedDeviceName} | " +
                $"Heart {GetHeartbeatLabel()} | Breath {GetBreathingLabel()} | Coherence {GetCoherenceLabel()}";
        }

        void EnsureRuntimeGraph()
        {
            ResolveHeadsetReference();

            bluetoothPermissions = EnsureComponentOnChild<BluetoothPermissionsBootstrap>(PermissionsName);

            var bleRuntime = EnsureChild(BleRuntimeName);
            bool bleRuntimeWasActive = bleRuntime.activeSelf;
            if (bleRuntimeWasActive)
            {
                bleRuntime.SetActive(false);
            }

            bleAdapter = GetOrAddComponent<BleAdapter>(bleRuntime);
            bleCentral = GetOrAddComponent<BleCentral>(bleRuntime);
            SetField(bleCentral, "adapter", bleAdapter);
            SetField(bleCentral, "initializeOnAwake", false);
            SetField(bleCentral, "beginReadyCheckOnStart", false);
            SetField(bleCentral, "persistAcrossScenes", true);
            bleRuntime.SetActive(true);

            var polarAdapterObject = EnsureChild(PolarAdapterName);
            bool polarAdapterWasActive = polarAdapterObject.activeSelf;
            if (polarAdapterWasActive)
            {
                polarAdapterObject.SetActive(false);
            }

            polarPmdAdapter = GetOrAddComponent<PolarPmdAdapter>(polarAdapterObject);
            polarAdapterObject.SetActive(true);

            var polarRuntime = EnsureChild(PolarRuntimeName);
            bool polarRuntimeWasActive = polarRuntime.activeSelf;
            if (polarRuntimeWasActive)
            {
                polarRuntime.SetActive(false);
            }

            polarUnifiedModule = GetOrAddComponent<PolarUnifiedModule>(polarRuntime);
            heartRateRouter = GetOrAddComponent<PolarHeartRateTransportRouter>(polarRuntime);
            accBreathingTracker = GetOrAddComponent<PolarAccBreathingTracker>(polarRuntime);
            accTransportRouter = GetOrAddComponent<PolarAccTransportRouter>(polarRuntime);
            breathingModule = GetOrAddComponent<PEPolarH10BreathingModule>(polarRuntime);
            heartbeatModule = GetOrAddComponent<PEPolarHeartbeatModule>(polarRuntime);
            coherenceModule = GetOrAddComponent<PEHeartbeatCoherenceModule>(polarRuntime);

            SetField(polarUnifiedModule, "central", bleCentral);
            SetField(polarUnifiedModule, "blePermissionBootstrap", null);
            SetField(polarUnifiedModule, "bluetoothPermissions", bluetoothPermissions);
            SetField(polarUnifiedModule, "pmdAdapter", polarPmdAdapter);
            SetField(polarUnifiedModule, "autoRequestPermissions", false);
            SetField(polarUnifiedModule, "autoConnectOnReady", false);
            SetField(polarUnifiedModule, "autoScanWhenNoSavedDevice", true);
            SetField(polarUnifiedModule, "autoRescanWhenNoMatch", true);
            SetField(polarUnifiedModule, "enableEcg", false);
            SetField(polarUnifiedModule, "enableAcc", true);
            SetField(polarUnifiedModule, "logDebug", false);

            SetField(heartRateRouter, "unifiedModule", polarUnifiedModule);
            SetField(heartRateRouter, "publishToRawSignalRegistry", true);
            SetField(heartRateRouter, "rawSignalPrefix", "polar_hr");
            SetField(heartRateRouter, "publishPerSample", true);
            SetField(heartRateRouter, "logDebug", false);

            SetField(accBreathingTracker, "headsetForwardReference", headsetForwardReference);
            SetField(accBreathingTracker, "loadRuntimeConfigOnEnable", false);
            SetField(accBreathingTracker, "logDebug", false);

            SetField(accTransportRouter, "unifiedModule", polarUnifiedModule);
            SetField(accTransportRouter, "breathingTracker", accBreathingTracker);
            SetField(accTransportRouter, "publishToRawSignalRegistry", true);
            SetField(accTransportRouter, "rawSignalPrefix", "polar_acc");
            SetField(accTransportRouter, "publishPerSample", true);
            SetField(accTransportRouter, "logDebug", false);

            SetField(coherenceModule, "logDebug", false);
            polarRuntime.SetActive(true);
        }

        bool PrepareBleRuntime()
        {
            if (bleCentral == null)
            {
                _statusMessage = "BLE runtime missing";
                return false;
            }

            bleCentral.Initialize();
            if (!bleCentral.IsInitialized)
            {
                string reason = string.IsNullOrWhiteSpace(bleCentral.LastInitializationError)
                    ? "unknown error"
                    : bleCentral.LastInitializationError;
                _statusMessage = $"BLE unavailable: {reason}";
                return false;
            }

            bleCentral.BeginReadyCheck();
            return true;
        }

        void SubscribeToRuntimeEvents()
        {
            if (_eventsSubscribed)
            {
                return;
            }

            if (bleCentral == null || polarUnifiedModule == null)
            {
                EnsureRuntimeGraph();
            }

            if (bleCentral != null)
            {
                bleCentral.OnBleReady += HandleBleReady;
                bleCentral.OnBleNotReady += HandleBleNotReady;
                bleCentral.OnBleError += HandleBleError;
            }

            if (polarUnifiedModule != null)
            {
                polarUnifiedModule.ConnectionChanged += HandlePolarConnectionChanged;
                polarUnifiedModule.DeviceDiscovered += HandlePolarDeviceDiscovered;
            }

            _eventsSubscribed = true;
        }

        void UnsubscribeFromRuntimeEvents()
        {
            if (!_eventsSubscribed)
            {
                return;
            }

            if (bleCentral != null)
            {
                bleCentral.OnBleReady -= HandleBleReady;
                bleCentral.OnBleNotReady -= HandleBleNotReady;
                bleCentral.OnBleError -= HandleBleError;
            }

            if (polarUnifiedModule != null)
            {
                polarUnifiedModule.ConnectionChanged -= HandlePolarConnectionChanged;
                polarUnifiedModule.DeviceDiscovered -= HandlePolarDeviceDiscovered;
            }

            _eventsSubscribed = false;
        }

        void HandleBleReady()
        {
            _statusMessage = "BLE ready";

            if (_pendingConnectWhenReady)
            {
                TriggerPendingConnect();
                return;
            }

            if (_pendingScanWhenReady)
            {
                TriggerPendingScan();
            }
        }

        void HandleBleNotReady(string reason)
        {
            _statusMessage = string.IsNullOrWhiteSpace(reason)
                ? "BLE not ready"
                : $"BLE not ready: {reason}";
        }

        void HandleBleError(string reason)
        {
            _statusMessage = string.IsNullOrWhiteSpace(reason)
                ? "BLE error"
                : $"BLE error: {reason}";
        }

        void HandlePolarConnectionChanged(bool connected)
        {
            _statusMessage = connected
                ? $"connected to {ConnectedDeviceName}"
                : "Polar disconnected";
        }

        void HandlePolarDeviceDiscovered(string address, string name)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "Unnamed Polar" : name.Trim();
            string safeAddress = string.IsNullOrWhiteSpace(address) ? "unknown" : address.Trim();
            string label = $"{safeName} ({safeAddress})";
            _recentDiscoveredDevices.Remove(label);
            _recentDiscoveredDevices.Insert(0, label);

            if (_recentDiscoveredDevices.Count > Mathf.Max(1, maxRecentDiscoveredDevices))
            {
                _recentDiscoveredDevices.RemoveAt(_recentDiscoveredDevices.Count - 1);
            }

            _statusMessage = $"discovered {safeName}";
        }

        void TriggerPendingConnect()
        {
            _pendingConnectWhenReady = false;

            if (polarUnifiedModule == null)
            {
                _statusMessage = "Polar module missing";
                return;
            }

            polarUnifiedModule.TryConnect();
            _statusMessage = "connecting to Polar H10";
        }

        void TriggerPendingScan()
        {
            _pendingScanWhenReady = false;

            if (polarUnifiedModule == null)
            {
                _statusMessage = "Polar module missing";
                return;
            }

            polarUnifiedModule.StartScan();
            _statusMessage = "scanning for Polar H10";
        }

        void ResolveHeadsetReference()
        {
            if (headsetForwardReference == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    headsetForwardReference = rig.centerEyeAnchor;
                }
            }

            if (headsetForwardReference == null && Camera.main != null)
            {
                headsetForwardReference = Camera.main.transform;
            }

            if (headsetForwardReference != null && accBreathingTracker != null)
            {
                SetField(accBreathingTracker, "headsetForwardReference", headsetForwardReference);
            }
        }

        GameObject EnsureChild(string childName)
        {
            var existing = transform.Find(childName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            return child;
        }

        T EnsureComponentOnChild<T>(string childName) where T : Component
        {
            var child = EnsureChild(childName);
            return GetOrAddComponent<T>(child);
        }

        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component == null)
            {
                component = target.AddComponent<T>();
            }

            return component;
        }

        static void SetField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                return;
            }

            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, InstanceFieldFlags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }
        }

        static string FormatBreathingState(PEBreathingState state)
        {
            return state switch
            {
                PEBreathingState.Inhaling => "inhaling",
                PEBreathingState.Exhaling => "exhaling",
                PEBreathingState.Pausing => "pausing",
                _ => "bad tracking"
            };
        }
    }
}

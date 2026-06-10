using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar;
using TheBigRedButtonInstitute.Biofeedback.Transport.Bluetooth;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Breathing;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Coherence;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Biofeedback
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-79)]
    public sealed class PolarH10RuntimeManager : MonoBehaviour
    {
        const string RuntimeRootName = "Polar H10 Runtime";
        const string ConnectionHubName = "Biofeedback Connection Hub";
        const string PolarRuntimeName = "Polar H10 Breathing Source";
        const float PermissionPollIntervalSeconds = 0.25f;
        const float PermissionRequestTimeoutSeconds = 30f;

        static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        enum BleStartupIntent
        {
            None = 0,
            Connect = 1,
            Scan = 2
        }

        readonly List<string> _recentDiscoveredDevices = new();
        bool _eventsSubscribed;
        bool _pendingConnectWhenReady;
        bool _pendingScanWhenReady;
        bool _usePolarAutoConnectOnReady;
        Coroutine _bleStartupRoutine;
        bool _hasRequestedBlePermissionsThisSession;
        string _statusMessage = "idle";
        PEHeartbeatSample _lastHeartbeatSample;
        bool _hasHeartbeatSample;

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
        public event Action<PEHeartbeatSample> HeartbeatSampleUpdated;

        public bool IsBleInitialized => bleCentral != null && bleCentral.IsInitialized;
        public bool IsBleReady => bleCentral != null && bleCentral.IsReady;
        public bool IsPolarConnected => polarUnifiedModule != null && polarUnifiedModule.IsConnected;
        public bool HasHeartbeatSample => _hasHeartbeatSample;
        public PEPolarHeartbeatModule HeartbeatModule => heartbeatModule;
        public PEHeartbeatSample LastHeartbeatSample => _lastHeartbeatSample;
        public string ConnectedDeviceName => polarUnifiedModule != null && !string.IsNullOrWhiteSpace(polarUnifiedModule.ConnectedName)
            ? polarUnifiedModule.ConnectedName
            : "n/a";
        public string ConnectedDeviceAddress => polarUnifiedModule != null && !string.IsNullOrWhiteSpace(polarUnifiedModule.ConnectedAddress)
            ? polarUnifiedModule.ConnectedAddress
            : "n/a";
        public string StatusMessage => string.IsNullOrWhiteSpace(_statusMessage) ? "idle" : _statusMessage;
        public PolarUnifiedModule PolarUnifiedModule => polarUnifiedModule;
        public PolarPmdAdapter PolarPmdAdapter => polarPmdAdapter;

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

        public void ConfigureRuntimeGraphReferences(
            Transform headReference,
            BluetoothPermissionsBootstrap permissionsBootstrap,
            BleAdapter adapter,
            BleCentral central,
            PolarPmdAdapter pmdAdapter,
            PolarUnifiedModule unifiedModule,
            PolarHeartRateTransportRouter heartRateTransportRouter,
            PolarAccTransportRouter accRouter,
            PolarAccBreathingTracker breathingTracker,
            PEPolarH10BreathingModule polarBreathingModule,
            PEPolarHeartbeatModule polarHeartbeatModule,
            PEHeartbeatCoherenceModule heartbeatCoherenceModule)
        {
            headsetForwardReference = headReference;
            bluetoothPermissions = permissionsBootstrap;
            bleAdapter = adapter;
            bleCentral = central;
            polarPmdAdapter = pmdAdapter;
            polarUnifiedModule = unifiedModule;
            heartRateRouter = heartRateTransportRouter;
            accTransportRouter = accRouter;
            accBreathingTracker = breathingTracker;
            breathingModule = polarBreathingModule;
            heartbeatModule = polarHeartbeatModule;
            coherenceModule = heartbeatCoherenceModule;
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
                return;
            }

            if (autoRequestPermissionsOnAndroidStart)
            {
                RequestBlePermissionsOnly();
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
            StopBleStartupRoutine();
            UnsubscribeFromRuntimeEvents();
        }

        void OnDestroy()
        {
            StopBleStartupRoutine();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void BeginConnectFlow(bool requestPermissions)
        {
            EnsureRuntimeGraph();
            StartBleStartup(BleStartupIntent.Connect, requestPermissions);
        }

        public void BeginScanFlow(bool requestPermissions)
        {
            EnsureRuntimeGraph();
            StartBleStartup(BleStartupIntent.Scan, requestPermissions);
        }

        public void ClearSavedDevice()
        {
            PolarDeviceStore.Clear();
            _statusMessage = "cleared saved Polar device";
        }

        public void RequestBlePermissionsOnly()
        {
            EnsureRuntimeGraph();
            StartBleStartup(BleStartupIntent.None, requestPermissions: true);
        }

        public string GetBleStateLabel()
        {
            if (bleCentral == null)
            {
                return "missing";
            }

            if (_bleStartupRoutine != null &&
                !bleCentral.IsInitialized &&
                !string.IsNullOrWhiteSpace(_statusMessage) &&
                _statusMessage.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "permissions";
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

        public string GetBlePermissionStatusLabel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int sdkInt = GetAndroidSdkInt();
            bool scanGranted = sdkInt < 31 || Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN");
            bool connectGranted = sdkInt < 31 || Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT");
            bool locationGranted = Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION");

            if (sdkInt >= 31)
            {
                return $"scan={OnOff(scanGranted)} connect={OnOff(connectGranted)} location={OnOff(locationGranted)} sdk={sdkInt}";
            }

            bool locationEnabled = IsLegacyLocationEnabled();
            return $"location={OnOff(locationGranted)} location_services={OnOff(locationEnabled)} sdk={sdkInt}";
#else
            return Application.isEditor ? "editor runtime" : "non-android runtime";
#endif
        }

        public string GetBlePermissionGuidanceLabel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (AreBleRuntimeConditionsSatisfied(out var reason))
            {
                return _hasRequestedBlePermissionsThisSession
                    ? "ready"
                    : "already granted";
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return _hasRequestedBlePermissionsThisSession
                    ? "waiting for permission flow"
                    : "request permissions";
            }

            bool blockedBySettings = _hasRequestedBlePermissionsThisSession && !ShouldShowBlePermissionRationale();
            if (blockedBySettings)
            {
                return $"blocked in settings: {reason}";
            }

            return _hasRequestedBlePermissionsThisSession
                ? $"grant required: {reason}"
                : $"not granted: {reason}";
#else
            return Application.isEditor ? "android build only" : "n/a";
#endif
        }

        public string GetConnectionStateLabel()
        {
            if (polarUnifiedModule == null)
            {
                return "missing";
            }

            if (polarUnifiedModule.IsConnected)
            {
                if (heartbeatModule == null)
                {
                    return "connected";
                }

                return heartbeatModule.TrackingState switch
                {
                    PEHeartbeatTrackingState.Tracking => "connected / tracking",
                    PEHeartbeatTrackingState.Stale => "connected / stale",
                    _ => "connected / waiting"
                };
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
            ResolveExistingRuntimeReferences();

            var connectionHub = ResolveOrCreateRuntimeObject(
                ConnectionHubName,
                bluetoothPermissions,
                bleCentral,
                bleAdapter);
            bool connectionHubWasActive = connectionHub.activeSelf;
            if (connectionHubWasActive)
            {
                connectionHub.SetActive(false);
            }

            bluetoothPermissions = GetOrAddComponent<BluetoothPermissionsBootstrap>(connectionHub);
            bleAdapter = GetOrAddComponent<BleAdapter>(connectionHub);
            bleCentral = GetOrAddComponent<BleCentral>(connectionHub);
            SetField(bleCentral, "adapter", bleAdapter);
            SetField(bleCentral, "initializeOnAwake", false);
            SetField(bleCentral, "beginReadyCheckOnStart", false);
            SetField(bleCentral, "persistAcrossScenes", true);
            connectionHub.SetActive(true);

            ResolveExistingRuntimeReferences();

            var polarRuntime = ResolveOrCreateRuntimeObject(
                PolarRuntimeName,
                polarUnifiedModule,
                polarPmdAdapter,
                heartRateRouter,
                accTransportRouter,
                accBreathingTracker,
                breathingModule,
                heartbeatModule,
                coherenceModule);
            bool polarRuntimeWasActive = polarRuntime.activeSelf;
            if (polarRuntimeWasActive)
            {
                polarRuntime.SetActive(false);
            }

            polarUnifiedModule = GetOrAddComponent<PolarUnifiedModule>(polarRuntime);
            polarPmdAdapter = GetOrAddComponent<PolarPmdAdapter>(polarRuntime);
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
            SetField(polarUnifiedModule, "logDebug", true);

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
            ResolveExistingRuntimeReferences();
        }

        void StartBleStartup(BleStartupIntent intent, bool requestPermissions)
        {
            StopBleStartupRoutine();
            _bleStartupRoutine = StartCoroutine(RunBleStartup(intent, requestPermissions));
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

        IEnumerator RunBleStartup(BleStartupIntent intent, bool requestPermissions)
        {
            _pendingConnectWhenReady = false;
            _pendingScanWhenReady = false;
            _usePolarAutoConnectOnReady = intent == BleStartupIntent.Connect;

            if (intent == BleStartupIntent.Scan)
            {
                _recentDiscoveredDevices.Clear();
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (requestPermissions)
            {
                bool permissionsGranted = false;
                if (!TryRequestBlePermissions())
                {
                    _bleStartupRoutine = null;
                    yield break;
                }

                yield return WaitForBlePermissions(result => permissionsGranted = result);
                if (!permissionsGranted)
                {
                    _bleStartupRoutine = null;
                    yield break;
                }
            }
            else if (!AreBleRuntimeConditionsSatisfied(out var reasonWithoutRequest))
            {
                _statusMessage = $"BLE permissions missing: {reasonWithoutRequest}";
                _bleStartupRoutine = null;
                yield break;
            }
#endif

            if (intent == BleStartupIntent.None)
            {
                _statusMessage = AreBleRuntimeConditionsSatisfied(out _)
                    ? "BLE permissions ready"
                    : _statusMessage;
                _bleStartupRoutine = null;
                yield break;
            }

            _statusMessage = intent == BleStartupIntent.Scan
                ? "preparing BLE scan"
                : "preparing BLE runtime";

            if (polarUnifiedModule != null)
            {
                SetField(polarUnifiedModule, "autoConnectOnReady", _usePolarAutoConnectOnReady);
            }

            if (!PrepareBleRuntime())
            {
                _bleStartupRoutine = null;
                yield break;
            }

            _pendingConnectWhenReady = intent == BleStartupIntent.Connect;
            _pendingScanWhenReady = intent == BleStartupIntent.Scan;

            if (bleCentral.IsReady)
            {
                if (_pendingConnectWhenReady)
                {
                    _pendingConnectWhenReady = false;
                    TriggerImmediateConnect();
                }
                else if (_pendingScanWhenReady)
                {
                    TriggerPendingScan();
                }
            }

            _bleStartupRoutine = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        IEnumerator WaitForBlePermissions(Action<bool> onComplete)
        {
            if (AreBleRuntimeConditionsSatisfied(out _))
            {
                onComplete?.Invoke(true);
                yield break;
            }

            float elapsed = 0f;
            string lastReason = null;

            while (elapsed < PermissionRequestTimeoutSeconds)
            {
                if (AreBleRuntimeConditionsSatisfied(out _))
                {
                    onComplete?.Invoke(true);
                    yield break;
                }

                AreBleRuntimeConditionsSatisfied(out var reason);
                if (!string.Equals(reason, lastReason, StringComparison.Ordinal))
                {
                    lastReason = reason;
                    _statusMessage = string.IsNullOrWhiteSpace(reason)
                        ? "waiting for BLE permissions"
                        : $"waiting for BLE permissions: {reason}";
                }

                yield return new WaitForSecondsRealtime(PermissionPollIntervalSeconds);
                elapsed += PermissionPollIntervalSeconds;
            }

            AreBleRuntimeConditionsSatisfied(out var timeoutReason);
            _statusMessage = string.IsNullOrWhiteSpace(timeoutReason)
                ? "BLE permission request timed out"
                : $"BLE permission request timed out: {timeoutReason}";
            onComplete?.Invoke(false);
        }
#endif

        bool TryRequestBlePermissions()
        {
            if (AreBleRuntimeConditionsSatisfied(out _))
            {
                _statusMessage = "BLE permissions already granted";
                return true;
            }

            if (bluetoothPermissions != null)
            {
                _hasRequestedBlePermissionsThisSession = true;
                bluetoothPermissions.EnsureBluetoothPermissionsNow(
                    BluetoothPermissionsBootstrap.BluetoothPermissionProfile.BleOnly);
                _statusMessage = "requesting BLE permissions";
                return true;
            }

            _statusMessage = "BLE permissions bootstrap missing";
            return false;
        }

        static bool AreBleRuntimeConditionsSatisfied(out string reason)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return BlePermissionHelper.AreBleRuntimeConditionsMet(out reason);
#else
            reason = null;
            return true;
#endif
        }

        void StopBleStartupRoutine()
        {
            if (_bleStartupRoutine == null)
            {
                return;
            }

            StopCoroutine(_bleStartupRoutine);
            _bleStartupRoutine = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static int GetAndroidSdkInt()
        {
            using var versionClass = new AndroidJavaClass("android.os.Build$VERSION");
            return versionClass.GetStatic<int>("SDK_INT");
        }

        static bool IsLegacyLocationEnabled()
        {
            using var ctxCls = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = ctxCls.GetStatic<AndroidJavaObject>("currentActivity");
            using var locationManager = activity.Call<AndroidJavaObject>("getSystemService", "location");
            return locationManager != null && locationManager.Call<bool>("isLocationEnabled");
        }

        bool ShouldShowBlePermissionRationale()
        {
            int sdkInt = GetAndroidSdkInt();
            if (sdkInt >= 31)
            {
                return
                    Permission.ShouldShowRequestPermissionRationale("android.permission.BLUETOOTH_SCAN") ||
                    Permission.ShouldShowRequestPermissionRationale("android.permission.BLUETOOTH_CONNECT") ||
                    Permission.ShouldShowRequestPermissionRationale("android.permission.ACCESS_FINE_LOCATION");
            }

            return Permission.ShouldShowRequestPermissionRationale(Permission.FineLocation);
        }
#endif

        static string OnOff(bool value) => value ? "on" : "off";

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

            if (heartbeatModule != null)
            {
                heartbeatModule.SampleUpdated += HandleHeartbeatSampleUpdated;
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

            if (heartbeatModule != null)
            {
                heartbeatModule.SampleUpdated -= HandleHeartbeatSampleUpdated;
            }

            _eventsSubscribed = false;
        }

        void HandleBleReady()
        {
            _statusMessage = "BLE ready";

            if (_pendingConnectWhenReady)
            {
                _pendingConnectWhenReady = false;

                if (_usePolarAutoConnectOnReady)
                {
                    _statusMessage = "BLE ready, waiting for Polar auto-connect";
                    return;
                }

                TriggerImmediateConnect();
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

        void HandleHeartbeatSampleUpdated(PEHeartbeatSample sample)
        {
            _lastHeartbeatSample = sample;
            _hasHeartbeatSample = true;
            HeartbeatSampleUpdated?.Invoke(sample);
        }

        void TriggerPendingConnect()
        {
            _pendingConnectWhenReady = false;
            TriggerImmediateConnect();
        }

        void TriggerImmediateConnect()
        {
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

        void ResolveExistingRuntimeReferences()
        {
            bluetoothPermissions ??= FindAnyObjectByType<BluetoothPermissionsBootstrap>();
            bleAdapter ??= FindAnyObjectByType<BleAdapter>();
            bleCentral ??= FindAnyObjectByType<BleCentral>();
            polarPmdAdapter ??= FindAnyObjectByType<PolarPmdAdapter>();
            polarUnifiedModule ??= FindAnyObjectByType<PolarUnifiedModule>();
            heartRateRouter ??= FindAnyObjectByType<PolarHeartRateTransportRouter>();
            accTransportRouter ??= FindAnyObjectByType<PolarAccTransportRouter>();
            accBreathingTracker ??= FindAnyObjectByType<PolarAccBreathingTracker>();
            breathingModule ??= FindAnyObjectByType<PEPolarH10BreathingModule>();
            heartbeatModule ??= FindAnyObjectByType<PEPolarHeartbeatModule>();
            coherenceModule ??= FindAnyObjectByType<PEHeartbeatCoherenceModule>();
        }

        GameObject ResolveOrCreateRuntimeObject(string fallbackName, params Component[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null)
                {
                    return candidates[i].gameObject;
                }
            }

            var existing = GameObject.Find(fallbackName);
            if (existing != null)
            {
                if (existing.scene != gameObject.scene)
                {
                    SceneManager.MoveGameObjectToScene(existing, gameObject.scene);
                }

                return existing;
            }

            var created = new GameObject(fallbackName);
            SceneManager.MoveGameObjectToScene(created, gameObject.scene);
            created.transform.SetParent(transform, false);
            return created;
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

using System;
using System.Collections;
using System.Collections.Generic;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Commands;
using TheBigRedButtonInstitute.Biofeedback.Transport.Bluetooth;
using UnityEngine;

namespace TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar
{
    /// <summary>
    /// Unified Polar H10 module that uses the native PMD bridge for both HR and PMD streams.
    /// This avoids running separate BLE stacks when HR + PMD are both enabled.
    ///
    /// Notes:
    /// - Uses BleCentral only for scanning (optional). Connection is via PolarPmdBridge.
    /// - HR notifications are delivered by the native bridge (no separate HR BLE subscription).
    /// </summary>
    [DefaultExecutionOrder(-46)]
    public class PolarUnifiedModule : MonoBehaviour, IBluetoothDeviceModule
    {
        [Header("References")]
        [SerializeField] private BleCentral central;
        [SerializeField] private BlePermissionBootstrap blePermissionBootstrap;
        [SerializeField] private BluetoothPermissionsBootstrap bluetoothPermissions;
        [SerializeField] private PolarPmdAdapter pmdAdapter;

        [Header("Auto Start")]
        [SerializeField] private bool autoRequestPermissions = false;
        [SerializeField] private bool autoConnectOnReady = false;
        [SerializeField] private bool autoScanWhenNoSavedDevice = false;
        [SerializeField] private bool autoRescanWhenNoMatch = false;
        [SerializeField] private float savedAddressFallbackDelaySeconds = 2.0f;

        [Header("Scan / Filter")]
        [SerializeField] private int scanDurationMs = 60000;
        [SerializeField] private string nameContainsA = "Polar";
        [SerializeField] private string nameContainsB = "H10";

        [Header("HR")]
        [SerializeField] private bool enableHr = true;
        [SerializeField] private bool decodeHr = true;

        [Header("PMD Streams")]
        [SerializeField] private bool enableEcg = true;
        [SerializeField] private bool enableAcc = true;
        [SerializeField] private bool decodeEcg = true;
        [SerializeField] private bool decodeAcc = true;

        [Header("Preferred Settings")]
        [SerializeField] private int preferredEcgSampleRate = 130;
        [SerializeField] private int preferredEcgResolution = 14;
        [SerializeField] private int preferredAccSampleRate = 200;
        [SerializeField] private int preferredAccResolution = 16;
        [SerializeField] private int preferredAccRangeG = 8;

        [Header("MTU")]
        [SerializeField] private bool requestMtuOnConnect = true;
        [SerializeField] private int desiredMtu = 232;
        [SerializeField] private float mtuRequestDelaySeconds = 1.0f;
        [SerializeField] private bool autoRetryOnInvalidMtu = true;
        [SerializeField] private int[] mtuRetryCandidates = new[] { 232, 247, 185, 158, 128 };
        [SerializeField] private float mtuRetryDelaySeconds = 0.5f;

        [Header("Logging")]
        [SerializeField] private bool logDebug = true;

        public bool IsConnected => _isConnected;
        public bool IsPmdReady => _pmdReady;
        public string ConnectedAddress => _connectedAddress;
        public string ConnectedName => _connectedName;
        public string DeviceName => ConnectedName;
        public string DeviceAddress => ConnectedAddress;

        public event Action<bool> ConnectionChanged;
        public event Action<ushort> HeartRateReceived;
        public event Action<float[]> RrIntervalsReceived;
        public event Action<byte[]> HrDataReceived;
        public event Action<byte[]> PmdCtrlDataReceived;
        public event Action<byte[]> PmdDataReceived;
        public event Action<PolarPmdEcgFrame> EcgFrameReceived;
        public event Action<PolarPmdAccFrame> AccFrameReceived;
        public event Action<string, string> DeviceDiscovered;

        private PolarPmdAdapter _adapter;
        private bool _adapterHooked;
        private DiscoverDevices _scanCommand;
        private bool _readyHooked;
        private bool _rawHooked;
        private bool _handledReadyTrigger;
        private bool _isConnected;
        private bool _pmdReady;
        private bool _startIssued;
        private Coroutine _startRoutine;
        private string _pendingAddress;
        private string _connectedAddress;
        private string _connectedName;
        private bool _attemptingSavedAddressConnect;
        private Coroutine _savedAddressFallbackRoutine;
        private int _mtuRetryIndex;
        private bool _mtuRetryInProgress;

        private int _pmdCtrlNotifCount;
        private byte _lastPmdCtrlCmd;
        private byte _lastPmdCtrlMeas;
        private byte _lastPmdCtrlErr;
        private bool _missingReferenceWarningLogged;
        private bool _missingAdapterWarningLogged;

        private readonly Dictionary<byte, PmdSettings> _settingsByMeas = new();

        private void OnEnable()
        {
            _handledReadyTrigger = false;
            WarnMissingReferencesIfNeeded();
            EnsureAdapter();

            if (central != null && !_readyHooked)
            {
                central.OnBleReady += HandleBleReady;
                _readyHooked = true;
            }

            if (central != null && !_rawHooked)
            {
                central.OnRawMessage += HandleRawBleMessage;
                _rawHooked = true;
            }

            if (autoConnectOnReady)
            {
                if (autoRequestPermissions)
                {
                    if (bluetoothPermissions != null)
                        bluetoothPermissions.EnsureBluetoothPermissionsNow(BluetoothPermissionsBootstrap.BluetoothPermissionProfile.BleOnly);
                    else
                        blePermissionBootstrap?.EnsureBlePermissionsNow();
                }

                if (central != null)
                {
                    if (!central.IsInitialized)
                        central.Initialize();

                    if (central.IsReady)
                        HandleBleReady();
                    else
                        central.BeginReadyCheck();
                }
                else
                {
                    Debug.LogWarning(
                        "[PolarUnified] autoConnectOnReady is enabled but BleCentral is missing. Assign explicit references in the inspector.",
                        this);
                }
            }
        }

        private void OnDisable()
        {
            if (central != null && _readyHooked)
                central.OnBleReady -= HandleBleReady;
            _readyHooked = false;

            if (central != null && _rawHooked)
                central.OnRawMessage -= HandleRawBleMessage;
            _rawHooked = false;

            StopScan();
            StopStartRoutine();
            StopSavedAddressFallbackRoutine();
            UnhookAdapterEvents();
            _startIssued = false;
            _handledReadyTrigger = false;
            _mtuRetryInProgress = false;
            _settingsByMeas.Clear();
        }

        private void HandleBleReady()
        {
            if (_handledReadyTrigger)
            {
                if (logDebug) Debug.Log("[PolarUnified] BLE ready already handled; skipping duplicate startup trigger.");
                return;
            }

            _handledReadyTrigger = true;
            if (logDebug) Debug.Log("[PolarUnified] BLE ready.");
            TryConnect();
        }

        public void TryConnect()
        {
            if (_isConnected) return;

            if (PolarDeviceStore.TryLoad(out var savedName, out var savedAddress))
            {
                if (logDebug) Debug.Log($"[PolarUnified] Using saved device {savedName} @ {savedAddress}");
                _attemptingSavedAddressConnect = true;
                ConnectNative(savedAddress, savedName);
                StartSavedAddressFallbackTimer();
                return;
            }

            if (autoScanWhenNoSavedDevice && central != null)
                StartScan();
            else if (logDebug)
                Debug.LogWarning("[PolarUnified] No saved device and BLE scan is unavailable.");
        }

        public void StartScan()
        {
            if (central == null) return;
            if (!central.IsInitialized)
                central.Initialize();
            if (!central.IsInitialized)
            {
                if (logDebug)
                {
                    string error = string.IsNullOrWhiteSpace(central.LastInitializationError)
                        ? "unknown initialization error"
                        : central.LastInitializationError;
                    Debug.LogWarning($"[PolarUnified] BLE scan unavailable: BleCentral is not initialized ({error}).");
                }
                return;
            }
            if (_scanCommand != null) return;
            StopScan();

            if (logDebug) Debug.Log("[PolarUnified] Scanning for Polar H10...");
            _scanCommand = new DiscoverDevices(OnDeviceFound, scanDurationMs);
            central.QueueCommand(_scanCommand);
        }

        public void StopScan()
        {
            _scanCommand?.End();
            _scanCommand = null;
        }

        private void OnDeviceFound(string address, string name)
        {
            DeviceDiscovered?.Invoke(address, name);

            bool savedAddressMatch = !string.IsNullOrEmpty(_connectedAddress) &&
                                     string.Equals(address, _connectedAddress, StringComparison.OrdinalIgnoreCase);
            bool polarNameMatch = IsPolarH10(name);

            if (logDebug)
            {
                string safeName = string.IsNullOrEmpty(name) ? "<unnamed>" : name;
                Debug.Log($"[PolarUnified] Scan hit: {safeName} @ {address}");
            }

            if (!savedAddressMatch && !polarNameMatch) return;

            if (logDebug)
            {
                string safeName = string.IsNullOrEmpty(name) ? "<unnamed>" : name;
                string matchReason = savedAddressMatch && !polarNameMatch
                    ? " (saved-address match)"
                    : string.Empty;
                Debug.Log($"[PolarUnified] Found {safeName} @ {address}{matchReason}");
            }

            StopScan();
            ConnectNative(address, name);
        }

        private bool IsPolarH10(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool hasA = string.IsNullOrEmpty(nameContainsA) || name.IndexOf(nameContainsA, StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasB = string.IsNullOrEmpty(nameContainsB) || name.IndexOf(nameContainsB, StringComparison.OrdinalIgnoreCase) >= 0;
            return hasA && hasB;
        }

        public void ConnectNative(string deviceAddress, string deviceName = null)
        {
            if (string.IsNullOrEmpty(deviceAddress)) return;

            _connectedAddress = deviceAddress;
            _connectedName = deviceName ?? string.Empty;

            if (!EnsureAdapter())
                return;

            if (!_adapter.IsInitialized)
            {
                _pendingAddress = deviceAddress;
                _adapter.Initialize();
            }
            else
            {
                _adapter.ConnectAndSubscribePmd(deviceAddress);
            }

            PolarDeviceStore.Save(_connectedName, _connectedAddress);
        }

        private bool EnsureAdapter()
        {
            _adapter ??= pmdAdapter;
            if (_adapter == null)
            {
                WarnMissingAdapterIfNeeded();
                return false;
            }

            if (_adapterHooked)
                return true;

            _adapter.OnInitialized += OnNativeInitialized;
            _adapter.OnConnected += OnNativeConnected;
            _adapter.OnDisconnected += OnNativeDisconnected;
            _adapter.OnHrData += OnNativeHrData;
            _adapter.OnPmdReady += OnNativePmdReady;
            _adapter.OnPmdCtrlData += OnNativePmdCtrlData;
            _adapter.OnPmdData += OnNativePmdData;
            _adapter.OnError += OnNativeError;
            _adapterHooked = true;
            return true;
        }

        private void UnhookAdapterEvents()
        {
            if (!_adapterHooked || _adapter == null)
                return;

            _adapter.OnInitialized -= OnNativeInitialized;
            _adapter.OnConnected -= OnNativeConnected;
            _adapter.OnDisconnected -= OnNativeDisconnected;
            _adapter.OnHrData -= OnNativeHrData;
            _adapter.OnPmdReady -= OnNativePmdReady;
            _adapter.OnPmdCtrlData -= OnNativePmdCtrlData;
            _adapter.OnPmdData -= OnNativePmdData;
            _adapter.OnError -= OnNativeError;
            _adapterHooked = false;
        }

        private void WarnMissingReferencesIfNeeded()
        {
            if (_missingReferenceWarningLogged)
                return;

            var missing = new List<string>(4);
            if (central == null)
                missing.Add(nameof(central));
            if (pmdAdapter == null)
                missing.Add(nameof(pmdAdapter));
            if (autoRequestPermissions && blePermissionBootstrap == null && bluetoothPermissions == null)
                missing.Add($"{nameof(blePermissionBootstrap)}|{nameof(bluetoothPermissions)}");

            if (missing.Count == 0)
                return;

            _missingReferenceWarningLogged = true;
            Debug.LogWarning(
                "[PolarUnified] Missing references: " +
                string.Join(", ", missing) +
                ". Assign references explicitly in the inspector.",
                this);
        }

        private void WarnMissingAdapterIfNeeded()
        {
            if (_missingAdapterWarningLogged)
                return;

            _missingAdapterWarningLogged = true;
            Debug.LogWarning(
                "[PolarUnified] PolarPmdAdapter reference missing. Assign explicitly in the inspector.",
                this);
        }

        private void OnNativeInitialized(bool success)
        {
            if (logDebug) Debug.Log($"[PolarUnified] Native bridge initialized: {success}");
            if (success && !string.IsNullOrEmpty(_pendingAddress))
            {
                _adapter.ConnectAndSubscribePmd(_pendingAddress);
                _pendingAddress = null;
            }
        }

        private void OnNativeConnected(string address)
        {
            _isConnected = true;
            _attemptingSavedAddressConnect = false;
            StopSavedAddressFallbackRoutine();
            if (logDebug) Debug.Log($"[PolarUnified] Native connected: {address}");
            ConnectionChanged?.Invoke(true);

            if (requestMtuOnConnect)
                _adapter.RequestMtu(desiredMtu);
        }

        private void OnNativeDisconnected(string address)
        {
            _isConnected = false;
            _pmdReady = false;
            _attemptingSavedAddressConnect = false;
            StopSavedAddressFallbackRoutine();
            StopStartRoutine();
            _startIssued = false;
            _mtuRetryIndex = 0;
            _mtuRetryInProgress = false;
            _settingsByMeas.Clear();
            if (logDebug) Debug.Log($"[PolarUnified] Native disconnected: {address}");
            ConnectionChanged?.Invoke(false);
        }

        private void OnNativeHrData(byte[] data)
        {
            if (!enableHr) return;

            HrDataReceived?.Invoke(data);

            if (decodeHr && data != null && data.Length > 0)
            {
                ushort hr = data.GetHr();
                var rr = data.GetRrIntervals();
                HeartRateReceived?.Invoke(hr);
                if (rr != null && rr.Length > 0)
                    RrIntervalsReceived?.Invoke(rr);

                if (logDebug)
                    Debug.Log($"[PolarUnified] HR={hr} bpm, RR count={(rr != null ? rr.Length : 0)}");
            }
        }

        private void OnNativePmdReady()
        {
            _pmdReady = true;
            if (logDebug) Debug.Log("[PolarUnified] Native PMD ready");
            if (_startRoutine == null && !_startIssued)
                _startRoutine = StartCoroutine(StartSelectedStreams());
        }

        private void OnNativePmdCtrlData(byte[] data)
        {
            _pmdCtrlNotifCount++;
            if (data == null || data.Length == 0) return;

            _lastPmdCtrlCmd = data.Length > 1 ? data[1] : (byte)0xFF;
            _lastPmdCtrlMeas = data.Length > 2 ? data[2] : (byte)0xFF;
            _lastPmdCtrlErr = data.Length > 3 ? data[3] : (byte)0xFF;

            TryCapturePmdSettings(data);

            if (logDebug)
            {
                string hex = BitConverter.ToString(data);
                Debug.Log($"[PolarUnified] CTRL data: cmd=0x{_lastPmdCtrlCmd:X2} meas=0x{_lastPmdCtrlMeas:X2} err=0x{_lastPmdCtrlErr:X2} bytes={hex}");
            }

            if (_lastPmdCtrlErr == 0x0A)
                QueueMtuRetry("Invalid MTU");

            PmdCtrlDataReceived?.Invoke(data);
        }

        private void OnNativePmdData(byte[] data)
        {
            if (data == null || data.Length < 10) return;

            PmdDataReceived?.Invoke(data);

            if (!decodeEcg && !decodeAcc) return;

            byte measType = data[0];
            byte frameType = data[9];
            bool compressedAcc = (frameType & 0x80) != 0;
            byte accFrameTypeBase = (byte)(frameType & 0x7F);

            long tsNs;
            try { tsNs = PolarPmdDecoder.ReadTimestampNs(data); }
            catch { return; }
            long receivedTicks = DateTime.UtcNow.Ticks;

            if (decodeEcg && measType == 0 && frameType == 0x00)
            {
                try
                {
                    var samples = PolarPmdDecoder.DecodeEcgMicroVolts(data);
                    EcgFrameReceived?.Invoke(new PolarPmdEcgFrame(tsNs, receivedTicks, samples));
                }
                catch (Exception ex)
                {
                    if (logDebug) Debug.LogWarning($"[PolarUnified] ECG decode failed: {ex.Message}");
                }
                return;
            }

            if (decodeAcc && measType == 2)
            {
                try
                {
                    var samples = PolarPmdDecoder.DecodeAccMilliG(data, compressedAcc, accFrameTypeBase);
                    AccFrameReceived?.Invoke(new PolarPmdAccFrame(tsNs, receivedTicks, samples));
                }
                catch (Exception ex)
                {
                    if (logDebug) Debug.LogWarning($"[PolarUnified] ACC decode failed: {ex.Message}");
                }
            }
        }

        private void OnNativeError(string error)
        {
            if (logDebug) Debug.LogWarning($"[PolarUnified] Native error: {error}");
        }

        private IEnumerator StartSelectedStreams()
        {
            if (_adapter == null || !_adapter.IsPmdReady)
                yield break;

            _startIssued = true;

            yield return new WaitForSeconds(0.5f);

            if (requestMtuOnConnect)
                yield return new WaitForSeconds(Mathf.Max(0f, mtuRequestDelaySeconds));

            _adapter.WritePmdCommand(new byte[] { 0x00 });
            yield return new WaitForSeconds(0.5f);

            if (enableEcg)
            {
                yield return RequestPmdSettingsWithRetries(measType: 0, maxAttempts: 3, perAttemptWaitSeconds: 1.5f);
                yield return StartEcgStream();
            }

            if (enableAcc)
            {
                yield return RequestPmdSettingsWithRetries(measType: 2, maxAttempts: 3, perAttemptWaitSeconds: 1.5f);
                yield return StartAccStream();
            }

            _startRoutine = null;
        }

        private void StopStartRoutine()
        {
            if (_startRoutine != null) StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        private IEnumerator StartEcgStream()
        {
            int sampleRate = preferredEcgSampleRate;
            int resolution = preferredEcgResolution;
            bool fromSettings = false;

            if (TryGetPmdSettings(0, out var settings) && settings.HasAny)
            {
                sampleRate = ChooseLowest(settings.SampleRates, (ushort)sampleRate);
                resolution = ChooseLowest(settings.Resolutions, (ushort)resolution);
                fromSettings = true;
            }

            var candidates = new List<(int sr, int res)>
            {
                (sampleRate, resolution),
                (130, 14),
                (256, 14),
            };

            var seen = new HashSet<string>();
            foreach (var cand in candidates)
            {
                string key = $"{cand.sr}:{cand.res}";
                if (!seen.Add(key)) continue;

                var req = BuildPmdStartRequest(
                    measType: 0,
                    sampleRate: cand.sr,
                    resolution: cand.res,
                    rangeG: null,
                    channels: null);

                if (logDebug)
                {
                    string hex = BitConverter.ToString(req);
                    Debug.Log($"[PolarUnified] START ECG sr={cand.sr} res={cand.res} fromSettings={fromSettings} req={hex}");
                }

                _adapter.WritePmdCommand(req);

                byte err = 0xFF;
                yield return WaitForPmdCtrlResponse(expectedCmd: 0x02, expectedMeas: 0x00, timeoutSeconds: 1.0f, onErr: e => err = e);
                if (err == 0x00)
                    yield break;
            }

            if (logDebug) Debug.LogWarning("[PolarUnified] ECG start failed for all candidates.");
        }

        private IEnumerator StartAccStream()
        {
            int sampleRate = preferredAccSampleRate;
            int resolution = preferredAccResolution;
            int rangeG = preferredAccRangeG;
            bool fromSettings = false;

            if (TryGetPmdSettings(2, out var settings) && settings.HasAny)
            {
                sampleRate = ChooseClosestToPreferred(settings.SampleRates, sampleRate);
                resolution = ChooseClosestToPreferred(settings.Resolutions, resolution);
                rangeG = ChooseClosestToPreferred(settings.Ranges, rangeG);
                fromSettings = true;
            }

            var candidates = new List<(int sr, int res, int range)>
            {
                (sampleRate, resolution, rangeG),
            };

            foreach (var cand in candidates)
            {
                var req = BuildPmdStartAccRequest(
                    sampleRate: cand.sr,
                    resolution: cand.res,
                    rangeG: cand.range);

                if (logDebug)
                {
                    string hex = BitConverter.ToString(req);
                    Debug.Log($"[PolarUnified] START ACC sr={cand.sr} res={cand.res} range={cand.range} fromSettings={fromSettings} req={hex}");
                }

                _adapter.WritePmdCommand(req);

                byte err = 0xFF;
                yield return WaitForPmdCtrlResponse(expectedCmd: 0x02, expectedMeas: 0x02, timeoutSeconds: 1.0f, onErr: e => err = e);
                if (err == 0x00)
                    yield break;
            }

            if (logDebug) Debug.LogWarning("[PolarUnified] ACC start failed.");
        }

        private static byte[] BuildPmdStartRequest(byte measType, int sampleRate, int resolution, int? rangeG, int? channels = null)
        {
            var req = new List<byte>(20);
            req.Add(0x02);
            req.Add(measType);

            req.Add(0x00);
            req.Add(0x01);
            ushort sr = (ushort)Mathf.Clamp(sampleRate, 1, 2000);
            req.Add((byte)(sr & 0xFF));
            req.Add((byte)((sr >> 8) & 0xFF));

            req.Add(0x01);
            req.Add(0x01);
            ushort res = (ushort)Mathf.Clamp(resolution, 1, 32);
            req.Add((byte)(res & 0xFF));
            req.Add((byte)((res >> 8) & 0xFF));

            if (rangeG.HasValue)
            {
                req.Add(0x02);
                req.Add(0x01);
                ushort rng = (ushort)Mathf.Clamp(rangeG.Value, 1, 16);
                req.Add((byte)(rng & 0xFF));
                req.Add((byte)((rng >> 8) & 0xFF));
            }

            if (channels.HasValue)
            {
                req.Add(0x04);
                req.Add(0x01);
                byte ch = (byte)Mathf.Clamp(channels.Value, 1, 3);
                req.Add(ch);
            }

            return req.ToArray();
        }

        private static byte[] BuildPmdStartAccRequest(int sampleRate, int resolution, int rangeG)
        {
            var req = new List<byte>(20);
            req.Add(0x02);
            req.Add(0x02);

            req.Add(0x02);
            req.Add(0x01);
            ushort rng = (ushort)Mathf.Clamp(rangeG, 1, 16);
            req.Add((byte)(rng & 0xFF));
            req.Add((byte)((rng >> 8) & 0xFF));

            req.Add(0x00);
            req.Add(0x01);
            ushort sr = (ushort)Mathf.Clamp(sampleRate, 1, 2000);
            req.Add((byte)(sr & 0xFF));
            req.Add((byte)((sr >> 8) & 0xFF));

            req.Add(0x01);
            req.Add(0x01);
            ushort res = (ushort)Mathf.Clamp(resolution, 1, 32);
            req.Add((byte)(res & 0xFF));
            req.Add((byte)((res >> 8) & 0xFF));

            return req.ToArray();
        }

        private void TryCapturePmdSettings(byte[] data)
        {
            if (data == null || data.Length < 5) return;
            if (data[0] != 0xF0) return;
            if (data[1] != 0x01) return;

            byte measType = data[2];
            byte err = data[3];
            if (err != 0x00)
                return;

            if (!TryParsePmdSettingsPayload(data, 4, out var settings))
            {
                if (!TryParsePmdSettingsPayload(data, 5, out settings))
                    return;
            }

            _settingsByMeas[measType] = settings;
        }

        private static bool TryParsePmdSettingsPayload(byte[] data, int offset, out PmdSettings settings)
        {
            settings = default;
            if (data == null || data.Length <= offset) return false;

            List<ushort> sampleRates = null;
            List<ushort> resolutions = null;
            List<ushort> ranges = null;

            int i = offset;
            while (i + 1 < data.Length)
            {
                byte settingType = data[i++];
                byte count = data[i++];
                int bytesNeeded = count * 2;
                if (i + bytesNeeded > data.Length) break;

                for (int n = 0; n < count; n++)
                {
                    ushort value = (ushort)(data[i] | (data[i + 1] << 8));
                    i += 2;
                    switch (settingType)
                    {
                        case 0x00:
                            (sampleRates ??= new List<ushort>()).Add(value);
                            break;
                        case 0x01:
                            (resolutions ??= new List<ushort>()).Add(value);
                            break;
                        case 0x02:
                            (ranges ??= new List<ushort>()).Add(value);
                            break;
                    }
                }
            }

            settings = new PmdSettings(
                sampleRates?.ToArray() ?? Array.Empty<ushort>(),
                resolutions?.ToArray() ?? Array.Empty<ushort>(),
                ranges?.ToArray() ?? Array.Empty<ushort>());

            return settings.HasAny;
        }

        private bool TryGetPmdSettings(byte measType, out PmdSettings settings)
        {
            return _settingsByMeas.TryGetValue(measType, out settings);
        }

        private static int ChooseLowest(ushort[] values, ushort fallback)
        {
            if (values == null || values.Length == 0) return fallback;
            ushort min = values[0];
            for (int i = 1; i < values.Length; i++)
                if (values[i] < min) min = values[i];
            return min;
        }

        private static int ChooseClosestToPreferred(ushort[] values, int preferred)
        {
            if (values == null || values.Length == 0) return preferred;

            int best = values[0];
            int bestScore = Mathf.Abs(best - preferred);

            for (int i = 1; i < values.Length; i++)
            {
                int candidate = values[i];
                int score = Mathf.Abs(candidate - preferred);
                if (score < bestScore || (score == bestScore && candidate > best))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private IEnumerator WaitForPmdSettingsOrTimeout(byte measType, float timeoutSeconds)
        {
            float t = 0f;
            while (t < timeoutSeconds)
            {
                if (TryGetPmdSettings(measType, out var settings) && settings.HasAny)
                    yield break;
                yield return new WaitForSeconds(0.1f);
                t += 0.1f;
            }
        }

        private IEnumerator RequestPmdSettingsWithRetries(byte measType, int maxAttempts, float perAttemptWaitSeconds)
        {
            maxAttempts = Mathf.Max(1, maxAttempts);
            perAttemptWaitSeconds = Mathf.Max(0.2f, perAttemptWaitSeconds);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                _adapter.WritePmdCommand(new byte[] { 0x01, measType });
                yield return WaitForPmdSettingsOrTimeout(measType, perAttemptWaitSeconds);

                if (TryGetPmdSettings(measType, out var settings) && settings.HasAny)
                    yield break;
            }
        }

        private IEnumerator WaitForPmdCtrlResponse(byte expectedCmd, byte expectedMeas, float timeoutSeconds, Action<byte> onErr)
        {
            float t = 0f;
            int initialCount = _pmdCtrlNotifCount;

            while (t < timeoutSeconds)
            {
                if (_pmdCtrlNotifCount > initialCount &&
                    _lastPmdCtrlCmd == expectedCmd &&
                    _lastPmdCtrlMeas == expectedMeas)
                {
                    onErr?.Invoke(_lastPmdCtrlErr);
                    yield break;
                }

                yield return new WaitForSeconds(0.05f);
                t += 0.05f;
            }

            onErr?.Invoke(0xFF);
        }

        private void QueueMtuRetry(string reason)
        {
            if (!autoRetryOnInvalidMtu) return;
            if (_adapter == null) return;
            if (_mtuRetryInProgress) return;

            int[] candidates = mtuRetryCandidates ?? Array.Empty<int>();
            if (candidates.Length == 0 && desiredMtu <= 0)
                return;

            _mtuRetryInProgress = true;
            StartCoroutine(RetryMtuSequence(reason));
        }

        private IEnumerator RetryMtuSequence(string reason)
        {
            int[] ordered = PolarMtuRetryPlanner.BuildOrderedCandidates(desiredMtu, mtuRetryCandidates);

            if (_mtuRetryIndex >= ordered.Length)
            {
                if (logDebug) Debug.LogWarning("[PolarUnified] MTU retry exhausted; no more candidates.");
                _mtuRetryInProgress = false;
                yield break;
            }

            int targetMtu = ordered[_mtuRetryIndex++];
            if (logDebug) Debug.Log($"[PolarUnified] MTU retry requested ({targetMtu}) due to: {reason}");
            _adapter.RequestMtu(targetMtu);

            yield return new WaitForSeconds(Mathf.Max(0.1f, mtuRetryDelaySeconds));

            _startIssued = false;
            StopStartRoutine();
            if (_pmdReady)
                _startRoutine = StartCoroutine(StartSelectedStreams());

            _mtuRetryInProgress = false;
        }

        private void StartSavedAddressFallbackTimer()
        {
            StopSavedAddressFallbackRoutine();
            float delay = Mathf.Max(0.2f, savedAddressFallbackDelaySeconds);
            _savedAddressFallbackRoutine = StartCoroutine(SavedAddressFallbackAfterDelay(delay));
        }

        private void StopSavedAddressFallbackRoutine()
        {
            if (_savedAddressFallbackRoutine != null)
                StopCoroutine(_savedAddressFallbackRoutine);
            _savedAddressFallbackRoutine = null;
        }

        private IEnumerator SavedAddressFallbackAfterDelay(float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

            if (_isConnected || !_attemptingSavedAddressConnect)
            {
                _savedAddressFallbackRoutine = null;
                yield break;
            }

            if (logDebug)
                Debug.Log($"[PolarUnified] Saved-address fallback -> scan. Reason: timeout after {delaySeconds:F1}s");

            _attemptingSavedAddressConnect = false;
            _savedAddressFallbackRoutine = null;

            if (autoScanWhenNoSavedDevice && central != null)
                StartScan();
        }

        private void HandleRawBleMessage(BleObject obj)
        {
            if (obj == null)
                return;

            if (!string.Equals(obj.Command, "FinishedDiscovering", StringComparison.Ordinal))
                return;

            _scanCommand = null;

            if (_isConnected || !autoScanWhenNoSavedDevice || !autoRescanWhenNoMatch)
                return;

            if (logDebug) Debug.Log("[PolarUnified] Scan finished without match; restarting scan.");
            StartScan();
        }

        private readonly struct PmdSettings
        {
            public readonly ushort[] SampleRates;
            public readonly ushort[] Resolutions;
            public readonly ushort[] Ranges;

            public PmdSettings(ushort[] sampleRates, ushort[] resolutions, ushort[] ranges)
            {
                SampleRates = sampleRates;
                Resolutions = resolutions;
                Ranges = ranges;
            }

            public bool HasAny =>
                (SampleRates != null && SampleRates.Length > 0) ||
                (Resolutions != null && Resolutions.Length > 0) ||
                (Ranges != null && Ranges.Length > 0);
        }

        private void OnDestroy()
        {
            UnhookAdapterEvents();
        }
    }
}


using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Android;
using AstralKarateDojo.RuntimeUtilities;

namespace AstralKarateDojo.Biofeedback.Transport.BLE
{
    /// <summary>
    /// Core BLE transport + command queue for Android BLE plugin.
    /// Does NOT own permission UI (see BlePermissionBootstrap).
    ///
    /// Responsibilities:
    /// - Owns the Android plugin instance and the UnitySendMessage adapter.
    /// - Queues commands (single active + parallel/continuous).
    /// - Exposes readiness state (permissions + runtime conditions).
    ///
    /// Non-responsibilities:
    /// - Does not request permissions directly.
    /// - Does not implement device-specific logic (see Polar modules).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class BleCentral : MonoBehaviour
    {
        public static BleCentral Instance { get; private set; }

        [Header("Adapter")]
        [SerializeField] private BleAdapter adapter;
        [SerializeField] private bool initializeOnAwake = false;
        [SerializeField] private bool beginReadyCheckOnStart = false;
        [SerializeField] private bool persistAcrossScenes = true;

        public bool IsInitialized => _initialized;
        public bool IsReady => _isReady;
        public string LastInitializationError => _lastInitializationError;

        // Invoked on Android runtime when readiness transitions complete.
#pragma warning disable CS0067
        public event Action OnBleReady;
        public event Action<string> OnBleNotReady;
#pragma warning restore CS0067
        public event Action<BleObject> OnRawMessage;
        public event Action<string> OnBleError;

        private bool _isReady;
        private bool _isReadyCheckRunning;
        private Coroutine _readyCoroutine;

        private readonly Queue<BleCommand> _commandQueue = new();
        private readonly List<BleCommand> _parallelStack = new();
        private BleCommand _activeCommand;
        private float _activeTimer;

        private static BleCentral _internalInstance;
        private static bool _initialized;
        private static string _lastInitializationError;
        private static AndroidJavaObject _bleLibrary;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _unityActivity;
        private static AndroidJavaObject UnityActivity
        {
            get
            {
                if (_unityActivity != null) return _unityActivity;
                using var ctxCls = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                _unityActivity = ctxCls.GetStatic<AndroidJavaObject>("currentActivity");
                return _unityActivity;
            }
        }

        private static void RunOnAndroidUiThread(Action action)
        {
            if (action == null) return;
            try
            {
                UnityActivity.Call("runOnUiThread", new AndroidJavaRunnable(action));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BLE] Failed to run on Android UI thread, falling back to direct call. {ex}");
                action();
            }
        }

        private static bool IsAndroidUiThread()
        {
            try
            {
                using var looperCls = new AndroidJavaClass("android.os.Looper");
                using var my = looperCls.CallStatic<AndroidJavaObject>("myLooper");
                using var main = looperCls.CallStatic<AndroidJavaObject>("getMainLooper");
                if (my == null || main == null) return false;
                return my.Call<bool>("equals", main);
            }
            catch
            {
                return false;
            }
        }

        private static void RunOnAndroidUiThreadBlocking(Action action, int timeoutMs = 2500)
        {
            if (action == null) return;

            if (IsAndroidUiThread())
            {
                action();
                return;
            }

            Exception captured = null;
            using var done = new ManualResetEventSlim(false);

            UnityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try { action(); }
                catch (Exception ex) { captured = ex; }
                finally { done.Set(); }
            }));

            if (!done.Wait(timeoutMs))
                throw new TimeoutException($"[BLE] Timed out waiting for Android UI thread ({timeoutMs}ms)");
            if (captured != null)
                throw new Exception("[BLE] Exception on Android UI thread", captured);
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[BLE] Duplicate BleCentral destroyed.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _internalInstance = this;

            if (persistAcrossScenes)
                DontDestroyOnLoad(gameObject);

            if (initializeOnAwake)
                Initialize();
        }

        private void Start()
        {
            if (beginReadyCheckOnStart)
                BeginReadyCheck();
        }

        public void Initialize()
        {
            if (_initialized) return;

            if (adapter == null)
            {
                _lastInitializationError =
                    "BleAdapter reference is missing. Assign BleCentral.adapter explicitly in the inspector.";
                Debug.LogWarning(
                    "[BLE] BleCentral.Initialize aborted: BleAdapter reference is missing. " +
                    "Assign BleCentral.adapter explicitly for deterministic runtime wiring.",
                    this);
                _initialized = false;
                return;
            }

            BindAdapterEvents();

            try
            {
                if (_bleLibrary == null)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    RunOnAndroidUiThreadBlocking(() =>
                    {
                        AndroidJavaClass jCls = new AndroidJavaClass("com.velorexe.unityandroidble.UnityAndroidBLE");
                        _bleLibrary = jCls.CallStatic<AndroidJavaObject>("getInstance");
                    });
#else
                    _lastInitializationError = "BLE transport is available only on Android runtime.";
                    _initialized = false;
                    return;
#endif
                }

                _lastInitializationError = null;
                _initialized = true;
            }
            catch (Exception ex)
            {
                _lastInitializationError = ex.ToString();
                Debug.LogError(
                    "[BLE] BleCentral.Initialize failed. This usually means the Android BLE plugin (AAR/JAR) is missing or not included for Android. " +
                    "Expected Java class: 'com.velorexe.unityandroidble.UnityAndroidBLE'.\n" +
                    ex);
                _initialized = false;
            }
        }

        public void BeginReadyCheck()
        {
            if (_isReadyCheckRunning) return;
            _isReady = false;
            if (_readyCoroutine != null) StopCoroutine(_readyCoroutine);
            _readyCoroutine = StartCoroutine(WaitForBleReady());
        }

        private IEnumerator WaitForBleReady()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _isReadyCheckRunning = true;

            float t = 0f;
            string reason;
            string lastNotReadyReason = null;
            while (!BlePermissionHelper.AreBleRuntimeConditionsMet(out reason))
            {
                if (t == 0f || reason != lastNotReadyReason)
                {
                    lastNotReadyReason = reason;
                    if (!string.IsNullOrEmpty(reason))
                    {
                        OnBleNotReady?.Invoke(reason);
                    }
                }
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
            }

            _isReady = true;
            OnBleReady?.Invoke();
            _isReadyCheckRunning = false;
            yield break;
#else
            _isReadyCheckRunning = false;
            yield break;
#endif
        }

        private void Update()
        {
            if (_activeCommand == null)
                return;

            _activeTimer += Time.deltaTime;

            if (_activeTimer > _activeCommand.Timeout)
            {
                CheckForLog($"[BLE] Timed out: {_activeCommand} - {_activeCommand.Timeout}");
                _activeTimer = 0f;

                TryEndOnTimeout(_activeCommand);
                AdvanceQueue();
            }
        }

        private void OnBleMessageReceived(BleObject obj)
        {
            if (ShouldLogAllMessages())
            {
                CheckForLog("=== BLE MESSAGE RECEIVED ===");
                CheckForLog("Command: " + obj.Command);
                CheckForLog("Device: " + obj.Device);
                CheckForLog("Name: " + obj.Name);
                CheckForLog("Full JSON: " + obj.ToString());
                CheckForLog("===========================");
            }

            OnRawMessage?.Invoke(obj);

            if (_activeCommand != null && TryHandleCommandMessage(_activeCommand, obj))
            {
                TryEndCommand(_activeCommand);
                AdvanceQueue();
            }

            for (int i = _parallelStack.Count - 1; i >= 0; i--)
            {
                BleCommand command = _parallelStack[i];
                if (command == null)
                {
                    _parallelStack.RemoveAt(i);
                    continue;
                }

                if (TryHandleCommandMessage(command, obj))
                {
                    TryEndCommand(command);
                    _parallelStack.RemoveAt(i);
                }
            }
        }

        private void OnBleErrorReceived(string errorMessage)
        {
            CheckForLog($"[BLE] Error: {errorMessage}");
            OnBleError?.Invoke(errorMessage);
        }

        public void QueueCommand(BleCommand command)
        {
            if (command == null) return;

            // Parallel/continuous commands run immediately alongside the active command.
            if (command.RunParallel || command.RunContinuous)
            {
                _parallelStack.Add(command);
                TryStartCommand(command);
            }
            else
            {
                // Single active command at a time (with timeout). Others are queued.
                if (_activeCommand == null)
                {
                    StartActiveCommand(command);
                }
                else
                {
                    _commandQueue.Enqueue(command);
                }
            }
        }

        internal static void SendCommand(string command, params object[] parameters)
        {
            if (_internalInstance != null && ShouldLogAllMessages())
            {
                var paramStr = parameters != null && parameters.Length > 0
                    ? string.Join(", ", parameters)
                    : "(none)";
                CheckForLog($"[BLE CMD] {command} -> {paramStr}");
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            RunOnAndroidUiThread(() => _bleLibrary?.Call(command, parameters));
#else
            _bleLibrary?.Call(command, parameters);
#endif
        }

        private static bool ShouldLogAllMessages()
        {
            return RuntimeLogManager.IsLogChannelEnabled(RuntimeLogManager.RuntimeLogChannel.BleVerboseMessages);
        }

        private static bool ShouldMirrorLogsToUnity()
        {
            return RuntimeLogManager.IsLogChannelEnabled(RuntimeLogManager.RuntimeLogChannel.BleUnityMirror);
        }

        private static bool ShouldMirrorLogsToAndroid()
        {
            return RuntimeLogManager.IsLogChannelEnabled(RuntimeLogManager.RuntimeLogChannel.BleAndroidMirror);
        }

        private static void CheckForLog(string msg)
        {
            if (_internalInstance == null) return;
            if (ShouldMirrorLogsToUnity()) Debug.Log(msg);
            if (ShouldMirrorLogsToAndroid()) AndroidLog(msg);
        }

        private static void AndroidLog(string message)
        {
            if (!_initialized) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            RunOnAndroidUiThread(() => _bleLibrary?.CallStatic("androidLog", message));
#else
            _bleLibrary?.CallStatic("androidLog", message);
#endif
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            if (_readyCoroutine != null)
            {
                StopCoroutine(_readyCoroutine);
                _readyCoroutine = null;
            }

            if (adapter != null)
            {
                adapter.OnMessageReceived -= OnBleMessageReceived;
                adapter.OnErrorReceived -= OnBleErrorReceived;
            }

            if (_activeCommand != null)
                TryEndCommand(_activeCommand);
            for (int i = _parallelStack.Count - 1; i >= 0; i--)
                TryEndCommand(_parallelStack[i]);

            _commandQueue.Clear();
            _parallelStack.Clear();

            Instance = null;
            _internalInstance = null;
            _initialized = false;
            _isReady = false;
            _isReadyCheckRunning = false;
            _bleLibrary = null;
        }

        private void BindAdapterEvents()
        {
            if (adapter == null)
                return;

            adapter.OnMessageReceived -= OnBleMessageReceived;
            adapter.OnErrorReceived -= OnBleErrorReceived;
            adapter.OnMessageReceived += OnBleMessageReceived;
            adapter.OnErrorReceived += OnBleErrorReceived;
        }

        private void StartActiveCommand(BleCommand command)
        {
            _activeCommand = command;
            if (_activeCommand == null)
                return;

            _activeTimer = 0f;
            if (!TryStartCommand(_activeCommand))
            {
                _activeCommand = null;
                AdvanceQueue();
            }
        }

        private void AdvanceQueue()
        {
            _activeCommand = null;
            _activeTimer = 0f;

            while (_commandQueue.Count > 0)
            {
                BleCommand next = _commandQueue.Dequeue();
                if (next == null)
                    continue;

                StartActiveCommand(next);
                return;
            }
        }

        private static bool TryHandleCommandMessage(BleCommand command, BleObject obj)
        {
            if (command == null)
                return false;

            try
            {
                return command.CommandReceived(obj);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BLE] Command message handling failed ({command.GetType().Name}): {ex}");
                return true;
            }
        }

        private static bool TryStartCommand(BleCommand command)
        {
            if (command == null)
                return false;

            try
            {
                command.Start();
                CheckForLog($"[BLE] Executing: {command.GetType().Name}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BLE] Failed to start command {command.GetType().Name}: {ex}");
                return false;
            }
        }

        private static void TryEndCommand(BleCommand command)
        {
            if (command == null)
                return;

            try
            {
                command.End();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BLE] Failed to end command {command.GetType().Name}: {ex}");
            }
        }

        private static void TryEndOnTimeout(BleCommand command)
        {
            if (command == null)
                return;

            try
            {
                command.EndOnTimeout();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BLE] Failed to timeout command {command.GetType().Name}: {ex}");
            }
        }
    }

    /// <summary>
    /// Read-only BLE readiness probe used by BleCentral.
    /// Mirrors BlePermissionBootstrap without owning request UI.
    /// </summary>
    internal static class BlePermissionHelper
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        public static bool AreBleRuntimeConditionsMet(out string reason)
        {
            reason = null;
            int sdkInt;
            using (var v = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkInt = v.GetStatic<int>("SDK_INT");

            if (sdkInt >= 31)
            {
                if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN"))
                { reason = "BLUETOOTH_SCAN not granted"; return false; }
                if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT"))
                { reason = "BLUETOOTH_CONNECT not granted"; return false; }
            }
            else
            {
                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                { reason = "ACCESS_FINE_LOCATION not granted (< Android 12)"; return false; }
                if (!IsLocationEnabled())
                { reason = "Location is OFF (< Android 12)"; return false; }
            }

            return true;
        }

        private static bool IsLocationEnabled()
        {
            using var ctxCls = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = ctxCls.GetStatic<AndroidJavaObject>("currentActivity");
            using var locMgr = activity.Call<AndroidJavaObject>("getSystemService", "location");
            return locMgr != null && locMgr.Call<bool>("isLocationEnabled");
        }
#else
        public static bool AreBleRuntimeConditionsMet(out string reason) { reason = "Non-Android runtime"; return false; }
#endif
    }
}


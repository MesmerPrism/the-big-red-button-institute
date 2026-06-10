using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using TheBigRedButtonInstitute.RustyXrBroker;
using TheBigRedButtonInstitute.VR;
using UnityEngine;

namespace TheBigRedButtonInstitute.Diagnostics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-17)]
    public sealed class BigRedButtonDirectLslDriveReceiver : MonoBehaviour
    {
        const string DefaultStreamName = "HRV_Biofeedback";
        const string DefaultStreamType = "HRV";
        const int WorkerJoinTimeoutMs = 8000;

        readonly ConcurrentQueue<BigRedButtonLslDriveSample> _pendingSamples = new();

        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] BigRedButtonDiagnosticComparisonController comparisonController;
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool startOnEnable = true;

        [Header("LSL Stream")]
        [SerializeField] string streamName = DefaultStreamName;
        [SerializeField] string streamType = DefaultStreamType;
        [SerializeField, Min(0)] int channelIndex;
        [SerializeField] BigRedButtonLslValueMapping valueMapping = BigRedButtonLslValueMapping.Normalized01;
        [SerializeField] float rawInputMin;
        [SerializeField] float rawInputMax = 1f;
        [SerializeField] bool invert01;

        [Header("Timing")]
        [SerializeField] bool autoReconnect = true;
        [SerializeField, Min(0.1f)] float resolveWaitSeconds = 1f;
        [SerializeField, Min(0.1f)] float openStreamTimeoutSeconds = 5f;
        [SerializeField, Min(0f)] float pullTimeoutSeconds = 0.05f;
        [SerializeField, Min(0.1f)] float reconnectDelaySeconds = 3f;
        [SerializeField, Min(0f)] float noSampleReconnectSeconds = 5f;
        [SerializeField, Min(1)] int maxBufferedSeconds = 8;
        [SerializeField, Min(0)] int maxChunkLengthSamples = 1;

        [Header("Button Drive")]
        [SerializeField, Range(0f, 1f)] float triggerThreshold01 = 0.5f;
        [SerializeField, Min(0f)] float minimumTriggerIntervalSeconds = 0.25f;
        [SerializeField] bool triggerOnRisingEdgeOnly = true;

        Thread _workerThread;
        volatile bool _stopRequested;
        volatile bool _running;
        float _previousValue01;
        double _lastTriggerTime = -1d;
        long _receivedSamples;
        long _rejectedSamples;
        long _localSequence;
        string _lastState = "idle";
        string _lastError = string.Empty;
        string _connectedStreamName = string.Empty;
        string _connectedStreamType = string.Empty;
        int _connectedChannelCount;

        public string StreamName => NormalizeFilterValue(streamName);
        public string StreamType => NormalizeFilterValue(streamType);
        public int ChannelIndex => channelIndex;
        public bool IsRunning => _running;
        public long ReceivedSamples => Interlocked.Read(ref _receivedSamples);
        public long RejectedSamples => Interlocked.Read(ref _rejectedSamples);
        public string LastState => string.IsNullOrWhiteSpace(_lastState) ? "idle" : _lastState;
        public string LastError => _lastError ?? string.Empty;
        public string ConnectedStreamName => _connectedStreamName ?? string.Empty;
        public string ConnectedStreamType => _connectedStreamType ?? string.Empty;
        public int ConnectedChannelCount => _connectedChannelCount;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            if (startOnEnable)
            {
                StartReceiver();
            }
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);
            DrainSamples();
        }

        void OnDisable()
        {
            StopReceiver();
        }

        void OnApplicationQuit()
        {
            StopReceiver();
        }

        void OnValidate()
        {
            streamName = NormalizeFilterValue(streamName);
            streamType = NormalizeFilterValue(streamType);
            channelIndex = Mathf.Max(0, channelIndex);
            resolveWaitSeconds = Mathf.Max(0.1f, resolveWaitSeconds);
            openStreamTimeoutSeconds = Mathf.Max(0.1f, openStreamTimeoutSeconds);
            pullTimeoutSeconds = Mathf.Max(0f, pullTimeoutSeconds);
            reconnectDelaySeconds = Mathf.Max(0.1f, reconnectDelaySeconds);
            noSampleReconnectSeconds = Mathf.Max(0f, noSampleReconnectSeconds);
            maxBufferedSeconds = Mathf.Max(1, maxBufferedSeconds);
            maxChunkLengthSamples = Mathf.Max(0, maxChunkLengthSamples);
        }

        public void ConfigureReferences(
            QuestVrInputManager manager,
            BigRedButtonDiagnosticComparisonController controller)
        {
            inputManager = manager;
            comparisonController = controller;
        }

        public void ConfigureStreamFilter(string newStreamName, string newStreamType, int newChannelIndex)
        {
            streamName = NormalizeFilterValue(newStreamName);
            streamType = NormalizeFilterValue(newStreamType);
            channelIndex = Mathf.Max(0, newChannelIndex);
        }

        public void StartReceiver()
        {
            if (_running || (_workerThread != null && _workerThread.IsAlive))
            {
                return;
            }

            var config = BuildWorkerConfig();
            if (string.IsNullOrWhiteSpace(config.StreamName) && string.IsNullOrWhiteSpace(config.StreamType))
            {
                _lastState = "missing stream filter";
                _lastError = "Set LSL stream name or type before starting the direct receiver.";
                Debug.LogWarning("[BigRedButtonDirectLslDriveReceiver] Stream Name and Stream Type are both empty; refusing ambiguous LSL discovery.", this);
                return;
            }

            _stopRequested = false;
            _running = true;
            _lastState = $"resolving {DescribeFilter(config)}";
            _lastError = string.Empty;
            _workerThread = new Thread(() => WorkerLoop(config))
            {
                IsBackground = true,
                Name = "BRB Direct LSL Receiver"
            };
            _workerThread.Start();
        }

        public void StopReceiver()
        {
            _stopRequested = true;
            _running = false;

            var worker = _workerThread;
            if (worker != null && worker.IsAlive && !ReferenceEquals(worker, Thread.CurrentThread))
            {
                worker.Join(WorkerJoinTimeoutMs);
            }

            if (ReferenceEquals(_workerThread, worker))
            {
                _workerThread = null;
            }

            _connectedStreamName = string.Empty;
            _connectedStreamType = string.Empty;
            _connectedChannelCount = 0;
            _lastState = "stopped";
        }

        WorkerConfig BuildWorkerConfig()
        {
            return new WorkerConfig(
                NormalizeFilterValue(streamName),
                NormalizeFilterValue(streamType),
                Mathf.Max(0.1f, resolveWaitSeconds),
                Mathf.Max(0.1f, openStreamTimeoutSeconds),
                Mathf.Max(0f, pullTimeoutSeconds),
                Mathf.Max(0.1f, reconnectDelaySeconds),
                Mathf.Max(0f, noSampleReconnectSeconds),
                Mathf.Max(1, maxBufferedSeconds),
                Mathf.Max(0, maxChunkLengthSamples),
                autoReconnect);
        }

        void WorkerLoop(WorkerConfig config)
        {
            try
            {
                while (!_stopRequested)
                {
                    var connected = false;
                    var info = IntPtr.Zero;
                    var inlet = IntPtr.Zero;
                    var readBuffer = Array.Empty<float>();
                    string activeStreamName = string.Empty;
                    string activeStreamType = string.Empty;

                    try
                    {
                        if (!TryResolveAndOpen(config, out info, out inlet, out readBuffer, out activeStreamName, out activeStreamType, out var foundAny, out var connectError))
                        {
                            if (!string.IsNullOrWhiteSpace(connectError))
                            {
                                _lastState = "connect failed";
                                _lastError = connectError;
                            }
                            else
                            {
                                _lastState = foundAny
                                    ? $"open timeout {DescribeFilter(config)}"
                                    : $"waiting for {DescribeFilter(config)}";
                            }

                            if (!WaitForReconnect(config))
                            {
                                return;
                            }

                            continue;
                        }

                        connected = true;
                        var sequence = 0L;
                        var lastSampleAtUtc = DateTime.UtcNow;
                        _connectedStreamName = activeStreamName;
                        _connectedStreamType = activeStreamType;
                        _connectedChannelCount = readBuffer.Length;
                        _lastState = $"connected {activeStreamName} ({activeStreamType}), ch={readBuffer.Length}";
                        _lastError = string.Empty;

                        while (!_stopRequested)
                        {
                            var errorCode = 0;
                            var timestamp = BigRedButtonLslNative.PullSampleFloat(
                                inlet,
                                readBuffer,
                                config.PullTimeoutSeconds,
                                ref errorCode);

                            if (timestamp == 0d && (errorCode == 0 || errorCode == BigRedButtonLslNative.ErrorTimeout))
                            {
                                if (config.AutoReconnect &&
                                    config.NoSampleReconnectSeconds > 0f &&
                                    (DateTime.UtcNow - lastSampleAtUtc).TotalSeconds >= config.NoSampleReconnectSeconds)
                                {
                                    _lastState = "stream silent; reconnecting";
                                    break;
                                }

                                continue;
                            }

                            if (errorCode < 0)
                            {
                                _lastState = errorCode == BigRedButtonLslNative.ErrorLost
                                    ? "stream lost"
                                    : "read failed";
                                _lastError = BigRedButtonLslNative.DescribeError(errorCode);
                                break;
                            }

                            lastSampleAtUtc = DateTime.UtcNow;
                            sequence++;

                            var sampleCopy = new float[readBuffer.Length];
                            Array.Copy(readBuffer, sampleCopy, sampleCopy.Length);
                            _pendingSamples.Enqueue(new BigRedButtonLslDriveSample(
                                sequence,
                                timestamp,
                                sampleCopy,
                                BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow),
                                activeStreamName,
                                activeStreamType));
                            Interlocked.Increment(ref _receivedSamples);
                        }
                    }
                    catch (Exception ex) when (IsNativeLoadException(ex))
                    {
                        _lastState = "native unavailable";
                        _lastError = ex.Message;
                        Debug.LogWarning($"[BigRedButtonDirectLslDriveReceiver] liblsl is unavailable: {ex.Message}", this);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _lastState = connected ? "read failed" : "connect failed";
                        _lastError = ex.Message;
                    }
                    finally
                    {
                        BigRedButtonLslNative.SafeCloseInlet(inlet);
                        BigRedButtonLslNative.SafeDestroyStreamInfo(info);
                        _connectedStreamName = string.Empty;
                        _connectedStreamType = string.Empty;
                        _connectedChannelCount = 0;
                    }

                    if (!WaitForReconnect(config))
                    {
                        return;
                    }
                }
            }
            finally
            {
                _running = false;
                if (ReferenceEquals(_workerThread, Thread.CurrentThread))
                {
                    _workerThread = null;
                }
            }
        }

        bool TryResolveAndOpen(
            WorkerConfig config,
            out IntPtr info,
            out IntPtr inlet,
            out float[] readBuffer,
            out string resolvedName,
            out string resolvedType,
            out bool foundAny,
            out string connectError)
        {
            info = IntPtr.Zero;
            inlet = IntPtr.Zero;
            readBuffer = Array.Empty<float>();
            resolvedName = string.Empty;
            resolvedType = string.Empty;
            foundAny = false;
            connectError = string.Empty;

            var results = new IntPtr[8];
            var property = !string.IsNullOrWhiteSpace(config.StreamName) ? "name" : "type";
            var value = !string.IsNullOrWhiteSpace(config.StreamName) ? config.StreamName : config.StreamType;
            var resultCount = BigRedButtonLslNative.ResolveByProperty(results, (uint)results.Length, property, value, 1, config.ResolveWaitSeconds);
            if (resultCount <= 0)
            {
                return false;
            }

            foundAny = true;
            info = results[0];
            for (var i = 1; i < resultCount && i < results.Length; i++)
            {
                BigRedButtonLslNative.SafeDestroyStreamInfo(results[i]);
            }

            try
            {
                var channelCount = Math.Max(1, BigRedButtonLslNative.GetChannelCount(info));
                inlet = BigRedButtonLslNative.CreateInlet(info, config.MaxBufferedSeconds, config.MaxChunkLengthSamples, recover: true);
                if (inlet == IntPtr.Zero)
                {
                    connectError = "lsl_create_inlet returned null.";
                    return false;
                }

                var openError = 0;
                BigRedButtonLslNative.OpenStream(inlet, config.OpenStreamTimeoutSeconds, ref openError);
                if (openError < 0)
                {
                    connectError = BigRedButtonLslNative.DescribeError(openError);
                    return false;
                }

                readBuffer = new float[channelCount];
                resolvedName = BigRedButtonLslNative.GetStreamName(info, config.StreamName);
                resolvedType = BigRedButtonLslNative.GetStreamType(info, config.StreamType);
                return true;
            }
            catch
            {
                BigRedButtonLslNative.SafeCloseInlet(inlet);
                inlet = IntPtr.Zero;
                throw;
            }
        }

        bool WaitForReconnect(WorkerConfig config)
        {
            if (!config.AutoReconnect || _stopRequested)
            {
                return false;
            }

            var remainingMs = Mathf.RoundToInt(config.ReconnectDelaySeconds * 1000f);
            while (!_stopRequested && remainingMs > 0)
            {
                var sleepMs = Math.Min(100, remainingMs);
                Thread.Sleep(sleepMs);
                remainingMs -= sleepMs;
            }

            return !_stopRequested;
        }

        void DrainSamples()
        {
            while (_pendingSamples.TryDequeue(out var sample))
            {
                ApplySample(sample);
            }
        }

        void ApplySample(BigRedButtonLslDriveSample sample)
        {
            if (!TryMapSampleValue(sample.Values, out var value))
            {
                Interlocked.Increment(ref _rejectedSamples);
                _lastError = $"channel {channelIndex} unavailable or invalid";
                return;
            }

            var sequence = sample.SequenceId > 0 ? sample.SequenceId : ++_localSequence;
            var nowSeconds = Time.unscaledTimeAsDouble;
            var shouldTrigger = RustyXrBrokerButtonDriver.ShouldTrigger(
                _previousValue01,
                value,
                triggerThreshold01,
                triggerOnRisingEdgeOnly);
            _previousValue01 = value;

            var acceptedPulse = false;
            if (shouldTrigger &&
                (_lastTriggerTime < 0d || nowSeconds - _lastTriggerTime >= minimumTriggerIntervalSeconds))
            {
                acceptedPulse = inputManager != null && inputManager.TriggerButtonPressFromRuntime();
                if (acceptedPulse)
                {
                    _lastTriggerTime = nowSeconds;
                }
            }

            comparisonController?.RecordRouteSample(
                BigRedButtonDiagnosticRouteId.DirectUnityLsl,
                new BigRedButtonDiagnosticSample(
                    sequence,
                    value,
                    0L,
                    0L,
                    sample.UnityReceiveTimeUnixNs > 0L
                        ? sample.UnityReceiveTimeUnixNs
                        : BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow),
                    BuildSourceLabel(sample)),
                acceptedPulse);

            _lastState = acceptedPulse ? $"pulse {value:0.00}" : $"armed {value:0.00}";
            if (acceptedPulse)
            {
                Debug.Log($"[BigRedButtonDirectLslDriveReceiver] direct LSL pulse sequence={sequence} value01={value:0.000} stream={BuildSourceLabel(sample)}", this);
            }
        }

        bool TryMapSampleValue(float[] values, out float value01)
        {
            value01 = 0f;
            if (values == null || channelIndex < 0 || channelIndex >= values.Length)
            {
                return false;
            }

            var raw = values[channelIndex];
            if (float.IsNaN(raw) || float.IsInfinity(raw))
            {
                return false;
            }

            var mapped = valueMapping == BigRedButtonLslValueMapping.RawMinMax && !Mathf.Approximately(rawInputMin, rawInputMax)
                ? Mathf.InverseLerp(rawInputMin, rawInputMax, raw)
                : raw;
            mapped = Mathf.Clamp01(mapped);
            value01 = invert01 ? 1f - mapped : mapped;
            return true;
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (inputManager == null || forceRefresh)
            {
                inputManager = GetComponent<QuestVrInputManager>() ?? FindAnyObjectByType<QuestVrInputManager>();
            }

            if (comparisonController == null || forceRefresh)
            {
                comparisonController = GetComponent<BigRedButtonDiagnosticComparisonController>() ?? FindAnyObjectByType<BigRedButtonDiagnosticComparisonController>();
            }
        }

        static bool IsNativeLoadException(Exception ex)
        {
            return ex is DllNotFoundException ||
                   ex is EntryPointNotFoundException ||
                   ex is BadImageFormatException;
        }

        static string BuildSourceLabel(BigRedButtonLslDriveSample sample)
        {
            var stream = !string.IsNullOrWhiteSpace(sample.StreamName) ? sample.StreamName : "lsl";
            var type = !string.IsNullOrWhiteSpace(sample.StreamType) ? sample.StreamType : "stream";
            return $"{stream}/{type}";
        }

        static string DescribeFilter(WorkerConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.StreamName))
            {
                return $"name='{config.StreamName}'";
            }

            if (!string.IsNullOrWhiteSpace(config.StreamType))
            {
                return $"type='{config.StreamType}'";
            }

            return "<missing>";
        }

        static string NormalizeFilterValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        readonly struct WorkerConfig
        {
            public WorkerConfig(
                string streamName,
                string streamType,
                float resolveWaitSeconds,
                float openStreamTimeoutSeconds,
                float pullTimeoutSeconds,
                float reconnectDelaySeconds,
                float noSampleReconnectSeconds,
                int maxBufferedSeconds,
                int maxChunkLengthSamples,
                bool autoReconnect)
            {
                StreamName = streamName;
                StreamType = streamType;
                ResolveWaitSeconds = resolveWaitSeconds;
                OpenStreamTimeoutSeconds = openStreamTimeoutSeconds;
                PullTimeoutSeconds = pullTimeoutSeconds;
                ReconnectDelaySeconds = reconnectDelaySeconds;
                NoSampleReconnectSeconds = noSampleReconnectSeconds;
                MaxBufferedSeconds = maxBufferedSeconds;
                MaxChunkLengthSamples = maxChunkLengthSamples;
                AutoReconnect = autoReconnect;
            }

            public string StreamName { get; }
            public string StreamType { get; }
            public float ResolveWaitSeconds { get; }
            public float OpenStreamTimeoutSeconds { get; }
            public float PullTimeoutSeconds { get; }
            public float ReconnectDelaySeconds { get; }
            public float NoSampleReconnectSeconds { get; }
            public int MaxBufferedSeconds { get; }
            public int MaxChunkLengthSamples { get; }
            public bool AutoReconnect { get; }
        }
    }

    public enum BigRedButtonLslValueMapping
    {
        Normalized01 = 0,
        RawMinMax = 1
    }

    readonly struct BigRedButtonLslDriveSample
    {
        public BigRedButtonLslDriveSample(
            long sequenceId,
            double lslTimestamp,
            float[] values,
            long unityReceiveTimeUnixNs,
            string streamName,
            string streamType)
        {
            SequenceId = sequenceId;
            LslTimestamp = lslTimestamp;
            Values = values ?? Array.Empty<float>();
            UnityReceiveTimeUnixNs = unityReceiveTimeUnixNs;
            StreamName = streamName ?? string.Empty;
            StreamType = streamType ?? string.Empty;
        }

        public long SequenceId { get; }
        public double LslTimestamp { get; }
        public float[] Values { get; }
        public long UnityReceiveTimeUnixNs { get; }
        public string StreamName { get; }
        public string StreamType { get; }
    }

    static class BigRedButtonLslNative
    {
        const string LibraryName = "lsl";

        public const int ErrorTimeout = -1;
        public const int ErrorLost = -2;
        public const int ErrorArgument = -3;
        public const int ErrorInternal = -4;

        public static int ResolveByProperty(IntPtr[] buffer, uint bufferElements, string property, string value, int minimum, double timeout)
        {
            return lsl_resolve_byprop(buffer, bufferElements, property, value, minimum, timeout);
        }

        public static IntPtr CreateInlet(IntPtr info, int maxBufferedSeconds, int maxChunkLengthSamples, bool recover)
        {
            return lsl_create_inlet(info, maxBufferedSeconds, maxChunkLengthSamples, recover ? 1 : 0);
        }

        public static void OpenStream(IntPtr inlet, double timeout, ref int errorCode)
        {
            lsl_open_stream(inlet, timeout, ref errorCode);
        }

        public static double PullSampleFloat(IntPtr inlet, float[] buffer, double timeout, ref int errorCode)
        {
            return lsl_pull_sample_f(inlet, buffer, buffer.Length, timeout, ref errorCode);
        }

        public static int GetChannelCount(IntPtr info)
        {
            return info == IntPtr.Zero ? 0 : lsl_get_channel_count(info);
        }

        public static string GetStreamName(IntPtr info, string fallback)
        {
            return GetStringOrFallback(info, fallback, lsl_get_name);
        }

        public static string GetStreamType(IntPtr info, string fallback)
        {
            return GetStringOrFallback(info, fallback, lsl_get_type);
        }

        public static void SafeCloseInlet(IntPtr inlet)
        {
            if (inlet == IntPtr.Zero)
            {
                return;
            }

            try { lsl_close_stream(inlet); } catch { }
            try { lsl_destroy_inlet(inlet); } catch { }
        }

        public static void SafeDestroyStreamInfo(IntPtr info)
        {
            if (info == IntPtr.Zero)
            {
                return;
            }

            try { lsl_destroy_streaminfo(info); } catch { }
        }

        public static string DescribeError(int errorCode)
        {
            var lastError = GetLastError();
            var prefix = errorCode switch
            {
                ErrorTimeout => "timeout",
                ErrorLost => "stream lost",
                ErrorArgument => "invalid argument",
                ErrorInternal => "internal liblsl error",
                _ => $"liblsl error {errorCode}"
            };

            return string.IsNullOrWhiteSpace(lastError) ? prefix : $"{prefix}: {lastError}";
        }

        static string GetStringOrFallback(IntPtr info, string fallback, Func<IntPtr, IntPtr> getter)
        {
            if (info != IntPtr.Zero)
            {
                try
                {
                    var pointer = getter(info);
                    var value = pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                catch
                {
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback;
        }

        static string GetLastError()
        {
            try
            {
                var pointer = lsl_last_error();
                return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        [DllImport(LibraryName, EntryPoint = "lsl_resolve_byprop", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern int lsl_resolve_byprop(IntPtr[] buffer, uint bufferElements, string property, string value, int minimum, double timeout);

        [DllImport(LibraryName, EntryPoint = "lsl_create_inlet", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern IntPtr lsl_create_inlet(IntPtr info, int maxBufferedSeconds, int maxChunkLengthSamples, int recover);

        [DllImport(LibraryName, EntryPoint = "lsl_destroy_inlet", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern void lsl_destroy_inlet(IntPtr inlet);

        [DllImport(LibraryName, EntryPoint = "lsl_open_stream", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern void lsl_open_stream(IntPtr inlet, double timeout, ref int errorCode);

        [DllImport(LibraryName, EntryPoint = "lsl_close_stream", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern void lsl_close_stream(IntPtr inlet);

        [DllImport(LibraryName, EntryPoint = "lsl_pull_sample_f", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern double lsl_pull_sample_f(IntPtr inlet, float[] buffer, int bufferElements, double timeout, ref int errorCode);

        [DllImport(LibraryName, EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern void lsl_destroy_streaminfo(IntPtr info);

        [DllImport(LibraryName, EntryPoint = "lsl_get_name", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern IntPtr lsl_get_name(IntPtr info);

        [DllImport(LibraryName, EntryPoint = "lsl_get_type", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern IntPtr lsl_get_type(IntPtr info);

        [DllImport(LibraryName, EntryPoint = "lsl_get_channel_count", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern int lsl_get_channel_count(IntPtr info);

        [DllImport(LibraryName, EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        static extern IntPtr lsl_last_error();
    }
}

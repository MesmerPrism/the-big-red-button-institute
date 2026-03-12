using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AstralKarateDojo.Biofeedback.Transport.BLE.Polar;
using AstralKarateDojo.IndirectParticles;
using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Breathing
{
    /// <summary>
    /// Minimal ACC-only breathing tracker for Polar H10 PMD accelerometer data.
    /// Uses a single adaptive-volume mode:
    /// 1) Detect useful ACC signal
    /// 2) Auto-calibrate a dominant axis + min/max bounds
    /// 3) Emit normalized breathing volume
    /// </summary>
    [DefaultExecutionOrder(-35)]
    public class PolarAccBreathingTracker : MonoBehaviour
    {
        private enum AccBaseMode
        {
            ThreeD = 0,
            Xz = 1
        }

        private struct TimedVectorSample
        {
            public float Time;
            public Vector3 Value;
        }

        private struct TimedScalarSample
        {
            public float Time;
            public float Value;
        }

        [Header("References")]
        [SerializeField] private Transform headsetForwardReference;

        [Header("Direction Disambiguation")]
        [SerializeField] private bool useHeadsetForwardDirection = true;
        [SerializeField] private bool assumeInhaleMovesAlongHeadsetForward = true;
        [Range(0f, 1f)]
        [SerializeField] private float headsetForwardMinAbsDot = 0.10f;

        [Header("Auto Calibration")]
        [SerializeField] private bool autoCalibrateOnUsefulSignal = true;
        [Min(0.25f)]
        [SerializeField] private float usefulSignalWindowSeconds = 4.0f;
        [Min(16)]
        [SerializeField] private int minUsefulSamples = 80;
        [Min(1f)]
        [SerializeField] private float minUsefulSampleRateHz = 20f;
        [Min(0.0005f)]
        [SerializeField] private float minUsefulAxisRangeG = 0.002f;
        [Min(1f)]
        [SerializeField] private float calibrationDurationSeconds = 12f;
        [Min(60)]
        [SerializeField] private int minCalibrationSamples = 240;
        [Min(0.001f)]
        [SerializeField] private float minCalibrationTravelG = 0.010f;
        [Min(0.1f)]
        [SerializeField] private float calibrationRetryDelaySeconds = 2f;

        [Header("Signal Processing")]
        [Range(0.01f, 1f)]
        [SerializeField] private float sampleEmaAlpha = 0.10f;
        [Range(0f, 0.25f)]
        [SerializeField] private float boundsLowerQuantile = 0.05f;
        [Range(0.75f, 1f)]
        [SerializeField] private float boundsUpperQuantile = 0.95f;
        [Range(0f, 0.2f)]
        [SerializeField] private float boundsEdgeEase = 0.03f;
        [Range(0.01f, 1f)]
        [SerializeField] private float projectionEmaAlpha = 0.10f;
        [SerializeField] private bool invertVolume = false;

        [Header("ACC Base Mode")]
        [Tooltip("Default breathing base from accelerometer data. Xz keeps output in X/Z-only mode.")]
        [SerializeField] private AccBaseMode accBaseMode = AccBaseMode.Xz;

        [Header("Adaptive Bounds")]
        [SerializeField] private bool useAdaptiveBounds = true;
        [Min(4f)]
        [SerializeField] private float adaptiveBoundsWindowSeconds = 20f;
        [Range(0.5f, 1f)]
        [SerializeField] private float adaptiveBoundsMinInitialRangeFactor = 0.75f;
        [Min(1f)]
        [SerializeField] private float adaptiveBoundsMaxInitialRangeFactor = 1.35f;
        [Range(0.25f, 1f)]
        [SerializeField] private float adaptiveBoundsMinWindowCoverage = 0.85f;
        [Min(0.1f)]
        [SerializeField] private float adaptiveBoundsUpdateIntervalSeconds = 0.5f;
        [Min(0.05f)]
        [SerializeField] private float adaptiveBoundsLerpSpeed = 0.35f;
        [Range(0.1f, 1f)]
        [SerializeField] private float adaptiveBoundsContractSpeedMultiplier = 0.45f;
        [Min(16)]
        [SerializeField] private int minAdaptiveBoundsSamples = 640;

        [Header("Runtime Guards")]
        [Min(0.1f)]
        [SerializeField] private float staleTimeoutSeconds = 3.0f;
        [Range(0.0001f, 0.05f)]
        [SerializeField] private float volumeEventMinDelta = 0.001f;

        [Header("Runtime Config")]
        [SerializeField] private bool loadRuntimeConfigOnEnable = true;
        [Tooltip("Path relative to Application.persistentDataPath. Absolute paths are also supported.")]
        [SerializeField] private string runtimeConfigRelativePath = PolarBreathRuntimeConfigCsv.DefaultRelativePath;

        [Header("Logging")]
        [SerializeField] private bool logDebug = true;
        [Min(0.2f)]
        [SerializeField] private float logIntervalSeconds = 1.0f;
        [SerializeField] private bool logActiveTuningOnEnable = true;

        [Header("Publish")]
        [SerializeField] private bool publishToSignalRegistry = true;
        [SerializeField] private string volumeSignalName = "polar_acc_breath_volume";
        [SerializeField] private string trackingSignalName = "polar_acc_breath_tracking";
        [SerializeField] private string sourceName = "PolarAccTransportRouter";

        public event Action<float> OnVolumeChanged;
        public event Action<bool> OnCalibrationChanged;

        public float CurrentVolume { get; private set; } = 0.5f;
        public bool IsCalibrated => _isCalibrated;
        public bool IsCalibrating => _isCalibrating;
        public float EstimatedSampleRateHz => _sampleRateHzEma;
        public float CalibrationProgress01
        {
            get
            {
                if (!_isCalibrating)
                    return _isCalibrated ? 1f : 0f;

                float duration = Mathf.Max(0.1f, ActiveCalibrationDurationSeconds);
                return Mathf.Clamp01((Time.unscaledTime - _calibrationStartTime) / duration);
            }
        }

        public string ActiveSourceName =>
            string.IsNullOrWhiteSpace(sourceName) ? nameof(PolarAccTransportRouter) : sourceName.Trim();
        public bool IsSourceConnected => IsActiveSourceConnected();
        public bool HasUsefulSignal => _hasUsefulSignal;
        public float UsefulAxisRangeG => _latestUsefulAxisRangeG;
        public string LastCalibrationFailureReason => _lastCalibrationFailureReason;
        public Vector3 CalibratedAxis => _axis;
        public float BoundMin => _boundMin;
        public float BoundMax => _boundMax;

        private readonly List<TimedVectorSample> _warmupSamples = new List<TimedVectorSample>(1024);
        private readonly List<TimedVectorSample> _calibrationSamples = new List<TimedVectorSample>(4096);
        private readonly List<float> _projectionScratch = new List<float>(4096);
        private readonly List<TimedScalarSample> _adaptiveProjectionSamples = new List<TimedScalarSample>(8192);

        private bool _isCalibrating;
        private bool _isCalibrated;
        private bool _isTransportConnected;
        private float _calibrationStartTime;
        private float _nextAutoCalibrationTime;
        private string _lastCalibrationFailureReason = string.Empty;

        private bool _hasFilteredSample;
        private Vector3 _filteredAccG;
        private bool _hasProjectionEma;
        private float _projectionEma;
        private bool _hasXzProjectionEma;
        private float _xzProjectionEma;

        private Vector3 _axis = Vector3.up;
        private Vector3 _center = Vector3.zero;
        private float _boundMin = -0.02f;
        private float _boundMax = 0.02f;
        private float _initialBoundSpan;
        private float _nextAdaptiveBoundsUpdateAt;
        private float _lastAdaptiveBoundsUpdateAt = -1f;
        private float _latestProjection;

        private Vector2 _xzAxis = Vector2.right;
        private float _xzBoundMin = -0.02f;
        private float _xzBoundMax = 0.02f;
        private float _xzInitialBoundSpan;
        private bool _hasXzModel;

        private bool _hasUsefulSignal;
        private float _latestUsefulAxisRangeG;

        private float _lastFrameAt = -1f;
        private bool _hasLastSensorFrameTimestamp;
        private long _lastSensorFrameTimestampNs;
        private float _estimatedSampleDtSeconds = 0.005f;
        private float _lastProcessedSampleAt = -1f;
        private float _sampleRateHzEma;
        private float _lastSampleAt = -1f;
        private bool _hasReceivedAnySample;
        private bool _staleWarningIssued;

        private int _accFrameCountSinceLog;
        private int _accSampleCountSinceLog;
        private int _rawPmdAccPacketCountSinceLog;
        private int _rawPmdOtherPacketCountSinceLog;
        private int _rawPmdByteCountSinceLog;
        private float _lastLogAt;
        private float _nextLogAt;

        private readonly List<TimedScalarSample> _adaptiveXzProjectionSamples = new List<TimedScalarSample>(8192);
        private readonly List<float> _fusionScratch = new List<float>(512);
        private float _lastAcc3dVolume;
        private float _lastAccBaseVolume;
        private float _lastAccXzVolume;

        private float ActiveUsefulSignalWindowSeconds => usefulSignalWindowSeconds;
        private int ActiveMinUsefulSamples => minUsefulSamples;
        private float ActiveMinUsefulSampleRateHz => minUsefulSampleRateHz;
        private float ActiveMinUsefulAxisRangeG => minUsefulAxisRangeG;
        private float ActiveCalibrationDurationSeconds => calibrationDurationSeconds;
        private int ActiveMinCalibrationSamples => minCalibrationSamples;
        private float ActiveMinCalibrationTravelG => minCalibrationTravelG;
        private float ActiveCalibrationRetryDelaySeconds => calibrationRetryDelaySeconds;
        private float ActiveSampleEmaAlpha => sampleEmaAlpha;
        private float ActiveBoundsLowerQuantile => boundsLowerQuantile;
        private float ActiveBoundsUpperQuantile => boundsUpperQuantile;
        private float ActiveBoundsEdgeEase => boundsEdgeEase;
        private float ActiveProjectionEmaAlpha => projectionEmaAlpha;
        private bool ActiveInvertVolume => invertVolume;
        private AccBaseMode ActiveAccBaseMode => accBaseMode;
        private bool ActiveUseAdaptiveBounds => useAdaptiveBounds;
        private float ActiveAdaptiveBoundsWindowSeconds => adaptiveBoundsWindowSeconds;
        private float ActiveAdaptiveBoundsMinInitialRangeFactor => adaptiveBoundsMinInitialRangeFactor;
        private float ActiveAdaptiveBoundsMaxInitialRangeFactor => adaptiveBoundsMaxInitialRangeFactor;
        private float ActiveAdaptiveBoundsMinWindowCoverage => adaptiveBoundsMinWindowCoverage;
        private float ActiveAdaptiveBoundsUpdateIntervalSeconds => adaptiveBoundsUpdateIntervalSeconds;
        private float ActiveAdaptiveBoundsLerpSpeed => adaptiveBoundsLerpSpeed;
        private float ActiveAdaptiveBoundsContractSpeedMultiplier => adaptiveBoundsContractSpeedMultiplier;
        private int ActiveMinAdaptiveBoundsSamples => minAdaptiveBoundsSamples;
        private bool ActiveUseHeadsetForwardDirection => useHeadsetForwardDirection;
        private bool ActiveAssumeInhaleMovesAlongHeadsetForward => assumeInhaleMovesAlongHeadsetForward;
        private float ActiveHeadsetForwardMinAbsDot => headsetForwardMinAbsDot;
        private float ActiveStaleTimeoutSeconds => staleTimeoutSeconds;
        private float ActiveVolumeEventMinDelta => volumeEventMinDelta;

        private void Awake()
        {
        }

        private void OnEnable()
        {
            if (loadRuntimeConfigOnEnable)
                ReloadRuntimeConfigFromDisk();

            ResetRuntimeState();

            _lastLogAt = Time.unscaledTime;
            _nextLogAt = _lastLogAt + Mathf.Max(0.2f, logIntervalSeconds);

            if (logDebug)
                Debug.Log($"[PolarAccBreath] Enabled. Source={ActiveSourceName}");

            if (logDebug && logActiveTuningOnEnable)
            {
                string directionAnchor = ActiveUseHeadsetForwardDirection
                    ? (ActiveAssumeInhaleMovesAlongHeadsetForward ? "HeadForward" : "-HeadForward")
                    : "Disabled";
                Debug.Log(
                    "[PolarAccBreath] Tuning " +
                    $"calDur={ActiveCalibrationDurationSeconds:F1}s minCalSamples={ActiveMinCalibrationSamples} " +
                    $"sampleEma={ActiveSampleEmaAlpha:F3} projEma={ActiveProjectionEmaAlpha:F3} " +
                    $"boundsQ=[{ActiveBoundsLowerQuantile:F2},{ActiveBoundsUpperQuantile:F2}] edgeEase={ActiveBoundsEdgeEase:F3}");
                Debug.Log(
                    $"[PolarAccBreath] directionAnchor={directionAnchor} minAbsDot={ActiveHeadsetForwardMinAbsDot:F2} " +
                    $"invertVolume={ActiveInvertVolume}");
                Debug.Log(
                    $"[PolarAccBreath] adaptiveBounds={(ActiveUseAdaptiveBounds ? "On" : "Off")} " +
                    $"window={ActiveAdaptiveBoundsWindowSeconds:F1}s minSpanFactor={ActiveAdaptiveBoundsMinInitialRangeFactor:F2} maxSpanFactor={ActiveAdaptiveBoundsMaxInitialRangeFactor:F2} " +
                    $"coverage={ActiveAdaptiveBoundsMinWindowCoverage:F2} minSamples={ActiveMinAdaptiveBoundsSamples} " +
                    $"update={ActiveAdaptiveBoundsUpdateIntervalSeconds:F2}s expand={ActiveAdaptiveBoundsLerpSpeed:F2} contractMul={ActiveAdaptiveBoundsContractSpeedMultiplier:F2}");
                Debug.Log($"[PolarAccBreath] accBaseMode={ActiveAccBaseMode}");
            }
        }

        private void OnDisable()
        {
            _isTransportConnected = false;
            if (logDebug)
                Debug.Log("[PolarAccBreath] Disabled.");
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (_hasReceivedAnySample)
            {
                float age = now - _lastSampleAt;
                if (age > Mathf.Max(0.1f, ActiveStaleTimeoutSeconds))
                {
                    // Keep consumers from holding a stale extrema value when ACC stream pauses.
                    SetVolume(0.5f);
                    if (!_staleWarningIssued && logDebug)
                    {
                        _staleWarningIssued = true;
                        Debug.LogWarning($"[PolarAccBreath] ACC data stale for {age:F2}s.");
                    }
                }
            }

            if (_isCalibrating && now - _calibrationStartTime >= Mathf.Max(0.1f, ActiveCalibrationDurationSeconds))
                CompleteCalibrationAttempt(now);

            if (_isCalibrated)
                UpdateAdaptiveBounds(now);

            if (logDebug && now >= _nextLogAt)
            {
                LogSummary(now);
                _nextLogAt = now + Mathf.Max(0.2f, logIntervalSeconds);
            }
        }

        public void BeginCalibration()
        {
            StartCalibration("manual request");
        }

        public void CancelCalibration()
        {
            if (!_isCalibrating)
                return;

            _isCalibrating = false;
            _calibrationSamples.Clear();
            _nextAutoCalibrationTime = Time.unscaledTime + Mathf.Max(0.1f, ActiveCalibrationRetryDelaySeconds);
            SetCalibration(false);
            SetVolume(0.5f, forceEvent: true);

            if (logDebug)
                Debug.Log("[PolarAccBreath] Calibration cancelled.");
        }

        public void ResetTracker()
        {
            bool wasCalibrated = _isCalibrated;
            ResetRuntimeState();
            if (wasCalibrated)
                OnCalibrationChanged?.Invoke(false);
            SetVolume(0.5f, forceEvent: true);

            if (logDebug)
                Debug.Log("[PolarAccBreath] Tracker reset.");
        }

        public bool ReloadRuntimeConfigFromDisk()
        {
            string configPath = ResolveRuntimeConfigPath();
            if (!File.Exists(configPath))
            {
                if (logDebug)
                    Debug.Log($"[PolarRuntimeConfig] No config file found at '{configPath}'. Using inspector values.");
                return false;
            }

            if (!PolarBreathRuntimeConfigCsv.TryReadEntries(configPath, out List<PolarRuntimeConfigEntry> entries, out string parseError))
            {
                Debug.LogWarning($"[PolarRuntimeConfig] Failed to parse '{configPath}': {parseError}");
                return false;
            }

            int appliedCount = 0;
            int ignoredCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PolarRuntimeConfigEntry entry = entries[i];
                if (TryApplyRuntimeConfigEntry(entry.Key, entry.Value, out string appliedValue, out string error))
                {
                    appliedCount++;
                    Debug.Log($"[PolarRuntimeConfig] Applied {entry.Key}={appliedValue} (line {entry.LineNumber}).");
                }
                else
                {
                    ignoredCount++;
                    Debug.LogWarning($"[PolarRuntimeConfig] Ignored key '{entry.Key}' (line {entry.LineNumber}): {error}");
                }
            }

            Debug.Log($"[PolarRuntimeConfig] Loaded {appliedCount} override(s), ignored {ignoredCount} from '{configPath}'.");
            return appliedCount > 0;
        }

        private string ResolveRuntimeConfigPath()
        {
            string path = runtimeConfigRelativePath;
            if (string.IsNullOrWhiteSpace(path))
                path = PolarBreathRuntimeConfigCsv.DefaultRelativePath;

            path = path.Trim();
            if (Path.IsPathRooted(path))
                return path;

            string normalized = path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(Application.persistentDataPath, normalized);
        }

        private bool TryApplyRuntimeConfigEntry(string key, string rawValue, out string appliedValue, out string error)
        {
            string normalizedKey = NormalizeConfigKey(key);
            string value = rawValue?.Trim()?.Trim('"') ?? string.Empty;

            switch (normalizedKey)
            {
                case "accbasemode":
                    return TryApplyAccBaseMode(value, out appliedValue, out error);
                case "sampleemaalpha":
                    return TryApplyFloat(value, 0.01f, 1f, v => sampleEmaAlpha = v, out appliedValue, out error);
                case "projectionemaalpha":
                    return TryApplyFloat(value, 0.01f, 1f, v => projectionEmaAlpha = v, out appliedValue, out error);
                case "boundslowerquantile":
                    return TryApplyFloat(value, 0f, 0.25f, v => boundsLowerQuantile = v, out appliedValue, out error);
                case "boundsupperquantile":
                    return TryApplyFloat(value, 0.75f, 1f, v => boundsUpperQuantile = v, out appliedValue, out error);
                case "boundsedgeease":
                    return TryApplyFloat(value, 0f, 0.2f, v => boundsEdgeEase = v, out appliedValue, out error);
                case "invertvolume":
                    return TryApplyBool(value, v => invertVolume = v, out appliedValue, out error);
                case "autocalibrateonusefulsignal":
                    return TryApplyBool(value, v => autoCalibrateOnUsefulSignal = v, out appliedValue, out error);
                case "usefulsignalwindowseconds":
                    return TryApplyFloat(value, 0.25f, 120f, v => usefulSignalWindowSeconds = v, out appliedValue, out error);
                case "minusefulsamples":
                    return TryApplyInt(value, 16, 100000, v => minUsefulSamples = v, out appliedValue, out error);
                case "minusefulsampleratehz":
                    return TryApplyFloat(value, 1f, 1000f, v => minUsefulSampleRateHz = v, out appliedValue, out error);
                case "minusefulaxisrangeg":
                    return TryApplyFloat(value, 0.0005f, 1f, v => minUsefulAxisRangeG = v, out appliedValue, out error);
                case "calibrationdurationseconds":
                    return TryApplyFloat(value, 1f, 120f, v => calibrationDurationSeconds = v, out appliedValue, out error);
                case "mincalibrationsamples":
                    return TryApplyInt(value, 60, 100000, v => minCalibrationSamples = v, out appliedValue, out error);
                case "mincalibrationtravelg":
                    return TryApplyFloat(value, 0.001f, 1f, v => minCalibrationTravelG = v, out appliedValue, out error);
                case "calibrationretrydelayseconds":
                    return TryApplyFloat(value, 0.1f, 120f, v => calibrationRetryDelaySeconds = v, out appliedValue, out error);
                case "useadaptivebounds":
                    return TryApplyBool(value, v => useAdaptiveBounds = v, out appliedValue, out error);
                case "adaptiveboundswindowseconds":
                    return TryApplyFloat(value, 4f, 300f, v => adaptiveBoundsWindowSeconds = v, out appliedValue, out error);
                case "adaptiveboundsmininitialrangefactor":
                    return TryApplyFloat(value, 0.5f, 1f, v => adaptiveBoundsMinInitialRangeFactor = v, out appliedValue, out error);
                case "adaptiveboundsmaxinitialrangefactor":
                    return TryApplyFloat(value, 1f, 10f, v => adaptiveBoundsMaxInitialRangeFactor = v, out appliedValue, out error);
                case "adaptiveboundsminwindowcoverage":
                    return TryApplyFloat(value, 0.25f, 1f, v => adaptiveBoundsMinWindowCoverage = v, out appliedValue, out error);
                case "adaptiveboundsupdateintervalseconds":
                    return TryApplyFloat(value, 0.1f, 30f, v => adaptiveBoundsUpdateIntervalSeconds = v, out appliedValue, out error);
                case "adaptiveboundslerpspeed":
                    return TryApplyFloat(value, 0.05f, 10f, v => adaptiveBoundsLerpSpeed = v, out appliedValue, out error);
                case "adaptiveboundscontractspeedmultiplier":
                    return TryApplyFloat(value, 0.1f, 1f, v => adaptiveBoundsContractSpeedMultiplier = v, out appliedValue, out error);
                case "minadaptiveboundssamples":
                    return TryApplyInt(value, 16, 200000, v => minAdaptiveBoundsSamples = v, out appliedValue, out error);
                case "useheadsetforwarddirection":
                    return TryApplyBool(value, v => useHeadsetForwardDirection = v, out appliedValue, out error);
                case "assumeinhalemovesalongheadsetforward":
                    return TryApplyBool(value, v => assumeInhaleMovesAlongHeadsetForward = v, out appliedValue, out error);
                case "headsetforwardminabsdot":
                    return TryApplyFloat(value, 0f, 1f, v => headsetForwardMinAbsDot = v, out appliedValue, out error);
                case "staletimeoutseconds":
                    return TryApplyFloat(value, 0.1f, 120f, v => staleTimeoutSeconds = v, out appliedValue, out error);
                case "volumeeventmindelta":
                    return TryApplyFloat(value, 0.0001f, 0.05f, v => volumeEventMinDelta = v, out appliedValue, out error);
                default:
                    appliedValue = string.Empty;
                    error = "Unknown key.";
                    return false;
            }
        }

        private bool TryApplyAccBaseMode(string value, out string appliedValue, out string error)
        {
            if (value.Equals("xz", StringComparison.OrdinalIgnoreCase))
            {
                accBaseMode = AccBaseMode.Xz;
                appliedValue = nameof(AccBaseMode.Xz);
                error = string.Empty;
                return true;
            }

            if (value.Equals("3d", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("threed", StringComparison.OrdinalIgnoreCase))
            {
                accBaseMode = AccBaseMode.ThreeD;
                appliedValue = nameof(AccBaseMode.ThreeD);
                error = string.Empty;
                return true;
            }

            appliedValue = string.Empty;
            error = "Expected accBaseMode to be 'Xz' or 'ThreeD'.";
            return false;
        }

        private static string NormalizeConfigKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            string normalized = key.Trim().ToLowerInvariant();
            normalized = normalized.Replace("_", string.Empty);
            normalized = normalized.Replace("-", string.Empty);
            normalized = normalized.Replace(" ", string.Empty);
            return normalized;
        }

        private static bool TryApplyBool(string rawValue, Action<bool> apply, out string appliedValue, out string error)
        {
            if (!TryParseBool(rawValue, out bool parsed))
            {
                appliedValue = string.Empty;
                error = $"Invalid boolean value '{rawValue}'.";
                return false;
            }

            apply(parsed);
            appliedValue = parsed ? "true" : "false";
            error = string.Empty;
            return true;
        }

        private static bool TryApplyInt(string rawValue, int min, int max, Action<int> apply, out string appliedValue, out string error)
        {
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                appliedValue = string.Empty;
                error = $"Invalid integer value '{rawValue}'.";
                return false;
            }

            int clamped = Mathf.Clamp(parsed, min, max);
            apply(clamped);
            appliedValue = FormatClampedValue(parsed, clamped);
            error = string.Empty;
            return true;
        }

        private static bool TryApplyFloat(string rawValue, float min, float max, Action<float> apply, out string appliedValue, out string error)
        {
            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
                float.IsNaN(parsed) ||
                float.IsInfinity(parsed))
            {
                appliedValue = string.Empty;
                error = $"Invalid float value '{rawValue}'.";
                return false;
            }

            float clamped = Mathf.Clamp(parsed, min, max);
            apply(clamped);
            appliedValue = FormatClampedValue(parsed, clamped);
            error = string.Empty;
            return true;
        }

        private static bool TryParseBool(string value, out bool parsed)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    parsed = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    parsed = false;
                    return true;
                default:
                    parsed = false;
                    return false;
            }
        }

        private static string FormatClampedValue(int original, int clamped)
        {
            if (original == clamped)
                return clamped.ToString(CultureInfo.InvariantCulture);

            return $"{clamped.ToString(CultureInfo.InvariantCulture)} (clamped from {original.ToString(CultureInfo.InvariantCulture)})";
        }

        private static string FormatClampedValue(float original, float clamped)
        {
            string originalText = original.ToString("0.#######", CultureInfo.InvariantCulture);
            string clampedText = clamped.ToString("0.#######", CultureInfo.InvariantCulture);
            if (Mathf.Approximately(original, clamped))
                return clampedText;

            return $"{clampedText} (clamped from {originalText})";
        }

        private bool TryGetHeadsetForward(out Vector3 headsetForward, out string sourceName)
        {
            headsetForward = Vector3.zero;
            sourceName = string.Empty;

            if (headsetForwardReference == null)
                return false;

            headsetForward = headsetForwardReference.forward;
            float mag = headsetForward.magnitude;
            if (mag < 1e-6f)
                return false;

            headsetForward /= mag;
            sourceName = headsetForwardReference.name;
            return true;
        }

        public void SetTransportConnected(bool isConnected)
        {
            _isTransportConnected = isConnected;
            PublishOutputSignals();
        }

        public void SubmitAccFrame(PolarPmdAccFrame frame)
        {
            if (!isActiveAndEnabled)
                return;

            if (frame.Samples == null || frame.Samples.Length == 0)
                return;

            float now = Time.unscaledTime;
            _hasReceivedAnySample = true;
            _lastSampleAt = now;
            _staleWarningIssued = false;

            _accFrameCountSinceLog++;
            _accSampleCountSinceLog += frame.Samples.Length;

            float frameDtSec = 0f;
            if (_hasLastSensorFrameTimestamp && frame.SensorTimestampNs > _lastSensorFrameTimestampNs)
            {
                double sensorDt = (frame.SensorTimestampNs - _lastSensorFrameTimestampNs) * 1e-9;
                if (sensorDt > 0.0001 && sensorDt < 1.0)
                    frameDtSec = (float)sensorDt;
            }
            else if (_lastFrameAt > 0f)
            {
                float hostDt = now - _lastFrameAt;
                if (hostDt > 0.0001f && hostDt < 1f)
                    frameDtSec = hostDt;
            }

            if (frameDtSec > 0.0001f)
            {
                float frameSampleRate = frame.Samples.Length / frameDtSec;
                if (_sampleRateHzEma <= 0f)
                    _sampleRateHzEma = frameSampleRate;
                else
                    _sampleRateHzEma = Mathf.Lerp(_sampleRateHzEma, frameSampleRate, 0.20f);
            }

            float sampleDtSec;
            if (frameDtSec > 0.0001f)
                sampleDtSec = frameDtSec / Mathf.Max(1, frame.Samples.Length);
            else if (_sampleRateHzEma > 1f)
                sampleDtSec = 1f / _sampleRateHzEma;
            else
                sampleDtSec = _estimatedSampleDtSeconds;

            sampleDtSec = Mathf.Clamp(sampleDtSec, 0.001f, 0.05f);
            _estimatedSampleDtSeconds = Mathf.Lerp(_estimatedSampleDtSeconds, sampleDtSec, 0.20f);

            _lastFrameAt = now;
            _lastSensorFrameTimestampNs = frame.SensorTimestampNs;
            _hasLastSensorFrameTimestamp = true;

            for (int i = 0; i < frame.Samples.Length; i++)
            {
                int samplesFromEnd = frame.Samples.Length - 1 - i;
                float sampleNow = now - (samplesFromEnd * sampleDtSec);
                if (_lastProcessedSampleAt > 0f && sampleNow <= _lastProcessedSampleAt)
                    sampleNow = _lastProcessedSampleAt + sampleDtSec;

                var s = frame.Samples[i];
                Vector3 rawG = new Vector3(s.X, s.Y, s.Z) * 0.001f;
                ProcessSample(rawG, sampleNow);
                _lastProcessedSampleAt = sampleNow;
            }

            if (!_isCalibrated)
                EvaluateUsefulSignalAndMaybeAutoCalibrate(now);
        }

        public void SubmitRawPmdPacket(byte[] data)
        {
            if (!isActiveAndEnabled)
                return;

            if (data == null || data.Length == 0)
                return;

            _rawPmdByteCountSinceLog += data.Length;
            if (data[0] == 0x02)
                _rawPmdAccPacketCountSinceLog++;
            else
                _rawPmdOtherPacketCountSinceLog++;
        }

        private void ProcessSample(Vector3 rawG, float now)
        {
            if (!_hasFilteredSample)
            {
                _filteredAccG = rawG;
                _hasFilteredSample = true;
            }
            else
            {
                float alpha = Mathf.Clamp01(ActiveSampleEmaAlpha);
                _filteredAccG = Vector3.Lerp(_filteredAccG, rawG, alpha);
            }

            if (!_isCalibrated)
                AddWarmupSample(now, _filteredAccG);

            if (_isCalibrating)
            {
                _calibrationSamples.Add(new TimedVectorSample
                {
                    Time = now,
                    Value = _filteredAccG
                });
            }

            if (_isCalibrated)
                UpdateBreathingFromFilteredSample(_filteredAccG, now);
        }

        private void AddWarmupSample(float now, Vector3 value)
        {
            _warmupSamples.Add(new TimedVectorSample
            {
                Time = now,
                Value = value
            });

            TrimTimedSamples(_warmupSamples, now - Mathf.Max(0.25f, ActiveUsefulSignalWindowSeconds), hardCap: 6000);
        }

        private void EvaluateUsefulSignalAndMaybeAutoCalibrate(float now)
        {
            bool hasUseful = TryGetUsefulSignalStats(out float axisRange, out string reason);
            _latestUsefulAxisRangeG = axisRange;

            if (_hasUsefulSignal != hasUseful)
            {
                _hasUsefulSignal = hasUseful;
                if (logDebug)
                {
                    if (hasUseful)
                        Debug.Log($"[PolarAccBreath] Useful ACC signal detected. axisRange={axisRange:F4}g sampleRate={_sampleRateHzEma:F1}Hz");
                    else
                        Debug.Log($"[PolarAccBreath] Useful ACC signal lost. reason={reason}");
                }
            }

            if (!autoCalibrateOnUsefulSignal || _isCalibrated || _isCalibrating)
                return;

            if (now < _nextAutoCalibrationTime)
                return;

            if (!hasUseful)
                return;

            StartCalibration($"auto (axisRange={axisRange:F4}g)");
        }

        private bool TryGetUsefulSignalStats(out float axisRange, out string reason)
        {
            axisRange = 0f;
            reason = string.Empty;

            int available = _warmupSamples.Count;
            if (available >= 2)
            {
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

                for (int i = 0; i < available; i++)
                {
                    Vector3 v = _warmupSamples[i].Value;
                    if (v.x < minX) minX = v.x;
                    if (v.x > maxX) maxX = v.x;
                    if (v.y < minY) minY = v.y;
                    if (v.y > maxY) maxY = v.y;
                    if (v.z < minZ) minZ = v.z;
                    if (v.z > maxZ) maxZ = v.z;
                }

                axisRange = Mathf.Max(maxX - minX, Mathf.Max(maxY - minY, maxZ - minZ));
            }

            int neededSamples = Mathf.Max(16, ActiveMinUsefulSamples);
            if (available < neededSamples)
            {
                reason = $"samples {available}/{neededSamples}";
                return false;
            }

            if (_sampleRateHzEma > 0f && _sampleRateHzEma < Mathf.Max(1f, ActiveMinUsefulSampleRateHz))
            {
                reason = $"sampleRate {_sampleRateHzEma:F1}Hz < {ActiveMinUsefulSampleRateHz:F1}Hz";
                return false;
            }

            float requiredRange = Mathf.Max(0.0005f, ActiveMinUsefulAxisRangeG);
            if (axisRange < requiredRange)
            {
                reason = $"axisRange {axisRange:F4}g < {requiredRange:F4}g";
                return false;
            }

            return true;
        }

        private void StartCalibration(string reason)
        {
            _isCalibrating = true;
            _calibrationStartTime = Time.unscaledTime;
            _calibrationSamples.Clear();
            _hasProjectionEma = false;
            _hasXzProjectionEma = false;
            _adaptiveProjectionSamples.Clear();
            _adaptiveXzProjectionSamples.Clear();
            _nextAdaptiveBoundsUpdateAt = 0f;
            _lastAdaptiveBoundsUpdateAt = -1f;
            _initialBoundSpan = 0f;
            _xzInitialBoundSpan = 0f;
            _hasXzModel = false;
            _lastCalibrationFailureReason = string.Empty;
            SetCalibration(false);
            SetVolume(0.5f, forceEvent: true);

            if (logDebug)
                Debug.Log($"[PolarAccBreath] Calibration started ({reason}). duration={ActiveCalibrationDurationSeconds:F1}s");
        }

        private void CompleteCalibrationAttempt(float now)
        {
            if (!_isCalibrating)
                return;

            if (!TryBuildCalibrationModel(
                    out Vector3 center,
                    out Vector3 axis,
                    out float boundMin,
                    out float boundMax,
                    out Vector2 xzAxis,
                    out float xzBoundMin,
                    out float xzBoundMax,
                    out float rawTravelG,
                    out float rawTravelXzG,
                    out string error))
            {
                FailCalibration(error);
                return;
            }

            _center = center;
            _axis = axis;
            _boundMin = boundMin;
            _boundMax = boundMax;
            _initialBoundSpan = Mathf.Max(0.001f, _boundMax - _boundMin);
            _xzAxis = xzAxis;
            _xzBoundMin = xzBoundMin;
            _xzBoundMax = xzBoundMax;
            _xzInitialBoundSpan = Mathf.Max(0.001f, _xzBoundMax - _xzBoundMin);
            _hasXzModel = rawTravelXzG > 0.0005f;
            _isCalibrating = false;
            _hasProjectionEma = false;
            _hasXzProjectionEma = false;
            _adaptiveProjectionSamples.Clear();
            _adaptiveXzProjectionSamples.Clear();
            _nextAdaptiveBoundsUpdateAt = now + Mathf.Max(0.1f, ActiveAdaptiveBoundsUpdateIntervalSeconds);
            _lastAdaptiveBoundsUpdateAt = now;
            _lastCalibrationFailureReason = string.Empty;
            SetCalibration(true);

            if (_hasFilteredSample)
                UpdateBreathingFromFilteredSample(_filteredAccG, now);

            if (logDebug)
            {
                float span = _boundMax - _boundMin;
                Debug.Log(
                    $"[PolarAccBreath] Calibration success. samples={_calibrationSamples.Count} rawTravel={rawTravelG:F4}g " +
                    $"span={span:F4}g bounds=[{_boundMin:F4}, {_boundMax:F4}] axis=({_axis.x:F3}, {_axis.y:F3}, {_axis.z:F3})");
                if (_hasXzModel)
                {
                    float xzSpan = _xzBoundMax - _xzBoundMin;
                    Debug.Log(
                        $"[PolarAccBreath] XZ model rawTravel={rawTravelXzG:F4}g span={xzSpan:F4}g " +
                        $"xzAxis=({_xzAxis.x:F3}, {_xzAxis.y:F3}) xzBounds=[{_xzBoundMin:F4}, {_xzBoundMax:F4}]");
                }
                else if (ActiveAccBaseMode == AccBaseMode.Xz)
                {
                    Debug.LogWarning("[PolarAccBreath] accBaseMode=Xz but XZ model was weak; falling back to 3D base.");
                }
            }
        }

        private bool TryBuildCalibrationModel(
            out Vector3 center,
            out Vector3 axis,
            out float boundMin,
            out float boundMax,
            out Vector2 xzAxis,
            out float xzBoundMin,
            out float xzBoundMax,
            out float rawTravelG,
            out float rawTravelXzG,
            out string error)
        {
            center = Vector3.zero;
            axis = Vector3.up;
            boundMin = 0f;
            boundMax = 0f;
            xzAxis = Vector2.right;
            xzBoundMin = 0f;
            xzBoundMax = 0f;
            rawTravelG = 0f;
            rawTravelXzG = 0f;
            error = string.Empty;

            int sampleCount = _calibrationSamples.Count;
            int requiredSamples = Mathf.Max(16, ActiveMinCalibrationSamples);
            if (sampleCount < requiredSamples)
            {
                error = $"not enough samples ({sampleCount}/{requiredSamples})";
                return false;
            }

            for (int i = 0; i < sampleCount; i++)
                center += _calibrationSamples[i].Value;
            center /= sampleCount;

            float c00 = 0f, c01 = 0f, c02 = 0f, c11 = 0f, c12 = 0f, c22 = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 d = _calibrationSamples[i].Value - center;
                c00 += d.x * d.x;
                c01 += d.x * d.y;
                c02 += d.x * d.z;
                c11 += d.y * d.y;
                c12 += d.y * d.z;
                c22 += d.z * d.z;
            }

            float inv = 1f / sampleCount;
            c00 *= inv;
            c01 *= inv;
            c02 *= inv;
            c11 *= inv;
            c12 *= inv;
            c22 *= inv;

            axis = _axis.sqrMagnitude > 1e-6f ? _axis : Vector3.up;
            for (int iter = 0; iter < 6; iter++)
            {
                axis = new Vector3(
                    c00 * axis.x + c01 * axis.y + c02 * axis.z,
                    c01 * axis.x + c11 * axis.y + c12 * axis.z,
                    c02 * axis.x + c12 * axis.y + c22 * axis.z);

                float mag = axis.magnitude;
                if (mag < 1e-6f)
                {
                    error = "principal axis estimation failed";
                    return false;
                }

                axis /= mag;
            }

            if (_isCalibrated && _axis.sqrMagnitude > 1e-6f && Vector3.Dot(axis, _axis) < 0f)
                axis = -axis;

            if (ActiveUseHeadsetForwardDirection)
            {
                if (TryGetHeadsetForward(out Vector3 headsetForward, out string sourceName))
                {
                    Vector3 desired = ActiveAssumeInhaleMovesAlongHeadsetForward ? headsetForward : -headsetForward;
                    float alignment = Vector3.Dot(axis, desired);
                    float minAbsDot = Mathf.Clamp01(ActiveHeadsetForwardMinAbsDot);
                    float absAlignment = Mathf.Abs(alignment);
                    if (absAlignment >= minAbsDot)
                    {
                        bool flipped = alignment < 0f;
                        if (flipped)
                            axis = -axis;

                        if (logDebug)
                        {
                            Debug.Log(
                                $"[PolarAccBreath] Direction anchor applied. source={sourceName} " +
                                $"dot={absAlignment:F3} flipped={flipped} " +
                                $"inhaleAlong={(ActiveAssumeInhaleMovesAlongHeadsetForward ? "headForward" : "-headForward")}");
                        }
                    }
                    else if (logDebug)
                    {
                        Debug.LogWarning(
                            $"[PolarAccBreath] Direction anchor skipped (weak alignment). " +
                            $"absDot={absAlignment:F3} < {minAbsDot:F3}. Keeping PCA sign.");
                    }
                }
                else if (logDebug)
                {
                    Debug.LogWarning("[PolarAccBreath] Direction anchor skipped (headset forward unavailable).");
                }
            }

            _projectionScratch.Clear();
            for (int i = 0; i < sampleCount; i++)
            {
                float p = Vector3.Dot(_calibrationSamples[i].Value - center, axis);
                _projectionScratch.Add(p);
            }

            if (!PEAdaptiveBoundsMath.TryComputeQuantileBoundsInPlace(
                    _projectionScratch,
                    ActiveBoundsLowerQuantile,
                    ActiveBoundsUpperQuantile,
                    PEQuantileSamplingMode.LinearInterpolation,
                    out float lo,
                    out float hi))
            {
                error = "collapsed quantile bounds";
                return false;
            }
            rawTravelG = Mathf.Max(0f, hi - lo);

            float requiredTravel = Mathf.Max(0.001f, ActiveMinCalibrationTravelG);
            if (rawTravelG < requiredTravel)
            {
                error = $"insufficient travel ({rawTravelG:F4}g < {requiredTravel:F4}g)";
                return false;
            }

            boundMin = lo;
            boundMax = hi;
            PEAdaptiveBoundsMath.ApplyEdgeEase(ref boundMin, ref boundMax, ActiveBoundsEdgeEase);

            if (boundMax - boundMin < 1e-6f)
            {
                error = "collapsed bounds after edge easing";
                return false;
            }

            TryBuildXzModel(center, out xzAxis, out xzBoundMin, out xzBoundMax, out rawTravelXzG);
            return true;
        }

        private void TryBuildXzModel(
            Vector3 center,
            out Vector2 xzAxis,
            out float xzBoundMin,
            out float xzBoundMax,
            out float rawTravelXzG)
        {
            xzAxis = _xzAxis.sqrMagnitude > 1e-6f ? _xzAxis.normalized : Vector2.right;
            xzBoundMin = -0.02f;
            xzBoundMax = 0.02f;
            rawTravelXzG = 0f;

            int sampleCount = _calibrationSamples.Count;
            if (sampleCount < 8)
                return;

            float c00 = 0f, c01 = 0f, c11 = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 d = _calibrationSamples[i].Value - center;
                c00 += d.x * d.x;
                c01 += d.x * d.z;
                c11 += d.z * d.z;
            }

            float inv = 1f / sampleCount;
            c00 *= inv;
            c01 *= inv;
            c11 *= inv;

            for (int iter = 0; iter < 6; iter++)
            {
                Vector2 next = new Vector2(
                    c00 * xzAxis.x + c01 * xzAxis.y,
                    c01 * xzAxis.x + c11 * xzAxis.y);

                float mag = next.magnitude;
                if (mag < 1e-6f)
                    break;
                xzAxis = next / mag;
            }

            if (_hasXzModel && Vector2.Dot(xzAxis, _xzAxis) < 0f)
                xzAxis = -xzAxis;

            if (ActiveUseHeadsetForwardDirection && TryGetHeadsetForward(out Vector3 headsetForward, out _))
            {
                Vector2 desired = new Vector2(headsetForward.x, headsetForward.z);
                if (desired.sqrMagnitude > 1e-6f)
                {
                    desired.Normalize();
                    if (!ActiveAssumeInhaleMovesAlongHeadsetForward)
                        desired = -desired;

                    float dot = Vector2.Dot(xzAxis, desired);
                    float absDot = Mathf.Abs(dot);
                    if (absDot >= Mathf.Clamp01(ActiveHeadsetForwardMinAbsDot) && dot < 0f)
                        xzAxis = -xzAxis;
                }
            }

            _fusionScratch.Clear();
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 d = _calibrationSamples[i].Value - center;
                float pxz = Vector2.Dot(new Vector2(d.x, d.z), xzAxis);
                _fusionScratch.Add(pxz);
            }

            if (!PEAdaptiveBoundsMath.TryComputeQuantileBoundsInPlace(
                    _fusionScratch,
                    ActiveBoundsLowerQuantile,
                    ActiveBoundsUpperQuantile,
                    PEQuantileSamplingMode.LinearInterpolation,
                    out float lo,
                    out float hi))
            {
                xzBoundMin = 0f;
                xzBoundMax = 0f;
                rawTravelXzG = 0f;
                return;
            }
            rawTravelXzG = Mathf.Max(0f, hi - lo);

            xzBoundMin = lo;
            xzBoundMax = hi;
            PEAdaptiveBoundsMath.ApplyEdgeEase(ref xzBoundMin, ref xzBoundMax, ActiveBoundsEdgeEase);

            float minSpan = Mathf.Max(0.005f, ActiveMinCalibrationTravelG * 0.5f);
            PEAdaptiveBoundsMath.EnforceSpanBounds(ref xzBoundMin, ref xzBoundMax, minSpan, float.MaxValue);
        }

        private void FailCalibration(string reason)
        {
            _isCalibrating = false;
            _calibrationSamples.Clear();
            _lastCalibrationFailureReason = reason ?? "unknown error";
            _nextAutoCalibrationTime = Time.unscaledTime + Mathf.Max(0.1f, ActiveCalibrationRetryDelaySeconds);
            SetCalibration(false);
            SetVolume(0.5f, forceEvent: true);

            if (logDebug)
            {
                Debug.LogWarning(
                    $"[PolarAccBreath] Calibration failed: {_lastCalibrationFailureReason}. " +
                    $"Next auto-attempt in {Mathf.Max(0.1f, ActiveCalibrationRetryDelaySeconds):F1}s.");
            }
        }

        private void UpdateBreathingFromFilteredSample(Vector3 filteredAccG, float now)
        {
            Vector3 centered = filteredAccG - _center;
            float projection = Vector3.Dot(centered, _axis);
            _latestProjection = projection;

            if (!_hasProjectionEma)
            {
                _projectionEma = projection;
                _hasProjectionEma = true;
            }
            else
            {
                _projectionEma = Mathf.Lerp(_projectionEma, projection, Mathf.Clamp01(ActiveProjectionEmaAlpha));
            }

            float volume3d = Mathf.Clamp01(Mathf.InverseLerp(_boundMin, _boundMax, _projectionEma));
            float xzVolume = volume3d;
            bool hasXzProjection = false;
            float xzProjectionForBounds = 0f;

            if (_hasXzModel)
            {
                float xzProjection = Vector2.Dot(new Vector2(centered.x, centered.z), _xzAxis);
                if (!_hasXzProjectionEma)
                {
                    _xzProjectionEma = xzProjection;
                    _hasXzProjectionEma = true;
                }
                else
                {
                    _xzProjectionEma = Mathf.Lerp(_xzProjectionEma, xzProjection, Mathf.Clamp01(ActiveProjectionEmaAlpha));
                }

                hasXzProjection = true;
                xzProjectionForBounds = _xzProjectionEma;
                xzVolume = Mathf.Clamp01(Mathf.InverseLerp(_xzBoundMin, _xzBoundMax, _xzProjectionEma));
            }

            RecordAdaptiveProjectionSample(now, _projectionEma, xzProjectionForBounds, hasXzProjection);

            bool useXzBase = ActiveAccBaseMode == AccBaseMode.Xz && _hasXzModel;
            float baseVolume = useXzBase ? xzVolume : volume3d;
            float outputVolume = baseVolume;
            _lastAcc3dVolume = volume3d;
            _lastAccBaseVolume = baseVolume;
            _lastAccXzVolume = xzVolume;

            if (ActiveInvertVolume)
                outputVolume = 1f - outputVolume;

            SetVolume(Mathf.Clamp01(outputVolume));
        }

        private void RecordAdaptiveProjectionSample(float now, float projection, float xzProjection, bool hasXzProjection)
        {
            if (!ActiveUseAdaptiveBounds || !_isCalibrated)
                return;

            _adaptiveProjectionSamples.Add(new TimedScalarSample
            {
                Time = now,
                Value = projection
            });

            int hardCap = Mathf.Max(2048, Mathf.RoundToInt(Mathf.Max(4f, ActiveAdaptiveBoundsWindowSeconds) * Mathf.Max(20f, _sampleRateHzEma) * 2.5f));
            if (_adaptiveProjectionSamples.Count > hardCap)
                _adaptiveProjectionSamples.RemoveRange(0, _adaptiveProjectionSamples.Count - hardCap);

            if (hasXzProjection && _hasXzModel)
            {
                _adaptiveXzProjectionSamples.Add(new TimedScalarSample
                {
                    Time = now,
                    Value = xzProjection
                });

                if (_adaptiveXzProjectionSamples.Count > hardCap)
                    _adaptiveXzProjectionSamples.RemoveRange(0, _adaptiveXzProjectionSamples.Count - hardCap);
            }
        }

        private void UpdateAdaptiveBounds(float now)
        {
            if (!ActiveUseAdaptiveBounds || _isCalibrating)
                return;

            float windowSeconds = Mathf.Max(4f, ActiveAdaptiveBoundsWindowSeconds);
            TrimTimedScalarSamples(_adaptiveProjectionSamples, now - windowSeconds, hardCap: 0);
            if (_hasXzModel)
                TrimTimedScalarSamples(_adaptiveXzProjectionSamples, now - windowSeconds, hardCap: 0);

            int requiredSamples = ComputeAdaptiveRequiredSamples(_sampleRateHzEma);
            if (_adaptiveProjectionSamples.Count < requiredSamples)
                return;

            if (now < _nextAdaptiveBoundsUpdateAt)
                return;

            _nextAdaptiveBoundsUpdateAt = now + Mathf.Max(0.1f, ActiveAdaptiveBoundsUpdateIntervalSeconds);

            float dt = _lastAdaptiveBoundsUpdateAt >= 0f
                ? Mathf.Max(0.0001f, now - _lastAdaptiveBoundsUpdateAt)
                : Mathf.Max(0.1f, ActiveAdaptiveBoundsUpdateIntervalSeconds);
            _lastAdaptiveBoundsUpdateAt = now;

            UpdateAdaptiveBoundsChannel(_adaptiveProjectionSamples, _initialBoundSpan, dt, ref _boundMin, ref _boundMax);

            if (_hasXzModel && _adaptiveXzProjectionSamples.Count >= requiredSamples)
                UpdateAdaptiveBoundsChannel(_adaptiveXzProjectionSamples, _xzInitialBoundSpan, dt, ref _xzBoundMin, ref _xzBoundMax);
        }

        private void UpdateAdaptiveBoundsChannel(List<TimedScalarSample> samples, float initialSpan, float dt, ref float boundMin, ref float boundMax)
        {
            _projectionScratch.Clear();
            for (int i = 0; i < samples.Count; i++)
                _projectionScratch.Add(samples[i].Value);

            if (!PEAdaptiveBoundsMath.TryComputeQuantileBoundsInPlace(
                    _projectionScratch,
                    ActiveBoundsLowerQuantile,
                    ActiveBoundsUpperQuantile,
                    PEQuantileSamplingMode.LinearInterpolation,
                    out float lo,
                    out float hi))
                return;
            float rawTravel = Mathf.Max(0f, hi - lo);
            if (rawTravel < 1e-6f)
                return;

            float targetMin = lo;
            float targetMax = hi;
            PEAdaptiveBoundsMath.ApplyEdgeEase(ref targetMin, ref targetMax, ActiveBoundsEdgeEase);
            if (targetMax - targetMin < 1e-6f)
                return;

            float minSpan = Mathf.Max(0.001f, initialSpan * Mathf.Clamp(ActiveAdaptiveBoundsMinInitialRangeFactor, 0.01f, 1f));
            float maxSpan = initialSpan > 0f
                ? Mathf.Max(minSpan, initialSpan * Mathf.Max(1f, ActiveAdaptiveBoundsMaxInitialRangeFactor))
                : float.MaxValue;
            PEAdaptiveBoundsMath.EnforceSpanBounds(ref targetMin, ref targetMax, minSpan, maxSpan);

            float expandSpeed = Mathf.Max(0.01f, ActiveAdaptiveBoundsLerpSpeed);
            float contractSpeed = Mathf.Max(0.01f, expandSpeed * Mathf.Clamp(ActiveAdaptiveBoundsContractSpeedMultiplier, 0.1f, 1f));
            float minSpeed = targetMin < boundMin ? expandSpeed : contractSpeed;
            float maxSpeed = targetMax > boundMax ? expandSpeed : contractSpeed;
            float minLerpT = PEAdaptiveBoundsMath.ComputeExponentialLerp(minSpeed, dt);
            float maxLerpT = PEAdaptiveBoundsMath.ComputeExponentialLerp(maxSpeed, dt);

            float newMin = Mathf.Lerp(boundMin, targetMin, minLerpT);
            float newMax = Mathf.Lerp(boundMax, targetMax, maxLerpT);
            PEAdaptiveBoundsMath.EnforceSpanBounds(ref newMin, ref newMax, minSpan, maxSpan);

            boundMin = newMin;
            boundMax = newMax;
        }

        private static void TrimTimedSamples(List<TimedVectorSample> list, float cutoffTime, int hardCap)
        {
            int remove = 0;
            while (remove < list.Count && list[remove].Time < cutoffTime)
                remove++;

            if (remove > 0)
                list.RemoveRange(0, remove);

            if (hardCap > 0 && list.Count > hardCap)
                list.RemoveRange(0, list.Count - hardCap);
        }

        private static void TrimTimedScalarSamples(List<TimedScalarSample> list, float cutoffTime, int hardCap)
        {
            int remove = 0;
            while (remove < list.Count && list[remove].Time < cutoffTime)
                remove++;

            if (remove > 0)
                list.RemoveRange(0, remove);

            if (hardCap > 0 && list.Count > hardCap)
                list.RemoveRange(0, list.Count - hardCap);
        }

        private int ComputeAdaptiveRequiredSamples(float sampleRateHz)
        {
            float windowSeconds = Mathf.Max(4f, ActiveAdaptiveBoundsWindowSeconds);
            float sampleRate = Mathf.Clamp(sampleRateHz, Mathf.Max(5f, ActiveMinUsefulSampleRateHz), 320f);
            float coverage = Mathf.Clamp(ActiveAdaptiveBoundsMinWindowCoverage, 0.25f, 1f);
            int coverageSamples = Mathf.RoundToInt(windowSeconds * sampleRate * coverage);
            return Mathf.Max(Mathf.Max(16, ActiveMinAdaptiveBoundsSamples), coverageSamples);
        }

        private void PublishOutputSignals()
        {
            if (!publishToSignalRegistry)
                return;

            string volumeKey = NormalizeSignalName(volumeSignalName);
            if (!string.IsNullOrEmpty(volumeKey))
                PEBiofeedbackSignalRegistry.Publish(volumeKey, CurrentVolume);

            string trackingKey = NormalizeSignalName(trackingSignalName);
            if (!string.IsNullOrEmpty(trackingKey))
            {
                float tracking = IsActiveSourceConnected() && _hasReceivedAnySample && _isCalibrated ? 1f : 0f;
                PEBiofeedbackSignalRegistry.Publish(trackingKey, tracking);
            }
        }

        private static string NormalizeSignalName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private void SetVolume(float volume, bool forceEvent = false)
        {
            float clamped = Mathf.Clamp01(volume);
            bool changedEnough = Mathf.Abs(CurrentVolume - clamped) >= Mathf.Max(0.0001f, ActiveVolumeEventMinDelta);
            CurrentVolume = clamped;
            PublishOutputSignals();

            if (forceEvent || changedEnough)
                OnVolumeChanged?.Invoke(CurrentVolume);
        }

        private void SetCalibration(bool calibrated)
        {
            if (_isCalibrated == calibrated)
                return;

            _isCalibrated = calibrated;
            PublishOutputSignals();
            OnCalibrationChanged?.Invoke(_isCalibrated);
        }

        private void ResetRuntimeState()
        {
            _isCalibrating = false;
            _isCalibrated = false;
            _calibrationStartTime = 0f;
            _nextAutoCalibrationTime = 0f;
            _lastCalibrationFailureReason = string.Empty;

            _hasFilteredSample = false;
            _filteredAccG = Vector3.zero;
            _hasProjectionEma = false;
            _projectionEma = 0f;
            _latestProjection = 0f;
            _hasXzProjectionEma = false;
            _xzProjectionEma = 0f;
            CurrentVolume = 0.5f;

            _axis = Vector3.up;
            _center = Vector3.zero;
            _boundMin = -0.02f;
            _boundMax = 0.02f;
            _initialBoundSpan = 0f;
            _xzAxis = Vector2.right;
            _xzBoundMin = -0.02f;
            _xzBoundMax = 0.02f;
            _xzInitialBoundSpan = 0f;
            _hasXzModel = false;
            _nextAdaptiveBoundsUpdateAt = 0f;
            _lastAdaptiveBoundsUpdateAt = -1f;

            _warmupSamples.Clear();
            _calibrationSamples.Clear();
            _projectionScratch.Clear();
            _adaptiveProjectionSamples.Clear();
            _adaptiveXzProjectionSamples.Clear();
            _fusionScratch.Clear();

            _hasUsefulSignal = false;
            _latestUsefulAxisRangeG = 0f;

            _lastFrameAt = -1f;
            _hasLastSensorFrameTimestamp = false;
            _lastSensorFrameTimestampNs = 0L;
            _estimatedSampleDtSeconds = 0.005f;
            _lastProcessedSampleAt = -1f;
            _sampleRateHzEma = 0f;
            _lastSampleAt = -1f;
            _hasReceivedAnySample = false;
            _staleWarningIssued = false;

            _accFrameCountSinceLog = 0;
            _accSampleCountSinceLog = 0;
            _rawPmdAccPacketCountSinceLog = 0;
            _rawPmdOtherPacketCountSinceLog = 0;
            _rawPmdByteCountSinceLog = 0;
            _lastAcc3dVolume = 0.5f;
            _lastAccBaseVolume = 0.5f;
            _lastAccXzVolume = 0.5f;
            PublishOutputSignals();
        }

        private bool IsActiveSourceConnected()
        {
            return _isTransportConnected;
        }

        private void LogSummary(float now)
        {
            float dt = Mathf.Max(0.001f, now - _lastLogAt);
            float fps = _accFrameCountSinceLog / dt;
            float sps = _accSampleCountSinceLog / dt;
            float rawAccPacketsPerSec = _rawPmdAccPacketCountSinceLog / dt;
            float rawOtherPacketsPerSec = _rawPmdOtherPacketCountSinceLog / dt;
            float decodeRatio = _rawPmdAccPacketCountSinceLog > 0
                ? (float)_accFrameCountSinceLog / _rawPmdAccPacketCountSinceLog
                : 0f;
            bool connected = IsActiveSourceConnected();

            string calibrationStatus = _isCalibrating
                ? $"Calibrating {CalibrationProgress01 * 100f:0}%"
                : _isCalibrated
                    ? "Ready"
                    : "Waiting";

            float span = _boundMax - _boundMin;
            float minSpan = _initialBoundSpan > 0f
                ? _initialBoundSpan * Mathf.Clamp(ActiveAdaptiveBoundsMinInitialRangeFactor, 0.01f, 1f)
                : 0f;
            float maxSpan = _initialBoundSpan > 0f
                ? _initialBoundSpan * Mathf.Max(1f, ActiveAdaptiveBoundsMaxInitialRangeFactor)
                : 0f;
            int adaptiveRequiredSamples = ComputeAdaptiveRequiredSamples(_sampleRateHzEma);
            float xzSpan = _xzBoundMax - _xzBoundMin;
            float selectedSpan = (ActiveAccBaseMode == AccBaseMode.Xz && _hasXzModel) ? xzSpan : span;

            Debug.Log(
                $"[PolarAccBreath] src={ActiveSourceName} connected={connected} " +
                $"ACC {fps:F1} fps / {sps:F0} sps (ema={_sampleRateHzEma:F1}Hz) " +
                $"rawAccPkts={rawAccPacketsPerSec:F1}/s rawOtherPkts={rawOtherPacketsPerSec:F1}/s rawBytes={_rawPmdByteCountSinceLog} " +
                $"decodeRatio={decodeRatio:F2} " +
                $"vol={CurrentVolume:F3} cal={calibrationStatus} " +
                $"useful={_hasUsefulSignal} axisRange={_latestUsefulAxisRangeG:F4}g " +
                $"baseMode={ActiveAccBaseMode} proj={_latestProjection:F4}g span={span:F4}g xzSpan={xzSpan:F4}g selectedSpan={selectedSpan:F4}g " +
                $"minSpan={minSpan:F4}g maxSpan={maxSpan:F4}g adaptN={_adaptiveProjectionSamples.Count}/{adaptiveRequiredSamples}");

            Debug.Log(
                $"[PolarAccBreath] mode={ActiveAccBaseMode} acc3d={_lastAcc3dVolume:F3} accXZ={_lastAccXzVolume:F3} base={_lastAccBaseVolume:F3}");

            if (_isCalibrated)
            {
                Debug.Log(
                    $"[PolarAccBreath] model axis=({_axis.x:F3}, {_axis.y:F3}, {_axis.z:F3}) " +
                    $"center=({_center.x:F3}, {_center.y:F3}, {_center.z:F3}) bounds=[{_boundMin:F4}, {_boundMax:F4}]");
                if (_hasXzModel)
                {
                    Debug.Log(
                        $"[PolarAccBreath] modelXZ xzAxis=({_xzAxis.x:F3}, {_xzAxis.y:F3}) bounds=[{_xzBoundMin:F4}, {_xzBoundMax:F4}]");
                }
            }
            else if (!_isCalibrating && !string.IsNullOrEmpty(_lastCalibrationFailureReason))
            {
                Debug.Log($"[PolarAccBreath] lastCalibrationFailure={_lastCalibrationFailureReason}");
            }

            _accFrameCountSinceLog = 0;
            _accSampleCountSinceLog = 0;
            _rawPmdAccPacketCountSinceLog = 0;
            _rawPmdOtherPacketCountSinceLog = 0;
            _rawPmdByteCountSinceLog = 0;
            _lastLogAt = now;
        }
    }
}


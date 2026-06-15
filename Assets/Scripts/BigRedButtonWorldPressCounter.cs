using TMPro;
using TheBigRedButtonInstitute.IndirectParticles;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.VR;
using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BigRedButtonWorldPressCounter : MonoBehaviour
    {
        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] TMP_Text displayText;
        [SerializeField] Camera targetCamera;
        [SerializeField] PolarH10RuntimeManager polarRuntimeManager;
        [SerializeField] bool faceCamera = true;
        [SerializeField] bool yawOnly = true;

        [Header("Presentation")]
        [SerializeField] Color baseColor = new(0.82f, 0.22f, 0.22f, 1f);
        [SerializeField] Color pulseColor = new(1f, 0.03f, 0.02f, 1f);
        [SerializeField, Range(1f, 1.4f)] float pulseScale = 1.12f;
        [SerializeField, Min(0.01f)] float countPulseDuration = 0.18f;
        [SerializeField] bool blinkWhilePolarConnected = true;
        [SerializeField, Min(0.1f)] float polarConnectedBlinkHz = 2.4f;

        [Header("Particle Digits")]
        [SerializeField] bool useParticleDigits = true;
        [SerializeField] bool hideTextWhenParticleDigitsReady = true;
        [SerializeField] BrbParticleCoordinateSet[] particleDigitCoordinateSets = new BrbParticleCoordinateSet[10];
        [SerializeField] bool useProceduralParticleDigitFallback = true;
        [SerializeField, Min(32)] int particleFallbackPointsPerDigit = 10000;
        [SerializeField, Min(0.0001f)] float particleDigitSize = 5f;
        [SerializeField, Range(0f, 1f)] float particleDigitRadialClip = 0.9f;
        [SerializeField, Range(0f, 0.5f)] float particleDigitSpacing = 0.12f;
        [SerializeField, Range(0.1f, 1f)] float particleDigitCoordinateFill = 0.94f;
        [SerializeField] bool particleDigitUseTexture = true;
        [SerializeField] Texture2D particleDigitTexture;
        [SerializeField] bool particleDigitTextureUsesLuminanceAlpha;
        [SerializeField] bool particleDigitMorphChanges = true;
        [SerializeField, Min(0f)] float particleDigitMorphDuration = 0.35f;
        [SerializeField] BigRedButtonParticlePressCounterDisplay particleDigits;

        int _lastDisplayedCount = int.MinValue;
        float _lastCountChangeTime = float.NegativeInfinity;
        Vector3 _baseDisplayTextScale = Vector3.one;
        bool _hasBaseDisplayTextScale;

        void Awake()
        {
            ResolveReferences(forceRefresh: false);
            RefreshImmediately();
            UpdateFacing();
            UpdatePresentation();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            RefreshImmediately();
            UpdateFacing();
            UpdatePresentation();
        }

        void OnDisable()
        {
            if (displayText != null)
            {
                displayText.enabled = true;
            }
        }

        void OnValidate()
        {
            pulseScale = Mathf.Clamp(pulseScale, 1f, 1.4f);
            countPulseDuration = Mathf.Max(0.01f, countPulseDuration);
            polarConnectedBlinkHz = Mathf.Max(0.1f, polarConnectedBlinkHz);
            particleFallbackPointsPerDigit = Mathf.Max(32, particleFallbackPointsPerDigit);
            particleDigitSize = Mathf.Max(0.0001f, particleDigitSize);
            particleDigitRadialClip = Mathf.Clamp01(particleDigitRadialClip);
            particleDigitSpacing = Mathf.Clamp(particleDigitSpacing, 0f, 0.5f);
            particleDigitCoordinateFill = Mathf.Clamp(particleDigitCoordinateFill, 0.1f, 1f);
            particleDigitMorphDuration = Mathf.Max(0f, particleDigitMorphDuration);
            RefreshImmediately();
            UpdateFacing();
            UpdatePresentation();
        }

        void LateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ResolveReferences(forceRefresh: false);
            UpdateDisplayText();
            UpdateFacing();
            UpdatePresentation();
        }

        public void Configure(
            QuestVrInputManager manager,
            TMP_Text targetDisplayText,
            Camera camera,
            PolarH10RuntimeManager runtimeManager)
        {
            inputManager = manager;
            displayText = targetDisplayText;
            targetCamera = camera;
            polarRuntimeManager = runtimeManager;
            RefreshImmediately();
            UpdateFacing();
            UpdatePresentation();
        }

        public void RefreshImmediately()
        {
            _lastDisplayedCount = int.MinValue;
            UpdateDisplayText(pulseOnChange: false);
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (inputManager == null || forceRefresh)
            {
                inputManager = FindAnyObjectByType<QuestVrInputManager>();
            }

            if (displayText == null || forceRefresh)
            {
                displayText = GetComponentInChildren<TMP_Text>(true);
                _hasBaseDisplayTextScale = false;
            }

            if (useParticleDigits && (particleDigits == null || forceRefresh))
            {
                particleDigits = GetComponent<BigRedButtonParticlePressCounterDisplay>();
                if (particleDigits == null && Application.isPlaying)
                {
                    particleDigits = gameObject.AddComponent<BigRedButtonParticlePressCounterDisplay>();
                }
            }

            if (particleDigits != null && displayText != null)
            {
                particleDigits.Configure(displayText);
                particleDigits.SetDigitCoordinateSets(particleDigitCoordinateSets);
                particleDigits.SetProceduralFallbackDigits(
                    useProceduralParticleDigitFallback,
                    particleFallbackPointsPerDigit);
                particleDigits.SetDigitLayout(
                    particleDigitSpacing,
                    particleDigitCoordinateFill);
                particleDigits.SetParticleVisuals(
                    particleDigitSize,
                    particleDigitRadialClip);
                particleDigits.SetParticleTexture(
                    particleDigitTexture,
                    particleDigitUseTexture,
                    particleDigitTextureUsesLuminanceAlpha);
                particleDigits.SetDigitMorphOptions(
                    particleDigitMorphChanges,
                    particleDigitMorphDuration);
            }

            if (targetCamera == null || forceRefresh)
            {
                targetCamera = Camera.main ?? FindAnyObjectByType<Camera>();
            }

            if ((polarRuntimeManager == null || forceRefresh) && Application.isPlaying)
            {
                polarRuntimeManager = PolarH10RuntimeManager.EnsureRuntimeExists();
            }

            if ((polarRuntimeManager == null || forceRefresh) && !Application.isPlaying)
            {
                polarRuntimeManager = FindAnyObjectByType<PolarH10RuntimeManager>();
            }
        }

        void UpdateDisplayText(bool pulseOnChange = true)
        {
            if (displayText == null)
            {
                return;
            }

            var count = inputManager != null ? Mathf.Max(0, inputManager.ButtonPressCount) : 0;
            if (_lastDisplayedCount == count)
            {
                return;
            }

            var hadDisplayedCount = _lastDisplayedCount != int.MinValue;
            var countText = count.ToString("N0");
            displayText.text = countText;
            _lastDisplayedCount = count;
            if (particleDigits != null)
            {
                particleDigits.SetNumberText(countText);
            }

            if (pulseOnChange && hadDisplayedCount && Application.isPlaying)
            {
                _lastCountChangeTime = Time.unscaledTime;
            }
        }

        void UpdateFacing()
        {
            if (!faceCamera || targetCamera == null)
            {
                return;
            }

            var directionToCamera = targetCamera.transform.position - transform.position;
            if (yawOnly)
            {
                directionToCamera = Vector3.ProjectOnPlane(directionToCamera, Vector3.up);
            }

            if (directionToCamera.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(-directionToCamera.normalized, Vector3.up);
        }

        void UpdatePresentation()
        {
            if (displayText == null)
            {
                return;
            }

            CaptureBaseDisplayTextScale();

            var pulseAmount = ResolveCountPulseAmount();
            var connectedBlinkAmount = ResolvePolarConnectedBlinkAmount();
            var colorAmount = Mathf.Max(pulseAmount, connectedBlinkAmount);

            var displayColor = Color.Lerp(baseColor, pulseColor, colorAmount);
            var displayScale = Mathf.Lerp(1f, pulseScale, pulseAmount);

            displayText.color = displayColor;
            displayText.transform.localScale = _baseDisplayTextScale * displayScale;

            if (useParticleDigits && particleDigits != null)
            {
                particleDigits.SetPresentation(displayColor, displayScale);
                particleDigits.SetNumberText(displayText.text);
                displayText.enabled = !hideTextWhenParticleDigitsReady || !particleDigits.IsRendering;
            }
            else
            {
                displayText.enabled = true;
            }
        }

        void CaptureBaseDisplayTextScale()
        {
            if (_hasBaseDisplayTextScale || displayText == null)
            {
                return;
            }

            _baseDisplayTextScale = displayText.transform.localScale;
            _hasBaseDisplayTextScale = true;
        }

        float ResolveCountPulseAmount()
        {
            if (!Application.isPlaying || float.IsNegativeInfinity(_lastCountChangeTime))
            {
                return 0f;
            }

            var age = Time.unscaledTime - _lastCountChangeTime;
            if (age >= countPulseDuration)
            {
                return 0f;
            }

            return 1f - Mathf.Clamp01(age / countPulseDuration);
        }

        float ResolvePolarConnectedBlinkAmount()
        {
            if (!Application.isPlaying ||
                !blinkWhilePolarConnected ||
                polarRuntimeManager == null ||
                !polarRuntimeManager.IsPolarConnected)
            {
                return 0f;
            }

            var wave = Mathf.Sin(Time.unscaledTime * polarConnectedBlinkHz * Mathf.PI * 2f);
            return Mathf.InverseLerp(-1f, 1f, wave) * 0.55f;
        }
    }
}

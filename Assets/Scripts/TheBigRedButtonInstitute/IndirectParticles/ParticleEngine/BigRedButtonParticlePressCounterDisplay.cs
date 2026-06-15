using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonParticlePressCounterDisplay : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private TMP_Text sourceText;

        [Header("Digit Coordinate Sources")]
        [SerializeField] private BrbParticleCoordinateSet[] digitCoordinateSets = new BrbParticleCoordinateSet[10];
        [SerializeField] private bool useProceduralFallbackDigits = true;
        [SerializeField, Min(32)] private int fallbackPointsPerDigit = 10000;

        [Header("Digit Layout")]
        [SerializeField, Range(0.1f, 1.2f)] private float digitHeightToFontSize = 0.92f;
        [SerializeField, Range(0.1f, 1.2f)] private float digitHeightToRect = 0.9f;
        [SerializeField, Range(0.1f, 1.2f)] private float digitAspect = 0.62f;
        [SerializeField, Range(0f, 0.5f)] private float digitSpacing = 0.12f;
        [SerializeField, Range(0f, 1f)] private float separatorSpacing = 0.32f;
        [SerializeField, Range(0.1f, 1f)] private float coordinateFill = 0.94f;
        [SerializeField] private float particleZOffset;

        [Header("Coordinate Axes")]
        [SerializeField] private Vector3 sourceRightAxis = Vector3.right;
        [SerializeField] private Vector3 sourceUpAxis = Vector3.up;
        [SerializeField] private Vector3 sourceDepthAxis = Vector3.forward;
        [SerializeField, Min(0f)] private float sourceDepthScale = 1f;

        [Header("Particle Visuals")]
        [SerializeField, Min(0.0001f)] private float particleSize = 5f;
        [SerializeField, Range(0f, 1f)] private float radialClip = 0.9f;
        [SerializeField] private bool useParticleTexture = true;
        [SerializeField] private Texture2D particleTexture;
        [Tooltip("Off uses the texture alpha channel. On treats grayscale luminance as alpha, so black source pixels become transparent.")]
        [SerializeField] private bool particleTextureUsesLuminanceAlpha;

        [Header("Digit Morph")]
        [SerializeField] private bool morphDigitChanges = true;
        [SerializeField, Min(0f)] private float digitMorphDuration = 0.35f;

        private const string RootName = "Particle Digit Counter";

        private readonly List<DigitRenderer> _digitRenderers = new();
        private readonly List<Vector3> _scratchPositions = new();
        private readonly List<Vector3> _scratchNormals = new();

        private Transform _particleRoot;
        private string _lastNumberText;
        private Color _currentTint = Color.white;
        private float _currentScale = 1f;
        private bool _isRendering;

        public bool IsRendering => _isRendering;

        public void Configure(TMP_Text targetSourceText)
        {
            if (sourceText == targetSourceText)
                return;

            sourceText = targetSourceText;
            _lastNumberText = null;
        }

        public void SetDigitCoordinateSets(BrbParticleCoordinateSet[] coordinateSets)
        {
            if (coordinateSets == digitCoordinateSets)
                return;

            digitCoordinateSets = coordinateSets ?? new BrbParticleCoordinateSet[10];
            _lastNumberText = null;
        }

        public void SetProceduralFallbackDigits(bool enabled, int pointsPerDigit)
        {
            int clampedPoints = Mathf.Max(32, pointsPerDigit);
            if (useProceduralFallbackDigits == enabled && fallbackPointsPerDigit == clampedPoints)
                return;

            useProceduralFallbackDigits = enabled;
            fallbackPointsPerDigit = clampedPoints;
            _lastNumberText = null;
        }

        public void SetNumberText(string numberText)
        {
            if (!Application.isPlaying || sourceText == null)
            {
                _isRendering = false;
                return;
            }

            string normalized = string.IsNullOrWhiteSpace(numberText) ? "0" : numberText;
            if (string.Equals(_lastNumberText, normalized, StringComparison.Ordinal))
                return;

            _lastNumberText = normalized;
            RebuildDigits(normalized);
        }

        public void SetPresentation(Color tint, float scale)
        {
            _currentTint = tint;
            _currentScale = Mathf.Max(0.0001f, scale);

            if (_particleRoot != null)
                _particleRoot.localScale = Vector3.one;

            float worldParticleSize = ResolveWorldParticleSize();
            for (int i = 0; i < _digitRenderers.Count; i++)
            {
                _digitRenderers[i].ParticleSystem.SetTint(_currentTint);
                _digitRenderers[i].ParticleSystem.SetParticleSize(worldParticleSize);
            }
        }

        public void SetParticleVisuals(float localParticleSize, float targetRadialClip)
        {
            float nextParticleSize = Mathf.Max(0.0001f, localParticleSize);
            float nextRadialClip = Mathf.Clamp01(targetRadialClip);
            bool changed = !Mathf.Approximately(particleSize, nextParticleSize) ||
                           !Mathf.Approximately(radialClip, nextRadialClip);
            particleSize = nextParticleSize;
            radialClip = nextRadialClip;
            if (!changed && _digitRenderers.Count == 0)
                return;

            float worldParticleSize = ResolveWorldParticleSize();

            for (int i = 0; i < _digitRenderers.Count; i++)
            {
                _digitRenderers[i].ParticleSystem.SetParticleSize(worldParticleSize);
                _digitRenderers[i].ParticleSystem.SetRadialClip(radialClip);
            }
        }

        public void SetDigitLayout(float targetDigitSpacing, float targetCoordinateFill)
        {
            float nextDigitSpacing = Mathf.Clamp(targetDigitSpacing, 0f, 0.5f);
            float nextCoordinateFill = Mathf.Clamp(targetCoordinateFill, 0.1f, 1f);
            if (Mathf.Approximately(digitSpacing, nextDigitSpacing) &&
                Mathf.Approximately(coordinateFill, nextCoordinateFill))
            {
                return;
            }

            digitSpacing = nextDigitSpacing;
            coordinateFill = nextCoordinateFill;
            string currentText = _lastNumberText;
            _lastNumberText = null;
            if (!string.IsNullOrEmpty(currentText))
                SetNumberText(currentText);
        }

        public void SetParticleTexture(
            Texture2D texture,
            bool enabled,
            bool useLuminanceAlpha)
        {
            bool changed = particleTexture != texture ||
                           useParticleTexture != enabled ||
                           particleTextureUsesLuminanceAlpha != useLuminanceAlpha;
            particleTexture = texture;
            useParticleTexture = enabled;
            particleTextureUsesLuminanceAlpha = useLuminanceAlpha;
            if (!changed && _digitRenderers.Count == 0)
                return;

            for (int i = 0; i < _digitRenderers.Count; i++)
            {
                _digitRenderers[i].ParticleSystem.SetParticleTexture(
                    particleTexture,
                    useParticleTexture,
                    particleTextureUsesLuminanceAlpha);
            }
        }

        public void SetDigitMorphOptions(bool enabled, float duration)
        {
            morphDigitChanges = enabled;
            digitMorphDuration = Mathf.Max(0f, duration);
        }

        private void OnDisable()
        {
            _isRendering = false;
        }

        private void OnValidate()
        {
            fallbackPointsPerDigit = Mathf.Max(32, fallbackPointsPerDigit);
            digitHeightToFontSize = Mathf.Clamp(digitHeightToFontSize, 0.1f, 1.2f);
            digitHeightToRect = Mathf.Clamp(digitHeightToRect, 0.1f, 1.2f);
            digitAspect = Mathf.Clamp(digitAspect, 0.1f, 1.2f);
            digitSpacing = Mathf.Clamp01(digitSpacing);
            separatorSpacing = Mathf.Clamp01(separatorSpacing);
            coordinateFill = Mathf.Clamp01(coordinateFill);
            particleSize = Mathf.Max(0.0001f, particleSize);
            radialClip = Mathf.Clamp01(radialClip);
            digitMorphDuration = Mathf.Max(0f, digitMorphDuration);
            sourceDepthScale = Mathf.Max(0f, sourceDepthScale);
            _lastNumberText = null;
        }

        private void RebuildDigits(string numberText)
        {
            EnsureParticleRoot();

            float digitHeight = ResolveDigitHeight();
            float digitWidth = digitHeight * digitAspect;
            float digitGap = digitWidth * digitSpacing;
            float totalWidth = MeasureTextWidth(numberText, digitWidth, digitGap);
            float cursor = -totalWidth * 0.5f;
            int digitCount = CountDigits(numberText);
            int visualDigitIndex = 0;

            for (int i = 0; i < numberText.Length; i++)
            {
                char character = numberText[i];
                if (!char.IsDigit(character))
                {
                    cursor += digitWidth * separatorSpacing;
                    continue;
                }

                int digit = character - '0';
                if (!BuildDigitCoordinates(digit, digitWidth, digitHeight, _scratchPositions, _scratchNormals))
                    continue;

                float centerX = cursor + digitWidth * 0.5f;
                OffsetCoordinates(_scratchPositions, centerX, particleZOffset);
                int rendererIndex = digitCount - 1 - visualDigitIndex;
                DigitRenderer renderer = EnsureDigitRenderer(rendererIndex);
                renderer.Transform.localPosition = Vector3.zero;
                renderer.Transform.localRotation = Quaternion.identity;
                renderer.Transform.localScale = Vector3.one;
                renderer.ParticleSystem.SetMaxParticleCount(_scratchPositions.Count);
                renderer.ParticleSystem.SetColorFromNormal(false);
                renderer.ParticleSystem.SetMotionOptions(animateFrame: false, spin: false);
                renderer.ParticleSystem.SetParticleSize(ResolveWorldParticleSize());
                renderer.ParticleSystem.SetRadialClip(radialClip);
                renderer.ParticleSystem.SetParticleTexture(
                    particleTexture,
                    useParticleTexture,
                    particleTextureUsesLuminanceAlpha);
                renderer.ParticleSystem.SetTint(_currentTint);
                renderer.ParticleSystem.SetCoordinatePoints(
                    _scratchPositions,
                    _scratchNormals,
                    ResolveDigitMorphDuration());
                renderer.GameObject.SetActive(true);

                visualDigitIndex++;
                cursor += digitWidth + digitGap;
            }

            for (int i = digitCount; i < _digitRenderers.Count; i++)
                _digitRenderers[i].GameObject.SetActive(false);

            _isRendering = digitCount > 0;
            SetPresentation(_currentTint, _currentScale);
        }

        private void EnsureParticleRoot()
        {
            if (_particleRoot != null)
                return;

            Transform parent = sourceText.transform;
            Transform existing = parent.Find(RootName);
            if (existing != null)
            {
                _particleRoot = existing;
            }
            else
            {
                var rootObject = new GameObject(RootName);
                _particleRoot = rootObject.transform;
                _particleRoot.SetParent(parent, false);
            }

            _particleRoot.localPosition = Vector3.zero;
            _particleRoot.localRotation = Quaternion.identity;
            _particleRoot.localScale = Vector3.one;
        }

        private DigitRenderer EnsureDigitRenderer(int index)
        {
            while (_digitRenderers.Count <= index)
            {
                var digitObject = new GameObject($"Particle Digit {_digitRenderers.Count}");
                digitObject.SetActive(false);
                Transform digitTransform = digitObject.transform;
                digitTransform.SetParent(_particleRoot, false);
                var particleSystem = digitObject.AddComponent<BrbIndirectCoordinateParticleSystem>();
                var renderer = new DigitRenderer(digitObject, digitTransform, particleSystem);
                _digitRenderers.Add(renderer);
            }

            return _digitRenderers[index];
        }

        private float ResolveDigitHeight()
        {
            float fontHeight = sourceText != null ? Mathf.Max(1f, sourceText.fontSize) : 210f;
            float height = fontHeight * digitHeightToFontSize;
            RectTransform rectTransform = sourceText != null ? sourceText.rectTransform : null;
            if (rectTransform != null && rectTransform.rect.height > 1f)
                height = Mathf.Min(height, rectTransform.rect.height * digitHeightToRect);
            return Mathf.Max(1f, height);
        }

        private float ResolveDigitMorphDuration()
        {
            return morphDigitChanges ? digitMorphDuration : 0f;
        }

        private float ResolveWorldParticleSize()
        {
            Transform targetTransform = sourceText != null ? sourceText.transform : transform;
            Vector3 scale = targetTransform.lossyScale;
            float localToWorld = Mathf.Max(
                0.000001f,
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));
            return particleSize * localToWorld;
        }

        private float MeasureTextWidth(string text, float digitWidth, float digitGap)
        {
            float width = 0f;
            int digitCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    if (digitCount > 0)
                        width += digitGap;
                    width += digitWidth;
                    digitCount++;
                }
                else
                {
                    width += digitWidth * separatorSpacing;
                }
            }

            return Mathf.Max(digitWidth, width);
        }

        private static int CountDigits(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                    count++;
            }

            return count;
        }

        private static void OffsetCoordinates(List<Vector3> positions, float centerX, float zOffset)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 position = positions[i];
                positions[i] = new Vector3(position.x + centerX, position.y, position.z + zOffset);
            }
        }

        private bool BuildDigitCoordinates(
            int digit,
            float digitWidth,
            float digitHeight,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            positions.Clear();
            normals.Clear();

            if (TryBuildAssetDigitCoordinates(digit, digitWidth, digitHeight, positions, normals))
                return true;

            if (!useProceduralFallbackDigits)
                return false;

            BuildProceduralDigitCoordinates(digit, digitWidth, digitHeight, positions, normals);
            return positions.Count > 0;
        }

        private bool TryBuildAssetDigitCoordinates(
            int digit,
            float digitWidth,
            float digitHeight,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            if (digitCoordinateSets == null ||
                digit < 0 ||
                digit >= digitCoordinateSets.Length ||
                digitCoordinateSets[digit] == null ||
                digitCoordinateSets[digit].Count == 0)
            {
                return false;
            }

            BrbParticleCoordinateSet coordinateSet = digitCoordinateSets[digit];
            Vector3 rightAxis = SafeAxis(sourceRightAxis, Vector3.right);
            Vector3 upAxis = SafeAxis(sourceUpAxis, Vector3.up);
            Vector3 depthAxis = SafeAxis(sourceDepthAxis, Vector3.forward);

            var projected = new List<Vector2>(coordinateSet.Count);
            var depths = new List<float>(coordinateSet.Count);
            var sourceNormals = new List<Vector3>(coordinateSet.Count);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            float minDepth = float.PositiveInfinity;
            float maxDepth = float.NegativeInfinity;

            for (int i = 0; i < coordinateSet.Count; i++)
            {
                if (!coordinateSet.TryGetPoint(i, out Vector3 position, out Vector3 normal, out _))
                    continue;

                if (!IsFinite(position))
                    continue;

                Vector2 point = new Vector2(Vector3.Dot(position, rightAxis), Vector3.Dot(position, upAxis));
                float depth = Vector3.Dot(position, depthAxis);
                projected.Add(point);
                depths.Add(depth);
                sourceNormals.Add(ProjectNormal(normal, rightAxis, upAxis, depthAxis));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
                minDepth = Mathf.Min(minDepth, depth);
                maxDepth = Mathf.Max(maxDepth, depth);
            }

            if (projected.Count == 0)
                return false;

            Vector2 size = max - min;
            float sourceWidth = Mathf.Max(0.000001f, size.x);
            float sourceHeight = Mathf.Max(0.000001f, size.y);
            float scale = Mathf.Min(digitWidth * coordinateFill / sourceWidth, digitHeight * coordinateFill / sourceHeight);
            Vector2 center = (min + max) * 0.5f;
            float depthCenter = (minDepth + maxDepth) * 0.5f;

            for (int i = 0; i < projected.Count; i++)
            {
                Vector2 point = (projected[i] - center) * scale;
                float depth = (depths[i] - depthCenter) * scale * sourceDepthScale;
                positions.Add(new Vector3(point.x, point.y, depth));
                normals.Add(sourceNormals[i]);
            }

            return positions.Count > 0;
        }

        private void BuildProceduralDigitCoordinates(
            int digit,
            float digitWidth,
            float digitHeight,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            Segment2D[] segments = ResolveDigitSegments(digit, digitWidth, digitHeight);
            if (segments.Length == 0)
                return;

            int targetCount = Mathf.Max(32, fallbackPointsPerDigit);
            int baseCount = targetCount / segments.Length;
            int remainder = targetCount % segments.Length;
            float thickness = Mathf.Min(digitWidth, digitHeight) * 0.035f;

            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                int count = baseCount + (segmentIndex < remainder ? 1 : 0);
                AppendSegmentPoints(digit, segmentIndex, segments[segmentIndex], count, thickness, positions, normals);
            }
        }

        private static void AppendSegmentPoints(
            int digit,
            int segmentIndex,
            Segment2D segment,
            int count,
            float thickness,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            Vector2 direction = segment.End - segment.Start;
            Vector2 tangent = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.right;
            Vector2 normal2D = new Vector2(-tangent.y, tangent.x);

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / Mathf.Max(1, count);
                float crossJitter = (Hash01(digit, segmentIndex, i) - 0.5f) * thickness;
                float alongJitter = (Hash01(segmentIndex, i, digit) - 0.5f) * thickness * 0.35f;
                Vector2 point = Vector2.Lerp(segment.Start, segment.End, t) +
                                normal2D * crossJitter +
                                tangent * alongJitter;
                positions.Add(new Vector3(point.x, point.y, 0f));
                normals.Add(Vector3.forward);
            }
        }

        private static Segment2D[] ResolveDigitSegments(int digit, float width, float height)
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float x = halfWidth * 0.82f;
            float y = halfHeight * 0.82f;

            Segment2D top = new(new Vector2(-x, y), new Vector2(x, y));
            Segment2D upperRight = new(new Vector2(x, y), new Vector2(x, 0f));
            Segment2D lowerRight = new(new Vector2(x, 0f), new Vector2(x, -y));
            Segment2D bottom = new(new Vector2(-x, -y), new Vector2(x, -y));
            Segment2D lowerLeft = new(new Vector2(-x, 0f), new Vector2(-x, -y));
            Segment2D upperLeft = new(new Vector2(-x, y), new Vector2(-x, 0f));
            Segment2D middle = new(new Vector2(-x, 0f), new Vector2(x, 0f));

            switch (Mathf.Clamp(digit, 0, 9))
            {
                case 0:
                    return new[] { top, upperRight, lowerRight, bottom, lowerLeft, upperLeft };
                case 1:
                    return new[] { upperRight, lowerRight };
                case 2:
                    return new[] { top, upperRight, middle, lowerLeft, bottom };
                case 3:
                    return new[] { top, upperRight, middle, lowerRight, bottom };
                case 4:
                    return new[] { upperLeft, middle, upperRight, lowerRight };
                case 5:
                    return new[] { top, upperLeft, middle, lowerRight, bottom };
                case 6:
                    return new[] { top, upperLeft, middle, lowerLeft, bottom, lowerRight };
                case 7:
                    return new[] { top, upperRight, lowerRight };
                case 8:
                    return new[] { top, upperRight, lowerRight, bottom, lowerLeft, upperLeft, middle };
                default:
                    return new[] { top, upperRight, lowerRight, bottom, upperLeft, middle };
            }
        }

        private static Vector3 ProjectNormal(Vector3 normal, Vector3 rightAxis, Vector3 upAxis, Vector3 depthAxis)
        {
            if (!IsFinite(normal) || normal.sqrMagnitude <= 0.000001f)
                return Vector3.forward;

            Vector3 projected = new Vector3(
                Vector3.Dot(normal, rightAxis),
                Vector3.Dot(normal, upAxis),
                Vector3.Dot(normal, depthAxis));
            return projected.sqrMagnitude > 0.000001f ? projected.normalized : Vector3.forward;
        }

        private static Vector3 SafeAxis(Vector3 axis, Vector3 fallback)
        {
            return IsFinite(axis) && axis.sqrMagnitude > 0.000001f ? axis.normalized : fallback;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Hash01(int a, int b, int c)
        {
            uint value = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return (value & 0x00FFFFFFu) / 16777216f;
        }

        private readonly struct DigitRenderer
        {
            public DigitRenderer(
                GameObject gameObject,
                Transform transform,
                BrbIndirectCoordinateParticleSystem particleSystem)
            {
                GameObject = gameObject;
                Transform = transform;
                ParticleSystem = particleSystem;
            }

            public GameObject GameObject { get; }
            public Transform Transform { get; }
            public BrbIndirectCoordinateParticleSystem ParticleSystem { get; }
        }

        private readonly struct Segment2D
        {
            public Segment2D(Vector2 start, Vector2 end)
            {
                Start = start;
                End = end;
            }

            public Vector2 Start { get; }
            public Vector2 End { get; }
        }
    }
}

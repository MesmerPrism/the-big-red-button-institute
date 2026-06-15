using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute.IndirectParticles
{
    /// <summary>
    /// Lightweight BRB port of the Astral indirect-particle draw path.
    /// It renders static coordinate points without Kuramoto phase coupling.
    /// </summary>
    public sealed class BrbIndirectCoordinateParticleSystem : MonoBehaviour
    {
        private const int MaxSafetyParticleCount = 200000;
        private const string DefaultShaderName =
            "TheBigRedButtonInstitute/Indirect Particles/URP Coordinate Billboard";
        private const string DefaultParticleTextureResourcePath =
            "Textures/IndirectParticles/BRB_DiffuseFeatherDot";

        private static readonly int ParticlesBufferId = Shader.PropertyToID("_Particles");
        private static readonly int IndexRemapBufferId = Shader.PropertyToID("_IndexRemap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int RadialClipId = Shader.PropertyToID("_RadialClip");
        private static readonly int ParticleTexId = Shader.PropertyToID("_ParticleTex");
        private static readonly int UseParticleTextureId = Shader.PropertyToID("_UseParticleTexture");
        private static readonly int ParticleTextureAlphaModeId = Shader.PropertyToID("_ParticleTextureAlphaMode");
        private static Texture2D s_defaultParticleTexture;

        [Header("Coordinate Source")]
        [SerializeField] private BrbParticleCoordinateSet coordinateSet;
        [Tooltip("Matter Mesh Lab coordinate-map package JSON, a coordinate-map JSON, or a simple points/coordinates JSON cloud.")]
        [SerializeField] private TextAsset coordinateJson;
        [SerializeField] private bool useProceduralFallback = true;
        [SerializeField, Min(1)] private int proceduralFallbackCount = 1024;
        [SerializeField, Min(1)] private int maxParticleCount = 10000;

        [Header("Coordinate Placement")]
        [SerializeField] private Vector3 coordinateScale = Vector3.one;
        [SerializeField] private Vector3 coordinateOffset = Vector3.zero;
        [SerializeField, Min(0f)] private float normalOffset;

        [Header("Visuals")]
        [SerializeField] private Material particleMaterial;
        [SerializeField, Min(0.0001f)] private float particleSize = 0.015f;
        [SerializeField] private Color tint = Color.white;
        [SerializeField] private bool colorFromNormal = true;
        [SerializeField] private Gradient colorGradient = CreateDefaultGradient();
        [SerializeField, Range(0f, 1f)] private float radialClip = 1f;
        [SerializeField] private bool useParticleTexture = true;
        [SerializeField] private Texture2D particleTexture;
        [Tooltip("Off uses the texture alpha channel. On treats grayscale luminance as alpha, so black source pixels become transparent.")]
        [SerializeField] private bool particleTextureUsesLuminanceAlpha;
        [SerializeField] private bool animateFramePhase = true;
        [SerializeField, Min(0f)] private float framePhaseRate = 0.2f;
        [SerializeField] private bool spinParticles = true;
        [SerializeField] private float spinRadiansPerSecond = 0.35f;

        [Header("Bounds")]
        [SerializeField] private bool useAutomaticBounds = true;
        [SerializeField, Min(0.01f)] private float boundsPadding = 0.5f;
        [SerializeField] private Vector3 manualWorldBoundsSize = new Vector3(8f, 8f, 8f);

        private Vector3[] _runtimeOverridePositions;
        private Vector3[] _runtimeOverrideNormals;
        private Vector3[] _localPositions = Array.Empty<Vector3>();
        private Vector3[] _localNormals = Array.Empty<Vector3>();
        private Color[] _pointColors = Array.Empty<Color>();
        private ParticleGpu[] _particles = Array.Empty<ParticleGpu>();
        private uint[] _identityRemap = Array.Empty<uint>();
        private GraphicsBuffer _particleBuffer;
        private GraphicsBuffer _indexRemapBuffer;
        private GraphicsBuffer _argsBuffer;
        private MaterialPropertyBlock _materialProperties;
        private Mesh _quadMesh;
        private Material _runtimeMaterial;
        private bool _ownsRuntimeMaterial;
        private bool _initialized;
        private bool _needsRebuild = true;
        private Bounds _worldBounds;
        private Vector3[] _transitionStartPositions = Array.Empty<Vector3>();
        private Vector3[] _transitionStartNormals = Array.Empty<Vector3>();
        private Vector3[] _transitionTargetPositions = Array.Empty<Vector3>();
        private Vector3[] _transitionTargetNormals = Array.Empty<Vector3>();
        private float _transitionStartTime;
        private float _transitionDuration;
        private bool _coordinateTransitionActive;

        [StructLayout(LayoutKind.Sequential)]
        private struct ParticleGpu
        {
            public Vector3 positionWS;
            public float size;
            public Vector4 color;
            public float rotation;
            public float frame;
            public float aux0;
            public float aux1;
        }

        public int ParticleCount => _particles != null ? _particles.Length : 0;

        public void SetMaxParticleCount(int count)
        {
            int clamped = Mathf.Clamp(count, 1, MaxSafetyParticleCount);
            if (maxParticleCount == clamped)
                return;

            maxParticleCount = clamped;
            _needsRebuild = true;
            if (isActiveAndEnabled)
                RebuildNow();
        }

        public void SetParticleSize(float size)
        {
            float clamped = Mathf.Max(0.0001f, size);
            if (Mathf.Approximately(particleSize, clamped))
                return;

            particleSize = clamped;
            if (_initialized)
            {
                WriteParticleData(Time.time);
                UploadParticleData();
            }
        }

        public void SetTint(Color color)
        {
            tint = color;
            ApplyMaterialProperties();
        }

        public void SetRadialClip(float value)
        {
            radialClip = Mathf.Clamp01(value);
            ApplyMaterialProperties();
        }

        public void SetParticleTexture(Texture2D texture, bool enabled, bool useLuminanceAlpha)
        {
            bool changed = particleTexture != texture ||
                           useParticleTexture != enabled ||
                           particleTextureUsesLuminanceAlpha != useLuminanceAlpha;
            particleTexture = texture;
            useParticleTexture = enabled;
            particleTextureUsesLuminanceAlpha = useLuminanceAlpha;
            if (changed)
                ApplyMaterialProperties();
        }

        public void SetColorFromNormal(bool enabled)
        {
            if (colorFromNormal == enabled)
                return;

            colorFromNormal = enabled;
            _needsRebuild = true;
            if (isActiveAndEnabled)
                RebuildNow();
        }

        public void SetMotionOptions(bool animateFrame, bool spin)
        {
            bool changed = animateFramePhase != animateFrame || spinParticles != spin;
            animateFramePhase = animateFrame;
            spinParticles = spin;
            if (changed && _initialized)
            {
                WriteParticleData(Time.time);
                UploadParticleData();
            }
        }

        public void SetCoordinatePoints(
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> normals = null,
            float transitionDuration = 0f)
        {
            if (positions == null || positions.Count == 0)
            {
                ClearRuntimeCoordinateOverride();
                return;
            }

            int count = Mathf.Min(positions.Count, ResolveParticleLimit());
            var targetPositions = new Vector3[count];
            var targetNormals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                targetPositions[i] = positions[i];
                targetNormals[i] = normals != null && i < normals.Count
                    ? SafeNormal(normals[i])
                    : Vector3.forward;
            }

            if (ShouldTransitionCoordinates(transitionDuration))
            {
                BeginCoordinateTransition(targetPositions, targetNormals, transitionDuration);
                return;
            }

            SetRuntimeCoordinateOverrideImmediate(targetPositions, targetNormals);
        }

        private void SetRuntimeCoordinateOverrideImmediate(Vector3[] positions, Vector3[] normals)
        {
            _coordinateTransitionActive = false;
            _transitionStartPositions = Array.Empty<Vector3>();
            _transitionStartNormals = Array.Empty<Vector3>();
            _transitionTargetPositions = Array.Empty<Vector3>();
            _transitionTargetNormals = Array.Empty<Vector3>();
            _runtimeOverridePositions = positions;
            _runtimeOverrideNormals = normals;
            _needsRebuild = true;
            if (isActiveAndEnabled)
                RebuildNow();
        }

        public void ClearRuntimeCoordinateOverride()
        {
            _coordinateTransitionActive = false;
            _runtimeOverridePositions = null;
            _runtimeOverrideNormals = null;
            _needsRebuild = true;
            if (isActiveAndEnabled)
                RebuildNow();
        }

        public void RebuildNow()
        {
            ReleaseRuntimeResources();
            if (!BuildCoordinateSource())
            {
                _initialized = false;
                return;
            }

            CreateRuntimeResources(_localPositions.Length);
            WriteParticleData(Time.time);
            UploadParticleData();
            transform.hasChanged = false;
            _initialized = true;
            _needsRebuild = false;
        }

        private void OnEnable()
        {
            RebuildNow();
        }

        private void OnDisable()
        {
            ReleaseRuntimeResources();
            _initialized = false;
        }

        private void OnDestroy()
        {
            ReleaseRuntimeResources();
        }

        private void OnValidate()
        {
            maxParticleCount = Mathf.Clamp(maxParticleCount, 1, MaxSafetyParticleCount);
            proceduralFallbackCount = Mathf.Clamp(proceduralFallbackCount, 1, MaxSafetyParticleCount);
            particleSize = Mathf.Max(0.0001f, particleSize);
            boundsPadding = Mathf.Max(0.01f, boundsPadding);
            normalOffset = Mathf.Max(0f, normalOffset);
            radialClip = Mathf.Clamp01(radialClip);
            _needsRebuild = true;
        }

        private void Update()
        {
            if (_needsRebuild)
            {
                RebuildNow();
                return;
            }

            if (!_initialized)
                return;

            bool needsUpload = transform.hasChanged || animateFramePhase || spinParticles;
            needsUpload |= UpdateCoordinateTransition(Time.unscaledTime);
            if (!needsUpload)
                return;

            WriteParticleData(Time.time);
            UploadParticleData();
            transform.hasChanged = false;
        }

        private void LateUpdate()
        {
            if (!_initialized || _particles.Length == 0 || _argsBuffer == null)
                return;

            Material material = ResolveMaterial();
            if (material == null)
                return;

            ApplyMaterialProperties();

            RenderParams renderParams = new RenderParams(material)
            {
                worldBounds = _worldBounds,
                matProps = _materialProperties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer
            };
            Graphics.RenderMeshIndirect(renderParams, _quadMesh, _argsBuffer, 1);
        }

        private bool BuildCoordinateSource()
        {
            if (_runtimeOverridePositions != null && _runtimeOverridePositions.Length > 0)
                return CopyRuntimeOverrideCoordinates();

            if (coordinateSet != null && coordinateSet.Count > 0)
                return CopyAssetCoordinates();

            if (coordinateJson != null && TryReadCoordinateJson(coordinateJson.text))
                return true;

            if (useProceduralFallback)
            {
                BuildProceduralFallbackCoordinates();
                return _localPositions.Length > 0;
            }

            _localPositions = Array.Empty<Vector3>();
            _localNormals = Array.Empty<Vector3>();
            _pointColors = Array.Empty<Color>();
            return false;
        }

        private bool CopyRuntimeOverrideCoordinates()
        {
            int count = Mathf.Min(_runtimeOverridePositions.Length, ResolveParticleLimit());
            _localPositions = new Vector3[count];
            _localNormals = new Vector3[count];
            _pointColors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                _localPositions[i] = _runtimeOverridePositions[i];
                _localNormals[i] = _runtimeOverrideNormals != null && i < _runtimeOverrideNormals.Length
                    ? SafeNormal(_runtimeOverrideNormals[i])
                    : Vector3.forward;
                _pointColors[i] = ResolvePointColor(i, count, _localNormals[i], Color.white);
            }

            return count > 0;
        }

        private bool CopyAssetCoordinates()
        {
            int count = Mathf.Min(coordinateSet.Count, ResolveParticleLimit());
            _localPositions = new Vector3[count];
            _localNormals = new Vector3[count];
            _pointColors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                coordinateSet.TryGetPoint(i, out Vector3 position, out Vector3 normal, out Color color);
                _localPositions[i] = position;
                _localNormals[i] = SafeNormal(normal);
                _pointColors[i] = ResolvePointColor(i, count, _localNormals[i], color);
            }

            return count > 0;
        }

        private bool TryReadCoordinateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                CoordinateJsonRoot root = JsonUtility.FromJson<CoordinateJsonRoot>(json);
                if (root == null)
                    return false;

                CoordinateJsonPoint[] points = null;
                if (root.coordinate_map?.samples?.samples != null && root.coordinate_map.samples.samples.Length > 0)
                    points = root.coordinate_map.samples.samples;
                else if (root.samples?.samples != null && root.samples.samples.Length > 0)
                    points = root.samples.samples;
                else if (root.points != null && root.points.Length > 0)
                    points = root.points;
                else if (root.coordinates != null && root.coordinates.Length > 0)
                    points = root.coordinates;

                if (points == null || points.Length == 0)
                    return false;

                int count = Mathf.Min(points.Length, ResolveParticleLimit());
                var positions = new List<Vector3>(count);
                var normals = new List<Vector3>(count);
                var colors = new List<Color>(count);
                for (int i = 0; i < points.Length && positions.Count < count; i++)
                {
                    CoordinateJsonPoint point = points[i];
                    Vector3 position = point.ResolvePosition();
                    if (!IsFinite(position))
                        continue;

                    Vector3 normal = point.ResolveNormal();
                    positions.Add(position);
                    normals.Add(SafeNormal(normal));
                    colors.Add(Color.white);
                }

                if (positions.Count == 0)
                    return false;

                _localPositions = positions.ToArray();
                _localNormals = normals.ToArray();
                _pointColors = new Color[_localPositions.Length];
                for (int i = 0; i < _pointColors.Length; i++)
                    _pointColors[i] = ResolvePointColor(i, _pointColors.Length, _localNormals[i], colors[i]);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BRB Particles] Failed to parse coordinate JSON: {exception.Message}", this);
                return false;
            }
        }

        private bool ShouldTransitionCoordinates(float transitionDuration)
        {
            return Application.isPlaying &&
                   transitionDuration > 0.0001f &&
                   _initialized &&
                   !_needsRebuild &&
                   _localPositions != null &&
                   _localPositions.Length > 0 &&
                   _particleBuffer != null &&
                   _argsBuffer != null;
        }

        private void BeginCoordinateTransition(
            Vector3[] targetPositions,
            Vector3[] targetNormals,
            float duration)
        {
            int count = targetPositions.Length;
            _runtimeOverridePositions = targetPositions;
            _runtimeOverrideNormals = targetNormals;
            _transitionStartPositions = BuildTransitionStartPositions(count);
            _transitionStartNormals = BuildTransitionStartNormals(count);
            _transitionTargetPositions = targetPositions;
            _transitionTargetNormals = targetNormals;
            _transitionStartTime = Time.unscaledTime;
            _transitionDuration = Mathf.Max(0.0001f, duration);
            _coordinateTransitionActive = true;
            _needsRebuild = false;

            bool resourceCountChanged = _particles == null || _particles.Length != count;
            if (resourceCountChanged)
            {
                ReleaseRuntimeResources();
                CreateRuntimeResources(count);
                _initialized = true;
            }

            _localPositions = new Vector3[count];
            _localNormals = new Vector3[count];
            _pointColors = BuildPointColors(count, _transitionTargetNormals);
            Array.Copy(_transitionStartPositions, _localPositions, count);
            Array.Copy(_transitionStartNormals, _localNormals, count);
            WriteParticleData(Time.time);
            UploadParticleData();
        }

        private Vector3[] BuildTransitionStartPositions(int count)
        {
            var start = new Vector3[count];
            if (_localPositions == null || _localPositions.Length == 0)
                return start;

            for (int i = 0; i < count; i++)
                start[i] = _localPositions[i % _localPositions.Length];
            return start;
        }

        private Vector3[] BuildTransitionStartNormals(int count)
        {
            var start = new Vector3[count];
            if (_localNormals == null || _localNormals.Length == 0)
            {
                for (int i = 0; i < count; i++)
                    start[i] = Vector3.forward;
                return start;
            }

            for (int i = 0; i < count; i++)
                start[i] = SafeNormal(_localNormals[i % _localNormals.Length]);
            return start;
        }

        private bool UpdateCoordinateTransition(float timeSeconds)
        {
            if (!_coordinateTransitionActive ||
                _transitionTargetPositions == null ||
                _transitionTargetPositions.Length == 0)
            {
                return false;
            }

            float rawT = Mathf.Clamp01((timeSeconds - _transitionStartTime) / _transitionDuration);
            float t = rawT * rawT * (3f - 2f * rawT);
            int count = _transitionTargetPositions.Length;

            if (_localPositions == null || _localPositions.Length != count)
                _localPositions = new Vector3[count];
            if (_localNormals == null || _localNormals.Length != count)
                _localNormals = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                _localPositions[i] = Vector3.LerpUnclamped(
                    _transitionStartPositions[i],
                    _transitionTargetPositions[i],
                    t);
                _localNormals[i] = LerpNormal(
                    _transitionStartNormals[i],
                    _transitionTargetNormals[i],
                    t);
            }

            if (rawT >= 1f)
            {
                _coordinateTransitionActive = false;
                _localPositions = _transitionTargetPositions;
                _localNormals = _transitionTargetNormals;
                _transitionStartPositions = Array.Empty<Vector3>();
                _transitionStartNormals = Array.Empty<Vector3>();
                _transitionTargetPositions = Array.Empty<Vector3>();
                _transitionTargetNormals = Array.Empty<Vector3>();
            }

            return true;
        }

        private void BuildProceduralFallbackCoordinates()
        {
            int count = Mathf.Min(proceduralFallbackCount, ResolveParticleLimit());
            _localPositions = new Vector3[count];
            _localNormals = new Vector3[count];
            _pointColors = new Color[count];

            const float radiusX = 0.85f;
            const float radiusY = 1.15f;
            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0f : i / (float)count;
                float angle = t * Mathf.PI * 2f;
                float ribbon = Mathf.Sin(t * Mathf.PI * 24f) * 0.03f;
                Vector3 position = new Vector3(
                    Mathf.Cos(angle) * (radiusX + ribbon),
                    Mathf.Sin(angle) * (radiusY + ribbon),
                    Mathf.Sin(angle * 3f) * 0.06f);
                Vector3 normal = SafeNormal(new Vector3(position.x / radiusX, position.y / radiusY, 0.25f));

                _localPositions[i] = position;
                _localNormals[i] = normal;
                _pointColors[i] = ResolvePointColor(i, count, normal, Color.white);
            }
        }

        private void CreateRuntimeResources(int particleCount)
        {
            if (particleCount <= 0)
                return;

            _quadMesh = CreateUnitQuad();
            _materialProperties = new MaterialPropertyBlock();
            int stride = Marshal.SizeOf<ParticleGpu>();
            _particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, stride);
            _indexRemapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(uint));

            _particles = new ParticleGpu[particleCount];
            _identityRemap = new uint[particleCount];
            for (int i = 0; i < particleCount; i++)
                _identityRemap[i] = (uint)i;
            _indexRemapBuffer.SetData(_identityRemap);

            var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            args[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = _quadMesh.GetIndexCount(0),
                instanceCount = (uint)particleCount,
                startIndex = _quadMesh.GetIndexStart(0),
                baseVertexIndex = _quadMesh.GetBaseVertex(0),
                startInstance = 0
            };

            _argsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            _argsBuffer.SetData(args);

            _materialProperties.SetBuffer(ParticlesBufferId, _particleBuffer);
            _materialProperties.SetBuffer(IndexRemapBufferId, _indexRemapBuffer);
            ApplyMaterialProperties();
        }

        private void WriteParticleData(float timeSeconds)
        {
            if (_localPositions == null || _particles == null)
                return;

            float frame = animateFramePhase ? Mathf.Repeat(timeSeconds * framePhaseRate, 1f) : 0f;
            _worldBounds = useAutomaticBounds
                ? BuildAutomaticWorldBounds()
                : new Bounds(transform.position, manualWorldBoundsSize);

            for (int i = 0; i < _particles.Length; i++)
            {
                Vector3 local = Vector3.Scale(_localPositions[i], coordinateScale) +
                                coordinateOffset +
                                _localNormals[i] * normalOffset;
                Vector3 world = transform.TransformPoint(local);
                float rotation = spinParticles ? timeSeconds * spinRadiansPerSecond + i * 0.6180339f : 0f;
                Color color = _pointColors != null && i < _pointColors.Length ? _pointColors[i] : Color.white;

                _particles[i] = new ParticleGpu
                {
                    positionWS = world,
                    size = particleSize,
                    color = new Vector4(color.r, color.g, color.b, color.a),
                    rotation = rotation,
                    frame = frame,
                    aux0 = 1f,
                    aux1 = 0f
                };
            }
        }

        private void UploadParticleData()
        {
            if (_particleBuffer != null && _particles != null && _particles.Length > 0)
                _particleBuffer.SetData(_particles);
        }

        private Bounds BuildAutomaticWorldBounds()
        {
            if (_localPositions == null || _localPositions.Length == 0)
                return new Bounds(transform.position, Vector3.one);

            Vector3 first = TransformLocalCoordinate(_localPositions[0], _localNormals[0]);
            Bounds bounds = new Bounds(first, Vector3.one * Mathf.Max(particleSize, 0.001f));
            for (int i = 1; i < _localPositions.Length; i++)
                bounds.Encapsulate(TransformLocalCoordinate(_localPositions[i], _localNormals[i]));
            bounds.Expand(boundsPadding + particleSize * 2f);
            return bounds;
        }

        private Vector3 TransformLocalCoordinate(Vector3 position, Vector3 normal)
        {
            Vector3 local = Vector3.Scale(position, coordinateScale) + coordinateOffset + normal * normalOffset;
            return transform.TransformPoint(local);
        }

        private Material ResolveMaterial()
        {
            if (particleMaterial != null)
                return particleMaterial;

            if (_runtimeMaterial != null)
                return _runtimeMaterial;

            Shader shader = Shader.Find(DefaultShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BRB Particles] Shader '{DefaultShaderName}' was not found.", this);
                return null;
            }

            _runtimeMaterial = new Material(shader)
            {
                name = "BRB Runtime Coordinate Particle Material",
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
            };
            _ownsRuntimeMaterial = true;
            return _runtimeMaterial;
        }

        private void ApplyMaterialProperties()
        {
            if (_materialProperties == null)
                return;

            Texture2D resolvedTexture = ResolveParticleTexture();
            bool shouldUseTexture = useParticleTexture && resolvedTexture != null;

            _materialProperties.SetColor(TintId, tint);
            _materialProperties.SetFloat(RadialClipId, radialClip);
            _materialProperties.SetFloat(UseParticleTextureId, shouldUseTexture ? 1f : 0f);
            _materialProperties.SetFloat(
                ParticleTextureAlphaModeId,
                particleTextureUsesLuminanceAlpha ? 1f : 0f);

            if (resolvedTexture != null)
                _materialProperties.SetTexture(ParticleTexId, resolvedTexture);
        }

        private Texture2D ResolveParticleTexture()
        {
            if (particleTexture != null)
                return particleTexture;

            if (!useParticleTexture)
                return null;

            if (s_defaultParticleTexture == null)
                s_defaultParticleTexture = Resources.Load<Texture2D>(DefaultParticleTextureResourcePath);

            return s_defaultParticleTexture;
        }

        private int ResolveParticleLimit()
        {
            return Mathf.Clamp(maxParticleCount, 1, MaxSafetyParticleCount);
        }

        private Color ResolvePointColor(int index, int count, Vector3 normal, Color authoredColor)
        {
            if (!colorFromNormal)
                return authoredColor;

            float t = Mathf.Clamp01(normal.y * 0.5f + 0.5f);
            Gradient gradient = colorGradient ?? CreateDefaultGradient();
            Color gradientColor = gradient.Evaluate(t);
            float sequence = count <= 1 ? 0f : index / (float)(count - 1);
            return Color.Lerp(gradientColor, authoredColor, 0.15f + sequence * 0.1f);
        }

        private Color[] BuildPointColors(int count, IReadOnlyList<Vector3> normals)
        {
            var colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 normal = normals != null && i < normals.Count
                    ? SafeNormal(normals[i])
                    : Vector3.forward;
                colors[i] = ResolvePointColor(i, count, normal, Color.white);
            }

            return colors;
        }

        private static Vector3 SafeNormal(Vector3 value)
        {
            return IsFinite(value) && value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.forward;
        }

        private static Vector3 LerpNormal(Vector3 start, Vector3 target, float t)
        {
            Vector3 normal = Vector3.Lerp(start, target, t);
            return SafeNormal(normal);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ReleaseRuntimeResources()
        {
            ReleaseBuffer(ref _particleBuffer);
            ReleaseBuffer(ref _indexRemapBuffer);
            ReleaseBuffer(ref _argsBuffer);
            _particles = Array.Empty<ParticleGpu>();
            _identityRemap = Array.Empty<uint>();

            if (_quadMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_quadMesh);
                else
                    DestroyImmediate(_quadMesh);
                _quadMesh = null;
            }

            if (_ownsRuntimeMaterial && _runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeMaterial);
                else
                    DestroyImmediate(_runtimeMaterial);
            }
            _runtimeMaterial = null;
            _ownsRuntimeMaterial = false;
        }

        private static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
            buffer = null;
        }

        private static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "BRB Coordinate Particle Quad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0);
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.1f, 0.7f, 1f), 0f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.35f), 0.55f),
                    new GradientColorKey(new Color(1f, 0.25f, 0.12f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return gradient;
        }

        [Serializable]
        private sealed class CoordinateJsonRoot
        {
            public CoordinateMapJson coordinate_map;
            public SampleSetJson samples;
            public CoordinateJsonPoint[] points;
            public CoordinateJsonPoint[] coordinates;
        }

        [Serializable]
        private sealed class CoordinateMapJson
        {
            public SampleSetJson samples;
        }

        [Serializable]
        private sealed class SampleSetJson
        {
            public CoordinateJsonPoint[] samples;
        }

        [Serializable]
        private sealed class CoordinateJsonPoint
        {
            public JsonVec3 position;
            public JsonVec3 normal;
            public float x;
            public float y;
            public float z;
            public float nx;
            public float ny;
            public float nz;

            public Vector3 ResolvePosition()
            {
                return position != null ? position.ToVector3() : new Vector3(x, y, z);
            }

            public Vector3 ResolveNormal()
            {
                return normal != null ? normal.ToVector3() : new Vector3(nx, ny, nz);
            }
        }

        [Serializable]
        private sealed class JsonVec3
        {
            public float x;
            public float y;
            public float z;

            public Vector3 ToVector3()
            {
                return new Vector3(x, y, z);
            }
        }
    }
}

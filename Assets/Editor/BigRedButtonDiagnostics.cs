using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheBigRedButtonInstitute.Editor
{
    public static class BigRedButtonDiagnostics
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ButtonName = "Big Red Button";
        const string OutputPath = "Builds/Android/big-red-button-diagnostics.txt";
        const string AnimationSampleOutputPath = "Builds/Android/big-red-button-animation-samples.txt";

        [MenuItem("Tools/Big Red Button/Dump Button Diagnostics")]
        public static void DumpFromMenu()
        {
            DumpSampleSceneButtonState();
        }

        [MenuItem("Tools/Big Red Button/Dump Animation Samples")]
        public static void DumpAnimationSamplesFromMenu()
        {
            DumpSampleSceneAnimationSamples();
        }

        public static void DumpSampleSceneButtonState()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var button = scene.GetRootGameObjects().FirstOrDefault(root => root.name == ButtonName);
            if (button == null)
            {
                throw new FileNotFoundException($"Could not find root object '{ButtonName}' in scene '{ScenePath}'.");
            }

            var builder = new StringBuilder(8192);
            builder.AppendLine($"Scene: {ScenePath}");
            builder.AppendLine($"Button Root: {BuildTransformPath(button.transform)}");
            builder.AppendLine();

            var animationTester = button.GetComponent<BigRedButtonAnimationTester>();
            var blinkController = button.GetComponent<BigRedButtonBlinkController>();
            var manualPressController = button.GetComponent<BigRedButtonManualPressController>();
            var legacyAnimation = button.GetComponentInChildren<Animation>(true);

            builder.AppendLine("[Animation Tester]");
            if (animationTester == null)
            {
                builder.AppendLine("missing");
            }
            else
            {
                AppendObjectHeader(builder, animationTester);
                AppendSerializedValue(builder, animationTester, "playOnStart");
                AppendSerializedValue(builder, animationTester, "loop");
                AppendSerializedValue(builder, animationTester, "startDelay");
                AppendSerializedValue(builder, animationTester, "pauseBetweenLoops");
                AppendSerializedObjectReference(builder, animationTester, "legacyAnimation");
                AppendSerializedObjectReference(builder, animationTester, "animator");
                AppendSerializedObjectReference(builder, animationTester, "pressedClip");
            }

            builder.AppendLine();
            builder.AppendLine("[Legacy Animation]");
            if (legacyAnimation == null)
            {
                builder.AppendLine("missing");
            }
            else
            {
                AppendObjectHeader(builder, legacyAnimation);
                builder.AppendLine($"playAutomatically: {legacyAnimation.playAutomatically}");
                builder.AppendLine($"wrapMode: {legacyAnimation.wrapMode}");
                builder.AppendLine($"clip: {legacyAnimation.clip?.name ?? "<none>"}");
            }

            builder.AppendLine();
            builder.AppendLine("[Blink Controller]");
            if (blinkController == null)
            {
                builder.AppendLine("missing");
            }
            else
            {
                AppendObjectHeader(builder, blinkController);
                AppendRenderer(builder, "targetRenderer", blinkController.TargetRenderer);
                AppendSerializedObjectReference(builder, blinkController, "blinkAnchor");
                AppendSerializedValue(builder, blinkController, "targetChildName");
            }

            builder.AppendLine();
            builder.AppendLine("[Manual Press Controller]");
            if (manualPressController == null)
            {
                builder.AppendLine("missing");
            }
            else
            {
                AppendObjectHeader(builder, manualPressController);
                AppendRenderer(builder, "targetRenderer", manualPressController.TargetRenderer);
                AppendRenderer(builder, "pressTriggerRenderer", manualPressController.PressTriggerRenderer);
            }

            builder.AppendLine();
            builder.AppendLine("[Hierarchy]");
            AppendHierarchy(builder, button.transform, 0);

            var outputFullPath = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath) ?? ".");
            File.WriteAllText(outputFullPath, builder.ToString());
            Debug.Log($"Wrote button diagnostics to {outputFullPath}");
        }

        public static void DumpSampleSceneAnimationSamples()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var button = scene.GetRootGameObjects().FirstOrDefault(root => root.name == ButtonName);
            if (button == null)
            {
                throw new FileNotFoundException($"Could not find root object '{ButtonName}' in scene '{ScenePath}'.");
            }

            var animationTester = button.GetComponent<BigRedButtonAnimationTester>();
            var manualPressController = button.GetComponent<BigRedButtonManualPressController>();
            var pressedClip = GetSerializedObjectReference<AnimationClip>(animationTester, "pressedClip");
            var triggerRenderer = manualPressController?.PressTriggerRenderer as SkinnedMeshRenderer ??
                button.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(renderer => renderer.name == "button");
            var passiveRenderer = manualPressController?.TargetRenderer;
            if (pressedClip == null || triggerRenderer == null || passiveRenderer == null)
            {
                throw new FileNotFoundException("Could not resolve the pressed clip, trigger skinned mesh renderer, or passive renderer for animation sampling.");
            }

            var outputFullPath = Path.GetFullPath(AnimationSampleOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath) ?? ".");

            var builder = new StringBuilder(8192);
            builder.AppendLine($"Scene: {ScenePath}");
            builder.AppendLine($"Button Root: {BuildTransformPath(button.transform)}");
            builder.AppendLine($"Clip: {pressedClip.name}");
            builder.AppendLine($"Clip Length: {pressedClip.length:0.###}s");
            builder.AppendLine($"Trigger Renderer: {BuildTransformPath(triggerRenderer.transform)}");
            builder.AppendLine($"Passive Renderer: {BuildTransformPath(passiveRenderer.transform)}");
            builder.AppendLine();

            var bakedMesh = new Mesh
            {
                name = "BigRedButton Diagnostics Baked Mesh"
            };

            try
            {
                var clipLength = Mathf.Max(pressedClip.length, 0.0001f);
                var sampleTimes = new[]
                {
                    0f,
                    clipLength * 0.25f,
                    clipLength * 0.5f,
                    clipLength * 0.75f,
                    clipLength
                };

                for (var sampleIndex = 0; sampleIndex < sampleTimes.Length; sampleIndex++)
                {
                    var sampleTime = Mathf.Clamp(sampleTimes[sampleIndex], 0f, clipLength);
                    pressedClip.SampleAnimation(button, sampleTime);
                    triggerRenderer.BakeMesh(bakedMesh, false);

                    builder.AppendLine($"[Sample {sampleIndex}] t={sampleTime:0.###}s normalized={(sampleTime / clipLength):0.###}");
                    builder.AppendLine($"renderer.position: {FormatVector(triggerRenderer.transform.position)}");
                    builder.AppendLine($"renderer.rotationEuler: {FormatVector(triggerRenderer.transform.rotation.eulerAngles)}");
                    builder.AppendLine($"renderer.up: {FormatVector(triggerRenderer.transform.up)}");
                    if (triggerRenderer.rootBone != null)
                    {
                        builder.AppendLine($"rootBone.position: {FormatVector(triggerRenderer.rootBone.position)}");
                        builder.AppendLine($"rootBone.rotationEuler: {FormatVector(triggerRenderer.rootBone.rotation.eulerAngles)}");
                        builder.AppendLine($"rootBone.up: {FormatVector(triggerRenderer.rootBone.up)}");
                    }
                    else
                    {
                        builder.AppendLine("rootBone: <null>");
                    }

                    builder.AppendLine($"renderer.bounds.center: {FormatVector(triggerRenderer.bounds.center)}");
                    builder.AppendLine($"renderer.bounds.size: {FormatVector(triggerRenderer.bounds.size)}");
                    builder.AppendLine($"bakedMesh.bounds.center: {FormatVector(bakedMesh.bounds.center)}");
                    builder.AppendLine($"bakedMesh.bounds.size: {FormatVector(bakedMesh.bounds.size)}");

                    var expectedNormal = (triggerRenderer.bounds.center - passiveRenderer.bounds.center).normalized;
                    builder.AppendLine($"expectedPressNormal: {FormatVector(expectedNormal)}");
                    if (TryComputeTopSurface(bakedMesh, triggerRenderer.localToWorldMatrix, expectedNormal, out var topNormal, out var topCentroid))
                    {
                        builder.AppendLine($"topSurface.normal: {FormatVector(topNormal)}");
                        builder.AppendLine($"topSurface.centroid: {FormatVector(topCentroid)}");
                        builder.AppendLine($"angle(topSurface, renderer.up): {Vector3.Angle(topNormal, triggerRenderer.transform.up):0.###}");
                        if (triggerRenderer.rootBone != null)
                        {
                            builder.AppendLine($"angle(topSurface, rootBone.up): {Vector3.Angle(topNormal, triggerRenderer.rootBone.up):0.###}");
                        }
                    }
                    else
                    {
                        builder.AppendLine("topSurface: <not found>");
                    }

                    builder.AppendLine();
                }
            }
            finally
            {
                if (bakedMesh != null)
                {
                    Object.DestroyImmediate(bakedMesh);
                }

                pressedClip.SampleAnimation(button, 0f);
            }

            File.WriteAllText(outputFullPath, builder.ToString());
            Debug.Log($"Wrote button animation samples to {outputFullPath}");
        }

        static void AppendHierarchy(StringBuilder builder, Transform transform, int depth)
        {
            if (transform == null)
            {
                return;
            }

            var indent = new string(' ', depth * 2);
            builder.AppendLine($"{indent}- {transform.name} activeSelf={transform.gameObject.activeSelf} activeInHierarchy={transform.gameObject.activeInHierarchy}");

            var components = transform.GetComponents<Component>();
            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var component = components[componentIndex];
                if (component == null || component is Transform)
                {
                    continue;
                }

                builder.AppendLine($"{indent}  * {DescribeComponent(component)}");
            }

            for (var childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                AppendHierarchy(builder, transform.GetChild(childIndex), depth + 1);
            }
        }

        static string DescribeComponent(Component component)
        {
            if (component == null)
            {
                return "<missing>";
            }

            switch (component)
            {
                case MeshRenderer meshRenderer:
                    return $"{nameof(MeshRenderer)} enabled={meshRenderer.enabled} mesh={meshRenderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "<none>"}";
                case SkinnedMeshRenderer skinnedMeshRenderer:
                    return $"{nameof(SkinnedMeshRenderer)} enabled={skinnedMeshRenderer.enabled} sharedMesh={skinnedMeshRenderer.sharedMesh?.name ?? "<none>"}";
                case MeshFilter meshFilter:
                    return $"{nameof(MeshFilter)} sharedMesh={meshFilter.sharedMesh?.name ?? "<none>"}";
                case MeshCollider meshCollider:
                    return $"{nameof(MeshCollider)} enabled={meshCollider.enabled} convex={meshCollider.convex} sharedMesh={meshCollider.sharedMesh?.name ?? "<none>"}";
                case Animation animation:
                    return $"{nameof(Animation)} playAutomatically={animation.playAutomatically} wrapMode={animation.wrapMode} clip={animation.clip?.name ?? "<none>"}";
                case BigRedButtonGeneratedBodyCollider generatedBodyCollider:
                    return $"{nameof(BigRedButtonGeneratedBodyCollider)} sourceRenderer={DescribeObject(generatedBodyCollider.SourceRenderer)} convex={generatedBodyCollider.IsConvex} snapshotMesh={generatedBodyCollider.SerializedSnapshotMesh?.name ?? "<none>"}";
                case BigRedButtonColliderDebugVisual debugVisual:
                    return $"{nameof(BigRedButtonColliderDebugVisual)} role={GetSerializedEnumName(debugVisual, "role")}";
                default:
                    return component.GetType().Name;
            }
        }

        static void AppendObjectHeader(StringBuilder builder, Object target)
        {
            builder.AppendLine(DescribeObject(target));
        }

        static void AppendSerializedValue(StringBuilder builder, Object target, string propertyName)
        {
            if (target == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                builder.AppendLine($"{propertyName}: <missing property>");
                return;
            }

            builder.AppendLine($"{propertyName}: {SerializedPropertyToString(property)}");
        }

        static void AppendSerializedObjectReference(StringBuilder builder, Object target, string propertyName)
        {
            if (target == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                builder.AppendLine($"{propertyName}: <missing property>");
                return;
            }

            builder.AppendLine($"{propertyName}: {DescribeObject(property.objectReferenceValue)}");
        }

        static void AppendRenderer(StringBuilder builder, string label, Renderer renderer)
        {
            builder.AppendLine($"{label}: {DescribeObject(renderer)}");
            if (renderer == null)
            {
                return;
            }

            builder.AppendLine($"  path: {BuildTransformPath(renderer.transform)}");
            builder.AppendLine($"  rendererType: {renderer.GetType().Name}");
            builder.AppendLine($"  enabled: {renderer.enabled}");
            builder.AppendLine($"  gameObjectActive: {renderer.gameObject.activeInHierarchy}");
            builder.AppendLine($"  material: {renderer.sharedMaterial?.name ?? "<none>"}");

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                builder.AppendLine($"  sharedMesh: {skinnedMeshRenderer.sharedMesh?.name ?? "<none>"}");
            }
            else
            {
                builder.AppendLine($"  sharedMesh: {renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "<none>"}");
            }
        }

        static string DescribeObject(Object target)
        {
            if (target == null)
            {
                return "<null>";
            }

            return target switch
            {
                Component component => $"{component.GetType().Name} '{BuildTransformPath(component.transform)}'",
                GameObject gameObject => $"GameObject '{BuildTransformPath(gameObject.transform)}'",
                _ => $"{target.GetType().Name} '{target.name}'"
            };
        }

        static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        static string GetSerializedEnumName(Object target, string propertyName)
        {
            if (target == null)
            {
                return "<null>";
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            return property == null ? "<missing property>" : property.enumDisplayNames[property.enumValueIndex];
        }

        static string SerializedPropertyToString(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("0.####"),
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Enum => property.enumDisplayNames[property.enumValueIndex],
                SerializedPropertyType.ObjectReference => DescribeObject(property.objectReferenceValue),
                _ => property.ToString()
            };
        }

        static T GetSerializedObjectReference<T>(Object target, string propertyName)
            where T : Object
        {
            if (target == null)
            {
                return null;
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            return property?.objectReferenceValue as T;
        }

        static bool TryComputeTopSurface(Mesh mesh, Matrix4x4 localToWorld, Vector3 expectedNormal, out Vector3 topNormal, out Vector3 topCentroid)
        {
            topNormal = default;
            topCentroid = default;
            if (mesh == null || mesh.vertexCount == 0 || mesh.triangles == null || mesh.triangles.Length < 3)
            {
                return false;
            }

            const float AlignmentThreshold = 0.75f;
            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            var maxProjectedHeight = float.NegativeInfinity;

            for (var triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                if (!TryGetWorldTriangle(vertices, indices, triangleIndex, localToWorld, out _, out _, out _, out var normal, out _, out var centroid))
                {
                    continue;
                }

                if (Vector3.Dot(normal, expectedNormal) <= AlignmentThreshold)
                {
                    continue;
                }

                maxProjectedHeight = Mathf.Max(maxProjectedHeight, Vector3.Dot(centroid, expectedNormal));
            }

            if (!float.IsFinite(maxProjectedHeight))
            {
                return false;
            }

            var topBandThickness = Mathf.Max(mesh.bounds.size.magnitude * 0.01f, 0.0025f);
            var weightedNormal = Vector3.zero;
            var weightedCentroid = Vector3.zero;
            var totalWeight = 0f;

            for (var triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                if (!TryGetWorldTriangle(vertices, indices, triangleIndex, localToWorld, out _, out _, out _, out var normal, out var area, out var centroid))
                {
                    continue;
                }

                var alignment = Vector3.Dot(normal, expectedNormal);
                if (alignment <= AlignmentThreshold)
                {
                    continue;
                }

                var projectedHeight = Vector3.Dot(centroid, expectedNormal);
                if (projectedHeight < maxProjectedHeight - topBandThickness)
                {
                    continue;
                }

                var weight = area * alignment;
                weightedNormal += normal * weight;
                weightedCentroid += centroid * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0.000001f)
            {
                return false;
            }

            topNormal = weightedNormal.normalized;
            if (Vector3.Dot(topNormal, expectedNormal) < 0f)
            {
                topNormal = -topNormal;
            }

            topCentroid = weightedCentroid / totalWeight;
            return true;
        }

        static bool TryGetWorldTriangle(
            Vector3[] vertices,
            int[] indices,
            int triangleIndex,
            Matrix4x4 localToWorld,
            out Vector3 a,
            out Vector3 b,
            out Vector3 c,
            out Vector3 normal,
            out float area,
            out Vector3 centroid)
        {
            a = default;
            b = default;
            c = default;
            normal = default;
            area = 0f;
            centroid = default;
            if (vertices == null || indices == null || triangleIndex < 0 || triangleIndex + 2 >= indices.Length)
            {
                return false;
            }

            a = localToWorld.MultiplyPoint3x4(vertices[indices[triangleIndex]]);
            b = localToWorld.MultiplyPoint3x4(vertices[indices[triangleIndex + 1]]);
            c = localToWorld.MultiplyPoint3x4(vertices[indices[triangleIndex + 2]]);
            var cross = Vector3.Cross(b - a, c - a);
            var magnitude = cross.magnitude;
            if (magnitude <= 0.000001f)
            {
                return false;
            }

            normal = cross / magnitude;
            area = magnitude * 0.5f;
            centroid = (a + b + c) / 3f;
            return true;
        }

        static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
        }
    }
}

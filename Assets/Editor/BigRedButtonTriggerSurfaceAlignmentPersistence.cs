using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    [InitializeOnLoad]
    public static class BigRedButtonTriggerSurfaceAlignmentPersistence
    {
        [Serializable]
        sealed class AlignmentSnapshot
        {
            public Vector3 localPosition;
            public Vector3 localEuler;
            public Vector3 boxSize;
            public string savedAtUtc;
        }

        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ButtonName = "Big Red Button";
        const string SnapshotRelativePath = "Temp/big-red-button-trigger-surface-alignment.json";
        const string MenuPath = "Tools/Big Red Button/Apply Latest Trigger Surface Alignment";

        static BigRedButtonTriggerSurfaceAlignmentPersistence()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem(MenuPath)]
        public static void ApplyLatestAlignmentFromMenu()
        {
            TryApplyPendingPose(allowOpenSampleScene: true, logWhenMissing: true);
        }

        static void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            TryApplyPendingPose(allowOpenSampleScene: false, logWhenMissing: false);
        }

        static void TryApplyPendingPose(bool allowOpenSampleScene, bool logWhenMissing)
        {
            var snapshotPath = GetSnapshotPath();
            if (!File.Exists(snapshotPath))
            {
                if (logWhenMissing)
                {
                    Debug.Log($"No pending trigger surface alignment snapshot found at {snapshotPath}.");
                }

                return;
            }

            AlignmentSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<AlignmentSnapshot>(File.ReadAllText(snapshotPath));
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to read trigger surface alignment snapshot: {exception}");
                return;
            }

            if (snapshot == null)
            {
                Debug.LogError($"Trigger surface alignment snapshot at {snapshotPath} was empty.");
                return;
            }

            var scene = ResolveTargetScene(allowOpenSampleScene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var button = FindButton(scene);
            if (button == null)
            {
                Debug.LogError($"Could not find '{ButtonName}' while applying the saved trigger surface alignment.");
                return;
            }

            var controller = button.GetComponent<BigRedButtonManualPressController>();
            if (controller == null)
            {
                Debug.LogError("Could not find BigRedButtonManualPressController while applying the saved trigger surface alignment.");
                return;
            }

            controller.SetTriggerSurfacePoseOverride(snapshot.localPosition, snapshot.localEuler);
            controller.SetTriggerSurfaceSizeOverride(snapshot.boxSize);
            controller.RebuildConfiguredPressGeometryInEditor();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.Delete(snapshotPath);

            Debug.Log(
                $"Applied trigger surface alignment pose from {snapshot.savedAtUtc ?? "unknown time"} " +
                $"to {ScenePath}: localPosition={snapshot.localPosition}, localEuler={snapshot.localEuler}.");
        }

        static Scene ResolveTargetScene(bool allowOpenSampleScene)
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded && activeScene.path == ScenePath)
            {
                return activeScene;
            }

            if (!allowOpenSampleScene)
            {
                return default;
            }

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static GameObject FindButton(Scene scene)
        {
            var rootObjects = scene.GetRootGameObjects();
            for (var i = 0; i < rootObjects.Length; i++)
            {
                if (rootObjects[i] != null && rootObjects[i].name == ButtonName)
                {
                    return rootObjects[i];
                }
            }

            return null;
        }

        static string GetSnapshotPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", SnapshotRelativePath));
        }
    }
}

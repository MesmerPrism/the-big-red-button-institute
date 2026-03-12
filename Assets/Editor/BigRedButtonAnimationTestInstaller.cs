using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    public static class BigRedButtonAnimationTestInstaller
    {
        const string ModelPath = "Assets/Models/BigRedButton.glb";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ButtonName = "Big Red Button";
        const string ClipName = "pressed";

        static bool _configureQueued;

        [MenuItem("Tools/Big Red Button/Configure Animation Test")]
        public static void ConfigureFromMenu()
        {
            QueueConfigure(force: true);
        }

        static void QueueConfigure(bool force = false)
        {
            if (_configureQueued && !force)
            {
                return;
            }

            _configureQueued = true;
            EditorApplication.delayCall += TryConfigure;
        }

        static void TryConfigure()
        {
            _configureQueued = false;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var clip = FindPressedClip();
            if (clip == null)
            {
                EditorApplication.delayCall += TryConfigure;
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var button = FindButton(scene);
            if (button == null)
            {
                EditorApplication.delayCall += TryConfigure;
                return;
            }

            var legacyAnimation = button.GetComponentInChildren<Animation>(true);
            if (legacyAnimation == null)
            {
                legacyAnimation = button.GetComponent<Animation>();
            }

            var animator = button.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = button.GetComponent<Animator>();
            }

            var tester = button.GetComponent<BigRedButtonAnimationTester>();
            if (tester == null)
            {
                tester = button.AddComponent<BigRedButtonAnimationTester>();
            }

            if (legacyAnimation != null)
            {
                tester.Configure(legacyAnimation, clip);
            }
            else
            {
                if (animator == null)
                {
                    animator = button.AddComponent<Animator>();
                }

                tester.Configure(animator, clip);
            }
            tester.ConfigurePlayback(false, false, 0f, 0f, KeyCode.Space);
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(tester);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Configured animation test for {ButtonName} using clip '{clip.name}'.");
        }

        static AnimationClip FindPressedClip()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            var clip = assets.OfType<AnimationClip>().FirstOrDefault(asset => asset.name == ClipName);
            return clip ?? assets.OfType<AnimationClip>().FirstOrDefault();
        }

        static GameObject FindButton(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == ButtonName)
                {
                    return rootObject;
                }
            }

            return null;
        }
    }
}

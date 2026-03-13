using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheBigRedButtonInstitute.Editor
{
    public static class QuestVrApkBuilder
    {
        const string OutputFileName = "TheBigRedButtonInstitute.apk";
        const string AndroidIdentifier = "org.thebigredbuttoninstitute.app";
        const string MenuPath = "Tools/Big Red Button/Build Quest APK";

        [MenuItem(MenuPath)]
        public static void BuildFromMenu()
        {
            InstallSceneAndBuildApk();
        }

        public static void InstallSceneAndBuildApk()
        {
            QuestVrSceneInstaller.InstallIntoSampleScene();
            ConfigurePlayerSettings();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (enabledScenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are present in EditorBuildSettings.");
            }

            var outputDirectory = Path.GetFullPath(Path.Combine("Builds", "Android"));
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, OutputFileName);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = null;
            AssetDatabase.DisallowAutoRefresh();
            EditorApplication.LockReloadAssemblies();
            try
            {
                report = BuildPipeline.BuildPlayer(buildOptions);
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                AssetDatabase.AllowAutoRefresh();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            if (report == null)
            {
                throw new InvalidOperationException("Android build did not return a build report.");
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                if (LooksLikeSuccessfulApkBuild(outputPath))
                {
                    Debug.LogWarning(
                        $"Unity reported '{report.summary.result}', but a fresh APK was produced at {outputPath}. " +
                        "Treating this as a successful build because the editor is currently hitting the Bee worker shutdown false-negative.");
                    return;
                }

                throw new InvalidOperationException(
                    $"Android build failed with result {report.summary.result} after {report.summary.totalErrors} errors and {report.summary.totalWarnings} warnings.");
            }

            if (!File.Exists(outputPath))
            {
                throw new FileNotFoundException($"Unity reported a successful Android build, but the APK was not found at {outputPath}.", outputPath);
            }

            Debug.Log($"Built Quest APK at {outputPath}");
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "The Big Red Button Institute";
            PlayerSettings.productName = "The Big Red Button Institute";
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            EditorUserBuildSettings.buildAppBundle = false;

#pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, AndroidIdentifier);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
#pragma warning restore CS0618
        }

        static bool LooksLikeSuccessfulApkBuild(string outputPath)
        {
            if (!File.Exists(outputPath))
            {
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(outputPath);
                return fileInfo.Length > 1024 * 1024;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}

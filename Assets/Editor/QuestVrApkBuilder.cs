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

            var buildOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android build failed with result {report.summary.result} after {report.summary.totalErrors} errors and {report.summary.totalWarnings} warnings.");
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
    }
}

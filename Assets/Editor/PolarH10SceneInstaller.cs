using TheBigRedButtonInstitute.Biofeedback;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE;
using TheBigRedButtonInstitute.Biofeedback.Transport.BLE.Polar;
using TheBigRedButtonInstitute.Biofeedback.Transport.Bluetooth;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Breathing;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Coherence;
using TheBigRedButtonInstitute.IndirectParticles.Biofeedback.Heartbeat;

namespace TheBigRedButtonInstitute.Editor
{
    public static class PolarH10SceneInstaller
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string RuntimeRootName = "VR Runtime";
        const string ConnectionHubName = "Biofeedback Connection Hub";
        const string PolarRuntimeName = "Polar H10 Breathing Source";

        [MenuItem("Tools/Big Red Button/Install Polar H10 Runtime")]
        public static void InstallFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject runtimeRoot = null;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == RuntimeRootName)
                {
                    runtimeRoot = rootObject;
                    break;
                }
            }

            if (runtimeRoot == null)
            {
                runtimeRoot = new GameObject(RuntimeRootName);
                SceneManager.MoveGameObjectToScene(runtimeRoot, scene);
            }

            if (runtimeRoot.GetComponent<PolarH10RuntimeManager>() == null)
            {
                runtimeRoot.AddComponent<PolarH10RuntimeManager>();
            }

            if (runtimeRoot.GetComponent<PolarHeartbeatButtonDriver>() == null)
            {
                runtimeRoot.AddComponent<PolarHeartbeatButtonDriver>();
            }

            InstallIntoScene(scene, runtimeRoot, null);
            EditorUtility.SetDirty(runtimeRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Installed Polar H10 runtime manager into SampleScene.");
        }

        public static PolarH10RuntimeManager InstallIntoScene(Scene scene, GameObject runtimeRoot, Transform headTransform)
        {
            var runtimeManager = runtimeRoot.GetComponent<PolarH10RuntimeManager>() ?? runtimeRoot.AddComponent<PolarH10RuntimeManager>();

            var connectionHub = EnsureChild(runtimeRoot.transform, ConnectionHubName);
            connectionHub.SetActive(true);
            var bluetoothPermissions = connectionHub.GetComponent<BluetoothPermissionsBootstrap>() ?? connectionHub.AddComponent<BluetoothPermissionsBootstrap>();
            var bleAdapter = connectionHub.GetComponent<BleAdapter>() ?? connectionHub.AddComponent<BleAdapter>();
            var bleCentral = connectionHub.GetComponent<BleCentral>() ?? connectionHub.AddComponent<BleCentral>();

            var polarRuntime = EnsureChild(runtimeRoot.transform, PolarRuntimeName);
            polarRuntime.SetActive(true);
            var polarUnifiedModule = polarRuntime.GetComponent<PolarUnifiedModule>() ?? polarRuntime.AddComponent<PolarUnifiedModule>();
            var polarPmdAdapter = polarRuntime.GetComponent<PolarPmdAdapter>() ?? polarRuntime.AddComponent<PolarPmdAdapter>();
            var heartRateRouter = polarRuntime.GetComponent<PolarHeartRateTransportRouter>() ?? polarRuntime.AddComponent<PolarHeartRateTransportRouter>();
            var accTransportRouter = polarRuntime.GetComponent<PolarAccTransportRouter>() ?? polarRuntime.AddComponent<PolarAccTransportRouter>();
            var accBreathingTracker = polarRuntime.GetComponent<PolarAccBreathingTracker>() ?? polarRuntime.AddComponent<PolarAccBreathingTracker>();
            var breathingModule = polarRuntime.GetComponent<PEPolarH10BreathingModule>() ?? polarRuntime.AddComponent<PEPolarH10BreathingModule>();
            var heartbeatModule = polarRuntime.GetComponent<PEPolarHeartbeatModule>() ?? polarRuntime.AddComponent<PEPolarHeartbeatModule>();
            var coherenceModule = polarRuntime.GetComponent<PEHeartbeatCoherenceModule>() ?? polarRuntime.AddComponent<PEHeartbeatCoherenceModule>();

            runtimeManager.ConfigureRuntimeGraphReferences(
                headTransform,
                bluetoothPermissions,
                bleAdapter,
                bleCentral,
                polarPmdAdapter,
                polarUnifiedModule,
                heartRateRouter,
                accTransportRouter,
                accBreathingTracker,
                breathingModule,
                heartbeatModule,
                coherenceModule);

            EditorUtility.SetDirty(connectionHub);
            EditorUtility.SetDirty(polarRuntime);
            EditorUtility.SetDirty(runtimeManager);
            return runtimeManager;
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                return child.gameObject;
            }

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created;
        }
    }
}

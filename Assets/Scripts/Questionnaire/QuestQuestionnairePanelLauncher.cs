using System;
using UnityEngine;

namespace TheBigRedButtonInstitute.Questionnaire
{
    [DisallowMultipleComponent]
    public sealed class QuestQuestionnairePanelLauncher : MonoBehaviour
    {
        const string BridgeClassName = "org.thebigredbuttoninstitute.questionnaire.QuestionnairePanelBridge";
        const int LaunchExtraOpenBit = 1;
        const int LaunchExtraDebugAutoSubmitBit = 2;
        const float LaunchExtraDelaySeconds = 1.0f;
        const float LaunchExtraPollSeconds = 0.5f;

        [SerializeField] bool allowQuestionnaireOpenLaunchExtra = true;
        [SerializeField] bool refreshStatusOnResume = true;
        [SerializeField] string latestStatus = "No request launched yet.";

        bool _launchExtraPending;
        bool _launchExtraDebugAutoSubmit;
        float _launchExtraDueTime;
        double _nextLaunchExtraPollTime;

        public string LatestStatus => latestStatus;

        void Start()
        {
            RefreshStatus();
            TryQueueLaunchExtra();
        }

        void Update()
        {
            TryQueueLaunchExtra();
            ProcessQueuedLaunchExtra();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && refreshStatusOnResume)
            {
                RefreshStatus();
            }

            if (hasFocus)
            {
                TryQueueLaunchExtra();
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus && refreshStatusOnResume)
            {
                RefreshStatus();
            }
        }

        public string LaunchDemographics()
        {
            return LaunchDemographics(debugAutoSubmit: false);
        }

        public string LaunchDemographics(bool debugAutoSubmit)
        {
            return LaunchDemographicsFromTrigger("direct", debugAutoSubmit);
        }

        public string LaunchDemographicsFromTrigger(string triggerName, bool debugAutoSubmit)
        {
            latestStatus = CallBridge("launchDemographics", debugAutoSubmit);
            Debug.Log($"[QuestQuestionnairePanelLauncher] trigger={triggerName} debugAutoSubmit={debugAutoSubmit} {latestStatus}", this);
            return latestStatus;
        }

        public string RefreshStatus()
        {
            var previousStatus = latestStatus;
            latestStatus = CallBridge("readLatestResultSummary");
            if (!string.Equals(previousStatus, latestStatus, StringComparison.Ordinal))
            {
                Debug.Log($"[QuestQuestionnairePanelLauncher] {latestStatus}", this);
            }

            return latestStatus;
        }

        void TryQueueLaunchExtra()
        {
            if (!allowQuestionnaireOpenLaunchExtra || _launchExtraPending)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTimeAsDouble < _nextLaunchExtraPollTime)
            {
                return;
            }

            _nextLaunchExtraPollTime = Time.unscaledTimeAsDouble + LaunchExtraPollSeconds;
            var flags = ConsumeLaunchExtra();
            if ((flags & LaunchExtraOpenBit) == 0)
            {
                return;
            }

            _launchExtraDebugAutoSubmit = (flags & LaunchExtraDebugAutoSubmitBit) != 0;
            _launchExtraDueTime = Time.unscaledTime + LaunchExtraDelaySeconds;
            _launchExtraPending = true;
            Debug.Log(
                $"[QuestQuestionnairePanelLauncher] questionnaire launch extra received debugAutoSubmit={_launchExtraDebugAutoSubmit}",
                this);
#endif
        }

        void ProcessQueuedLaunchExtra()
        {
            if (!_launchExtraPending || Time.unscaledTime < _launchExtraDueTime)
            {
                return;
            }

            _launchExtraPending = false;
            LaunchDemographicsFromTrigger("launch_extra", _launchExtraDebugAutoSubmit);
        }

        static string CallBridge(string methodName)
        {
            return CallBridge(methodName, debugAutoSubmit: false);
        }

        static string CallBridge(string methodName, bool debugAutoSubmit)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                {
                    return "Questionnaire panel bridge unavailable: missing Unity activity.";
                }

                using var bridge = new AndroidJavaClass(BridgeClassName);
                return methodName == "launchDemographics"
                    ? bridge.CallStatic<string>(methodName, activity, debugAutoSubmit)
                    : bridge.CallStatic<string>(methodName, activity);
            }
            catch (Exception ex)
            {
                return $"Questionnaire panel bridge failed: {ex.Message}";
            }
#else
            return "Questionnaire panel bridge requires an Android player build.";
#endif
        }

        static int ConsumeLaunchExtra()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                {
                    return 0;
                }

                using var bridge = new AndroidJavaClass(BridgeClassName);
                return bridge.CallStatic<int>("consumeQuestionnaireLaunchExtra", activity);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestQuestionnairePanelLauncher] launch extra check failed: {ex.Message}");
                return 0;
            }
#else
            return 0;
#endif
        }
    }
}

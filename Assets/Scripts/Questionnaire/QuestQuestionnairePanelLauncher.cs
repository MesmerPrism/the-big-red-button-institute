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
        const int LaunchExtraInitialBit = 4;
        const int LaunchExtraPostConditionOneBit = 8;
        const int LaunchExtraPostConditionTwoBit = 16;
        const int LaunchExtraFinalBit = 32;
        const int LaunchExtraInvalidBit = 64;
        const float LaunchExtraDelaySeconds = 1.0f;
        const float LaunchExtraPollSeconds = 0.5f;

        [SerializeField] bool allowQuestionnaireOpenLaunchExtra = true;
        [SerializeField] bool refreshStatusOnResume = true;
        [SerializeField] string latestStatus = "No request launched yet.";

        bool _launchExtraPending;
        bool _launchExtraDebugAutoSubmit;
        LaunchExtraRoute _launchExtraRoute;
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
            return LaunchInitialStudyQuestionnairesFromTrigger("direct", debugAutoSubmit);
        }

        public string LaunchDemographicsFromTrigger(string triggerName, bool debugAutoSubmit)
        {
            return LaunchInitialStudyQuestionnairesFromTrigger(triggerName, debugAutoSubmit);
        }

        public string LaunchInitialStudyQuestionnairesFromTrigger(string triggerName, bool debugAutoSubmit)
        {
            latestStatus = CallBridge("launchInitialStudyQuestionnaires", debugAutoSubmit);
            Debug.Log($"[QuestQuestionnairePanelLauncher] trigger={triggerName} debugAutoSubmit={debugAutoSubmit} {latestStatus}", this);
            return latestStatus;
        }

        public string LaunchPostConditionQuestionnairesFromTrigger(int conditionNumber, string triggerName, bool debugAutoSubmit)
        {
            latestStatus = CallPostConditionBridge(conditionNumber, debugAutoSubmit);
            Debug.Log(
                $"[QuestQuestionnairePanelLauncher] trigger={triggerName} condition={conditionNumber} debugAutoSubmit={debugAutoSubmit} {latestStatus}",
                this);
            return latestStatus;
        }

        public string LaunchFinalQuestionnairesFromTrigger(string triggerName, bool debugAutoSubmit)
        {
            latestStatus = CallBridge("launchFinalQuestionnaires", debugAutoSubmit);
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

            if ((flags & LaunchExtraInvalidBit) != 0)
            {
                Debug.LogWarning("[QuestQuestionnairePanelLauncher] ignored invalid questionnaire launch extra", this);
                return;
            }

            _launchExtraDebugAutoSubmit = (flags & LaunchExtraDebugAutoSubmitBit) != 0;
            _launchExtraRoute = DecodeLaunchExtraRoute(flags);
            _launchExtraDueTime = Time.unscaledTime + LaunchExtraDelaySeconds;
            _launchExtraPending = true;
            Debug.Log(
                $"[QuestQuestionnairePanelLauncher] questionnaire launch extra received route={_launchExtraRoute} debugAutoSubmit={_launchExtraDebugAutoSubmit}",
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
            switch (_launchExtraRoute)
            {
                case LaunchExtraRoute.PostConditionOne:
                    LaunchPostConditionQuestionnairesFromTrigger(1, "launch_extra:post_condition_1", _launchExtraDebugAutoSubmit);
                    break;
                case LaunchExtraRoute.PostConditionTwo:
                    LaunchPostConditionQuestionnairesFromTrigger(2, "launch_extra:post_condition_2", _launchExtraDebugAutoSubmit);
                    break;
                case LaunchExtraRoute.Final:
                    LaunchFinalQuestionnairesFromTrigger("launch_extra:final", _launchExtraDebugAutoSubmit);
                    break;
                default:
                    LaunchInitialStudyQuestionnairesFromTrigger("launch_extra:initial", _launchExtraDebugAutoSubmit);
                    break;
            }
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
                return AcceptsDebugAutoSubmit(methodName)
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

        static bool AcceptsDebugAutoSubmit(string methodName)
        {
            return string.Equals(methodName, "launchDemographics", StringComparison.Ordinal)
                || string.Equals(methodName, "launchInitialStudyQuestionnaires", StringComparison.Ordinal)
                || string.Equals(methodName, "launchFinalQuestionnaires", StringComparison.Ordinal);
        }

        static string CallPostConditionBridge(int conditionNumber, bool debugAutoSubmit)
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
                return bridge.CallStatic<string>(
                    "launchPostConditionQuestionnaires",
                    activity,
                    conditionNumber,
                    debugAutoSubmit);
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

        static LaunchExtraRoute DecodeLaunchExtraRoute(int flags)
        {
            if ((flags & LaunchExtraPostConditionOneBit) != 0)
            {
                return LaunchExtraRoute.PostConditionOne;
            }

            if ((flags & LaunchExtraPostConditionTwoBit) != 0)
            {
                return LaunchExtraRoute.PostConditionTwo;
            }

            if ((flags & LaunchExtraFinalBit) != 0)
            {
                return LaunchExtraRoute.Final;
            }

            return LaunchExtraRoute.Initial;
        }

        enum LaunchExtraRoute
        {
            Initial,
            PostConditionOne,
            PostConditionTwo,
            Final
        }
    }
}

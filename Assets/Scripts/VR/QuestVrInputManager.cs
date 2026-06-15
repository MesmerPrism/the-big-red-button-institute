using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.Diagnostics;
using TheBigRedButtonInstitute.Questionnaire;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-21)]
    public sealed class QuestVrInputManager : MonoBehaviour
    {
        const string ButtonObjectName = "Big Red Button";
        const string PreviousPageControlLabel = "R Stick Left / Left Arrow";
        const string NextPageControlLabel = "R Stick Right / Right Arrow";
        const string CommandMoveControlLabel = "R Stick Up/Down / Up Arrow / Down Arrow";
        const string CommandActivateControlLabel = "R Trigger / Enter";
        const string RuntimeCommandLaunchExtra = "brb.runtimeCommand";
        const string RuntimeCommandScriptLaunchExtra = "brb.runtimeCommandScript";
        const string RuntimeCommandRepeatLaunchExtra = "brb.runtimeCommandRepeat";
        const string RuntimeCommandIntervalMsLaunchExtra = "brb.runtimeCommandIntervalMs";
        const int ObsoleteExternalConsoleActionId = 7;
        const int FirstObsoleteExternalCommandId = 9;
        const int LastObsoleteExternalCommandId = 17;
        const int MaxCliRuntimeCommands = 64;
        const double RuntimeCommandPollSeconds = 0.5d;
        const double RuntimeCommandInitialDelaySeconds = 0.75d;
        const double DefaultRuntimeCommandIntervalSeconds = 0.35d;
        const float DefaultTimedBlinkSeconds = 5f;

        public enum VrActionId
        {
            None = 0,
            ToggleHud = 1,
            SelectPreviousCommand = 2,
            SelectNextCommand = 3,
            ExecuteSelectedCommand = 4,
            CenterButton = 5,
            ReplayButtonPress = 6
        }

        public enum VrTerminalCommandId
        {
            None = 0,
            CenterButton = 1,
            PressButton = 2,
            ToggleHud = 3,
            StatusSnapshot = 4,
            PolarConnect = 5,
            PolarScan = 6,
            PolarClearSavedDevice = 7,
            PolarRequestPermissions = 8,
            QuestionnaireOpen = 18,
            BlinkButton = 19,
            StopButtonBlink = 20,
            ReloadLayout = 21,
            LayoutStatus = 22
        }

        public enum VrControllerButtonId
        {
            None = 0,
            RightPrimaryButtonA = 1,
            RightSecondaryButtonB = 2,
            RightIndexTrigger = 3,
            RightThumbstickUp = 4,
            RightThumbstickDown = 5,
            RightThumbstickClick = 6,
            LeftPrimaryButtonX = 7,
            LeftSecondaryButtonY = 8,
            LeftIndexTrigger = 9,
            RightGripTrigger = 10
        }

        enum HudPageId
        {
            Dashboard = 0,
            Permissions = 1,
            Signals = 2,
            Terminal = 3,
            Input = 4
        }

        [Serializable]
        public struct ActionBinding
        {
            public VrControllerButtonId controllerButton;
            public KeyCode keyboardKey;
            public VrActionId action;
            public string label;
        }

        [Serializable]
        public struct TerminalCommand
        {
            public string command;
            public string description;
            public VrTerminalCommandId action;
        }

        readonly struct HudPageDefinition
        {
            public HudPageDefinition(HudPageId id, string name)
            {
                Id = id;
                Name = name;
            }

            public HudPageId Id { get; }
            public string Name { get; }
        }

        readonly struct RuntimeCommandParts
        {
            public RuntimeCommandParts(string command, string argument)
            {
                Command = command;
                Argument = argument;
            }

            public string Command { get; }
            public string Argument { get; }
        }

        static readonly HudPageDefinition[] HudPages =
        {
            new(HudPageId.Dashboard, "Dashboard"),
            new(HudPageId.Permissions, "Permissions"),
            new(HudPageId.Signals, "Signals"),
            new(HudPageId.Terminal, "Terminal"),
            new(HudPageId.Input, "Input")
        };

        [Header("References")]
        [SerializeField] QuestVrOverlayHud hud;
        [SerializeField] Transform headTransform;
        [SerializeField] Transform buttonTransform;
        [SerializeField] BigRedButtonAnimationTester buttonAnimationTester;
        [SerializeField] BigRedButtonBlinkController buttonBlinkController;
        [SerializeField] BigRedButtonManualPressController manualPressController;
        [SerializeField] PolarH10RuntimeManager polarRuntimeManager;
        [SerializeField] PolarHeartbeatButtonDriver polarHeartbeatButtonDriver;
        [SerializeField] BigRedButtonDiagnosticComparisonController diagnosticComparisonController;
        [SerializeField] QuestQuestionnairePanelLauncher questionnaireLauncher;

        [Header("Behavior")]
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool placeButtonOnStartup = true;
        [SerializeField] bool keepButtonInFrontOfHead = true;
        [SerializeField] bool enableSimultaneousHandsAndControllers = true;
        [SerializeField, Min(0f)] float startupPlacementDelay = 0.2f;
        [SerializeField, Min(0.2f)] float buttonDistanceFromHead = 0.48f;
        [SerializeField] float buttonVerticalOffset = -0.32f;
        [SerializeField, Min(0.4f)] float minimumButtonHeight = 0.54f;
        [SerializeField, Min(0.1f)] float targetButtonHeight = 0.36f;
        [SerializeField, Min(0.1f)] float defaultTimedBlinkSeconds = DefaultTimedBlinkSeconds;
        [SerializeField] Vector3 buttonRotationOffsetEuler;

        [Header("Runtime Layout Config")]
        [SerializeField] bool loadRuntimeLayoutConfigOnEnable = true;
        [SerializeField] bool writeRuntimeLayoutDefaultsIfMissing = true;
        [Tooltip("Default layout config path relative to Application.persistentDataPath. Absolute paths are also supported.")]
        [SerializeField] string runtimeLayoutDefaultsRelativePath = BigRedButtonRuntimeLayoutConfig.DefaultsRelativePath;
        [Tooltip("Optional active layout override path relative to Application.persistentDataPath. Absolute paths are also supported.")]
        [SerializeField] string runtimeLayoutOverrideRelativePath = BigRedButtonRuntimeLayoutConfig.RuntimeOverrideRelativePath;
        [SerializeField] bool recenterButtonAfterLayoutReload = true;
        [SerializeField] bool useAbsoluteButtonWorldY;
        [SerializeField] float absoluteButtonWorldY = BigRedButtonRuntimeLayoutConfig.DefaultAbsoluteButtonWorldY;
        [SerializeField] bool logRuntimeLayoutConfig = true;

        [Header("HUD Page Flick")]
        [SerializeField] bool enableHudPageFlickNavigation = true;
        [SerializeField, Range(0.3f, 0.95f)] float hudPageFlickThreshold = 0.7f;
        [SerializeField, Range(0.05f, 0.5f)] float hudPageFlickRearmThreshold = 0.25f;
        [SerializeField] KeyCode previousPageKeyboardKey = KeyCode.LeftArrow;
        [SerializeField] KeyCode nextPageKeyboardKey = KeyCode.RightArrow;

        [Header("HUD Cursor Flick")]
        [SerializeField] bool enableHudTerminalCursor = true;
        [SerializeField, Range(0.3f, 0.95f)] float thumbstickPressThreshold = 0.7f;
        [SerializeField, Range(0.05f, 0.5f)] float thumbstickRearmThreshold = 0.25f;
        [SerializeField] KeyCode cursorUpKeyboardKey = KeyCode.UpArrow;
        [SerializeField] KeyCode cursorDownKeyboardKey = KeyCode.DownArrow;

        [Header("Bindings")]
        [SerializeField] List<ActionBinding> bindings = new();
        [SerializeField] List<TerminalCommand> commands = new();

        bool _rightThumbstickVerticalArmed = true;
        bool _rightThumbstickHorizontalArmed = true;
        int _buttonPressCount;
        readonly Queue<string> _runtimeCommandQueue = new();
        bool _hasPlacedButtonOnStartup;
        bool _hasConfiguredSimultaneousHandsAndControllers;
        float _startupPlacementTime;
        double _nextRuntimeCommandPollTime;
        double _nextRuntimeCommandExecuteTime;
        double _runtimeCommandIntervalSeconds = DefaultRuntimeCommandIntervalSeconds;
        bool _timedButtonBlinkActive;
        double _buttonBlinkStopTime;
        BigRedButtonRuntimeLayoutConfig _activeRuntimeLayoutConfig;
        string _lastRuntimeLayoutStatus = "runtime layout not loaded";
        string _lastRuntimeLayoutSource = "inspector";

        public IReadOnlyList<ActionBinding> Bindings => bindings;
        public IReadOnlyList<TerminalCommand> Commands => commands;
        public int ButtonPressCount => _buttonPressCount;
        public BigRedButtonAnimationTester ButtonAnimationTester => buttonAnimationTester;
        public BigRedButtonBlinkController ButtonBlinkController => buttonBlinkController;

        void Reset()
        {
            EnsureConfiguration();
            ResolveReferences(forceRefresh: true);
        }

        void Awake()
        {
            EnsureConfiguration();
            ResolveReferences(forceRefresh: true);
            if (loadRuntimeLayoutConfigOnEnable)
            {
                ReloadRuntimeLayoutConfigFromDisk(recenterButton: false, reportToHud: false);
            }
            hud?.ConfigureReferences(this, headTransform);
            EnsureReasonableButtonScale();
            TryConfigureSimultaneousHandsAndControllers();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            if (loadRuntimeLayoutConfigOnEnable)
            {
                ReloadRuntimeLayoutConfigFromDisk(recenterButton: false, reportToHud: false);
            }
            hud?.ConfigureReferences(this, headTransform);
            hud?.RefreshImmediately();
            ArmStartupPlacement();
            TryConfigureSimultaneousHandsAndControllers();
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);
            TryConfigureSimultaneousHandsAndControllers();
            TryPlaceButtonOnStartup();
            TryKeepButtonInFrontOfHead();
            TryQueueRuntimeCommandLaunchExtra();
            ProcessRuntimeCommandQueue();
            ProcessTimedButtonBlink();
            ProcessHudNavigation();
            ProcessBindings();
        }

        public void ConfigureReferences(QuestVrOverlayHud targetHud, Transform head, Transform button, BigRedButtonAnimationTester tester)
        {
            hud = targetHud;
            headTransform = head;
            buttonTransform = button;
            buttonAnimationTester = tester;
            buttonBlinkController = button != null ? button.GetComponent<BigRedButtonBlinkController>() : null;
            manualPressController = button != null ? button.GetComponent<BigRedButtonManualPressController>() : null;
            EnsureConfiguration();
            ApplyRuntimeLayoutConfig(_activeRuntimeLayoutConfig, recenterButton: false);
            hud?.ConfigureReferences(this, headTransform);
            ArmStartupPlacement();
        }

        public void ConfigurePolarReferences(PolarH10RuntimeManager runtimeManager, PolarHeartbeatButtonDriver heartbeatButtonDriver)
        {
            polarRuntimeManager = runtimeManager;
            polarHeartbeatButtonDriver = heartbeatButtonDriver;
        }

        public void ConfigureDiagnosticReferences(BigRedButtonDiagnosticComparisonController comparisonController)
        {
            diagnosticComparisonController = comparisonController;
        }

        public void ConfigureQuestionnaireReferences(QuestQuestionnairePanelLauncher launcher)
        {
            questionnaireLauncher = launcher;
        }

        public void EnsureConfiguration()
        {
            bindings ??= new List<ActionBinding>();
            commands ??= new List<TerminalCommand>();
            MigrateLegacyBindings();
            RemoveLegacyCommandCursorBindings();
            RemoveObsoleteExternalBindings();
            MergeMissingBindings();
            MergeMissingCommands();
            RemoveObsoleteExternalCommands();
        }

        public int GetHudPageCount() => HudPages.Length;

        public string GetHudPageName(int index)
        {
            return GetHudPage(index).Name;
        }

        public string BuildHudText(int activePageIndex, int selectedCommandIndex, string transientMessage)
        {
            ResolveReferences(forceRefresh: false);

            var builder = new StringBuilder(2048);
            var page = GetHudPage(activePageIndex);
            var statusText = string.IsNullOrWhiteSpace(transientMessage) ? "ready" : transientMessage.Trim();

            builder.AppendLine("<b><size=118%><color=#8FE6FF>=== BIG RED BUTTON ===</color></size></b>");
            builder.AppendLine($"<size=78%><color=#7FA6B8>[ {DateTime.UtcNow:HH:mm:ss} UTC ]</color></size>");
            builder.AppendLine($"<size=82%><color=#AFC0CF>Page:</color> <color=#C7FFA2>{EscapeRichText(page.Name)}</color></size>");
            builder.AppendLine($"<size=82%><color=#AFC0CF>Message:</color> <color=#EAF6FF>{EscapeRichText(statusText)}</color></size>");
            builder.AppendLine();

            switch (page.Id)
            {
                case HudPageId.Dashboard:
                    AppendDashboardPage(builder);
                    break;
                case HudPageId.Permissions:
                    AppendPermissionsPage(builder);
                    break;
                case HudPageId.Signals:
                    AppendSignalsPage(builder);
                    break;
                case HudPageId.Terminal:
                    AppendTerminalPage(builder, selectedCommandIndex);
                    break;
                case HudPageId.Input:
                    AppendInputPage(builder);
                    break;
            }

            builder.AppendLine("<color=#4C5A66>--------------------</color>");
            builder.Append(BuildPageFooter(activePageIndex));
            return builder.ToString().TrimEnd('\n');
        }

        public void ExecuteSelectedCommand()
        {
            if (hud == null)
            {
                return;
            }

            ExecuteCommand(hud.GetSelectedCommand());
        }

        public void CenterButtonInFrontOfHead()
        {
            CenterButtonInFrontOfHead(reportToHud: true);
        }

        bool CenterButtonInFrontOfHead(bool reportToHud)
        {
            ResolveReferences(forceRefresh: false);
            if (buttonTransform == null || headTransform == null)
            {
                if (reportToHud)
                {
                    hud?.SetTransientMessage("center_button failed: missing references");
                }

                return false;
            }

            EnsureReasonableButtonScale();

            if (!TryResolveButtonPoseInFrontOfHead(out var targetPosition, out var targetRotation))
            {
                if (reportToHud)
                {
                    hud?.SetTransientMessage("center_button failed: invalid head direction");
                }

                return false;
            }

            buttonTransform.SetPositionAndRotation(targetPosition, targetRotation);

            _hasPlacedButtonOnStartup = true;
            if (reportToHud)
            {
                hud?.SetTransientMessage("center_button executed");
            }

            hud?.RefreshImmediately();
            return true;
        }

        public bool TriggerButtonPressFromRuntime()
        {
            ResolveReferences(forceRefresh: false);
            var triggered = false;

            if (buttonAnimationTester != null)
            {
                buttonAnimationTester.PlayPressed();
                triggered = true;
            }

            if (!triggered)
            {
                return false;
            }

            _buttonPressCount++;
            hud?.RefreshImmediately();
            return true;
        }

        public bool TriggerButtonBlinkFromRuntime()
        {
            ResolveReferences(forceRefresh: false);
            if (buttonBlinkController == null)
            {
                return false;
            }

            buttonAnimationTester?.StopAndReset();
            buttonBlinkController.PulseOnce();
            hud?.RefreshImmediately();
            return true;
        }

        void ProcessHudNavigation()
        {
            if (hud == null)
            {
                return;
            }

            var thumbstick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            var pageHandled = TryProcessHudPageKeyboardNavigation();
            if (!pageHandled)
            {
                pageHandled = TryProcessHudPageStickNavigation(thumbstick);
            }

            var cursorHandled = TryProcessHudCursorKeyboardNavigation();
            if (!cursorHandled)
            {
                TryProcessHudCursorStickNavigation(thumbstick, pageHandled);
            }
        }

        void ProcessBindings()
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.action == VrActionId.None)
                {
                    continue;
                }

                if (binding.action == VrActionId.SelectPreviousCommand || binding.action == VrActionId.SelectNextCommand)
                {
                    continue;
                }

                if (WasPressed(binding))
                {
                    ExecuteAction(binding.action);
                }
            }
        }

        bool TryProcessHudPageKeyboardNavigation()
        {
            if (WasKeyboardPressed(nextPageKeyboardKey))
            {
                return hud.SelectNextPage();
            }

            if (WasKeyboardPressed(previousPageKeyboardKey))
            {
                return hud.SelectPreviousPage();
            }

            return false;
        }

        bool TryProcessHudPageStickNavigation(Vector2 thumbstick)
        {
            if (!enableHudPageFlickNavigation)
            {
                _rightThumbstickHorizontalArmed = true;
                return false;
            }

            var horizontal = thumbstick.x;
            var absHorizontal = Mathf.Abs(horizontal);

            if (!_rightThumbstickHorizontalArmed)
            {
                if (absHorizontal <= hudPageFlickRearmThreshold)
                {
                    _rightThumbstickHorizontalArmed = true;
                }

                return false;
            }

            if (absHorizontal < hudPageFlickThreshold)
            {
                return false;
            }

            _rightThumbstickHorizontalArmed = false;
            return horizontal > 0f ? hud.SelectNextPage() : hud.SelectPreviousPage();
        }

        bool TryProcessHudCursorKeyboardNavigation()
        {
            if (WasKeyboardPressed(cursorDownKeyboardKey))
            {
                hud.SelectNextCommand();
                return true;
            }

            if (WasKeyboardPressed(cursorUpKeyboardKey))
            {
                hud.SelectPreviousCommand();
                return true;
            }

            return false;
        }

        bool TryProcessHudCursorStickNavigation(Vector2 thumbstick, bool suppressStickNavigation)
        {
            if (!enableHudTerminalCursor || suppressStickNavigation)
            {
                _rightThumbstickVerticalArmed = true;
                return false;
            }

            var vertical = thumbstick.y;
            var absVertical = Mathf.Abs(vertical);

            if (!_rightThumbstickVerticalArmed)
            {
                if (absVertical <= thumbstickRearmThreshold)
                {
                    _rightThumbstickVerticalArmed = true;
                }

                return false;
            }

            if (absVertical < thumbstickPressThreshold)
            {
                return false;
            }

            _rightThumbstickVerticalArmed = false;
            if (vertical > 0f)
            {
                hud.SelectPreviousCommand();
            }
            else
            {
                hud.SelectNextCommand();
            }

            return true;
        }

        void ExecuteAction(VrActionId action)
        {
            switch (action)
            {
                case VrActionId.ToggleHud:
                    if (hud != null)
                    {
                        hud.ToggleVisibility();
                        if (hud.IsVisible)
                        {
                            hud.SetTransientMessage("hud visible");
                        }
                    }
                    break;
                case VrActionId.SelectPreviousCommand:
                    hud?.SelectPreviousCommand();
                    break;
                case VrActionId.SelectNextCommand:
                    hud?.SelectNextCommand();
                    break;
                case VrActionId.ExecuteSelectedCommand:
                    ExecuteSelectedCommand();
                    break;
                case VrActionId.CenterButton:
                    CenterButtonInFrontOfHead();
                    break;
                case VrActionId.ReplayButtonPress:
                    ReplayButtonPress();
                    break;
            }
        }

        void ExecuteCommand(TerminalCommand command)
        {
            switch (command.action)
            {
                case VrTerminalCommandId.CenterButton:
                    CenterButtonInFrontOfHead();
                    break;
                case VrTerminalCommandId.PressButton:
                    ReplayButtonPress();
                    break;
                case VrTerminalCommandId.BlinkButton:
                    StartTimedButtonBlink(defaultTimedBlinkSeconds, "hud_terminal");
                    break;
                case VrTerminalCommandId.StopButtonBlink:
                    StopTimedButtonBlink("hud_terminal");
                    break;
                case VrTerminalCommandId.ToggleHud:
                    ExecuteAction(VrActionId.ToggleHud);
                    break;
                case VrTerminalCommandId.StatusSnapshot:
                    var snapshot = BuildStatusSummary();
                    Debug.Log($"[QuestVrInputManager] {snapshot}", this);
                    hud?.SetTransientMessage($"status: {snapshot}");
                    break;
                case VrTerminalCommandId.ReloadLayout:
                    ReloadRuntimeLayoutConfigFromDisk(recenterButtonAfterLayoutReload, reportToHud: true);
                    break;
                case VrTerminalCommandId.LayoutStatus:
                    LogRuntimeLayoutStatus(reportToHud: true);
                    break;
                case VrTerminalCommandId.PolarConnect:
                    if (polarRuntimeManager == null)
                    {
                        hud?.SetTransientMessage("polar_connect failed: runtime missing");
                        break;
                    }

                    polarRuntimeManager.BeginConnectFlow(true);
                    hud?.SetTransientMessage("polar_connect requested");
                    break;
                case VrTerminalCommandId.PolarScan:
                    if (polarRuntimeManager == null)
                    {
                        hud?.SetTransientMessage("polar_scan failed: runtime missing");
                        break;
                    }

                    polarRuntimeManager.BeginScanFlow(true);
                    hud?.SetTransientMessage("polar_scan requested");
                    break;
                case VrTerminalCommandId.PolarClearSavedDevice:
                    if (polarRuntimeManager == null)
                    {
                        hud?.SetTransientMessage("polar_clear_saved_device failed: runtime missing");
                        break;
                    }

                    polarRuntimeManager.ClearSavedDevice();
                    hud?.SetTransientMessage("polar device cleared");
                    break;
                case VrTerminalCommandId.PolarRequestPermissions:
                    if (polarRuntimeManager == null)
                    {
                        hud?.SetTransientMessage("polar_permissions failed: runtime missing");
                        break;
                    }

                    polarRuntimeManager.RequestBlePermissionsOnly();
                    hud?.SetTransientMessage("polar_permissions requested");
                    break;
                case VrTerminalCommandId.QuestionnaireOpen:
                    OpenQuestionnairePanel();
                    break;
            }
        }

        void OpenQuestionnairePanel()
        {
            if (questionnaireLauncher == null)
            {
                hud?.SetTransientMessage("questionnaire_open failed: launcher missing");
                return;
            }

            var status = questionnaireLauncher.LaunchDemographicsFromTrigger("hud_terminal", debugAutoSubmit: false);
            hud?.SetTransientMessage(status);
        }

        void ReplayButtonPress()
        {
            if (!TriggerButtonPressFromRuntime())
            {
                hud?.SetTransientMessage("press_button failed: no button visual");
                return;
            }

            hud?.SetTransientMessage("press_button executed");
        }

        bool StartTimedButtonBlink(float durationSeconds, string source)
        {
            ResolveReferences(forceRefresh: false);
            if (buttonBlinkController == null)
            {
                hud?.SetTransientMessage("blink_button failed: no blink controller");
                Debug.LogWarning($"[QuestVrInputManager] blink_button failed source={source}: no blink controller", this);
                return false;
            }

            var seconds = Mathf.Clamp(durationSeconds, 0.1f, 120f);
            buttonAnimationTester?.StopAndReset();
            buttonBlinkController.SetBlinking(true);
            _timedButtonBlinkActive = true;
            _buttonBlinkStopTime = Time.unscaledTimeAsDouble + seconds;
            hud?.SetTransientMessage($"blink_button executing: {seconds:0.#}s");
            hud?.RefreshImmediately();
            Debug.Log($"[QuestVrInputManager] blink_button source={source} duration={seconds:0.###}s", this);
            return true;
        }

        void StopTimedButtonBlink(string source)
        {
            _timedButtonBlinkActive = false;
            if (buttonBlinkController != null)
            {
                buttonBlinkController.SetBlinking(false);
            }

            hud?.SetTransientMessage("blink_button stopped");
            hud?.RefreshImmediately();
            Debug.Log($"[QuestVrInputManager] blink_button stopped source={source}", this);
        }

        void ProcessTimedButtonBlink()
        {
            if (!_timedButtonBlinkActive || Time.unscaledTimeAsDouble < _buttonBlinkStopTime)
            {
                return;
            }

            StopTimedButtonBlink("timer");
        }

        void TryQueueRuntimeCommandLaunchExtra()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTimeAsDouble < _nextRuntimeCommandPollTime)
            {
                return;
            }

            _nextRuntimeCommandPollTime = Time.unscaledTimeAsDouble + RuntimeCommandPollSeconds;
            var commandsFromIntent = ReadRuntimeCommandLaunchExtras(out var intervalSeconds);
            if (commandsFromIntent.Count == 0)
            {
                return;
            }

            _runtimeCommandIntervalSeconds = intervalSeconds;
            var queued = 0;
            for (var i = 0; i < commandsFromIntent.Count && _runtimeCommandQueue.Count < MaxCliRuntimeCommands; i++)
            {
                var command = commandsFromIntent[i];
                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                _runtimeCommandQueue.Enqueue(command.Trim());
                queued++;
            }

            if (queued > 0)
            {
                _nextRuntimeCommandExecuteTime = Time.unscaledTimeAsDouble + RuntimeCommandInitialDelaySeconds;
                Debug.Log(
                    $"[QuestVrInputManager] queued {queued} CLI runtime command(s), interval={_runtimeCommandIntervalSeconds:0.###}s",
                    this);
            }
#endif
        }

        void ProcessRuntimeCommandQueue()
        {
            if (_runtimeCommandQueue.Count == 0 ||
                Time.unscaledTimeAsDouble < _nextRuntimeCommandExecuteTime)
            {
                return;
            }

            var command = _runtimeCommandQueue.Dequeue();
            ExecuteRuntimeCommand(command);
            _nextRuntimeCommandExecuteTime = Time.unscaledTimeAsDouble + _runtimeCommandIntervalSeconds;
        }

        bool ExecuteRuntimeCommand(string rawCommand)
        {
            var parsed = ParseRuntimeCommand(rawCommand);
            var command = parsed.Command;
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            switch (command)
            {
                case "center":
                case "center_button":
                    return CenterButtonInFrontOfHead(reportToHud: true);
                case "press":
                case "press_button":
                case "button_press":
                    ReplayButtonPress();
                    Debug.Log($"[QuestVrInputManager] CLI command press_button count={_buttonPressCount}", this);
                    return true;
                case "blink":
                case "blink_button":
                case "button_blink":
                case "heartbeat_blink":
                    return StartTimedButtonBlink(
                        ParseDurationSeconds(parsed.Argument, defaultTimedBlinkSeconds),
                        "cli");
                case "stop_blink":
                case "blink_stop":
                case "button_blink_stop":
                    StopTimedButtonBlink("cli");
                    return true;
                case "toggle_hud":
                case "hud_toggle":
                    ExecuteAction(VrActionId.ToggleHud);
                    return true;
                case "status":
                case "status_snapshot":
                    var snapshot = BuildStatusSummary();
                    Debug.Log($"[QuestVrInputManager] CLI status: {snapshot}", this);
                    hud?.SetTransientMessage($"status: {snapshot}");
                    return true;
                case "reload_layout":
                case "layout_reload":
                case "runtime_layout_reload":
                case "reload_runtime_layout":
                    ReloadRuntimeLayoutConfigFromDisk(recenterButtonAfterLayoutReload, reportToHud: true);
                    Debug.Log($"[QuestVrInputManager] CLI layout reload: {_lastRuntimeLayoutStatus}", this);
                    return true;
                case "layout_status":
                case "runtime_layout_status":
                    LogRuntimeLayoutStatus(reportToHud: true);
                    return true;
                case "questionnaire_open":
                case "questionnaire_initial":
                case "initial":
                    return LaunchQuestionnaireFromRuntimeCommand(
                        "cli:initial",
                        route => route.LaunchInitialStudyQuestionnairesFromTrigger("cli:initial", false));
                case "questionnaire_initial_debug":
                case "initial_debug":
                    return LaunchQuestionnaireFromRuntimeCommand(
                        "cli:initial_debug",
                        route => route.LaunchInitialStudyQuestionnairesFromTrigger("cli:initial_debug", true));
                case "questionnaire_post_1":
                case "post_condition_1":
                case "post1":
                    return LaunchQuestionnaireFromRuntimeCommand(
                        "cli:post_condition_1",
                        route => route.LaunchPostConditionQuestionnairesFromTrigger(1, "cli:post_condition_1", false));
                case "questionnaire_post_2":
                case "post_condition_2":
                case "post2":
                    return LaunchQuestionnaireFromRuntimeCommand(
                        "cli:post_condition_2",
                        route => route.LaunchPostConditionQuestionnairesFromTrigger(2, "cli:post_condition_2", false));
                case "questionnaire_final":
                case "final":
                    return LaunchQuestionnaireFromRuntimeCommand(
                        "cli:final",
                        route => route.LaunchFinalQuestionnairesFromTrigger("cli:final", false));
                case "questionnaire_final_debug":
                case "final_debug":
                    return LaunchQuestionnaireFromRuntimeCommand(
                        "cli:final_debug",
                        route => route.LaunchFinalQuestionnairesFromTrigger("cli:final_debug", true));
                default:
                    Debug.LogWarning($"[QuestVrInputManager] unknown CLI runtime command '{rawCommand}'", this);
                    hud?.SetTransientMessage($"unknown CLI command: {rawCommand}");
                    return false;
            }
        }

        bool LaunchQuestionnaireFromRuntimeCommand(
            string source,
            Func<QuestQuestionnairePanelLauncher, string> launch)
        {
            ResolveReferences(forceRefresh: false);
            if (questionnaireLauncher == null)
            {
                hud?.SetTransientMessage("questionnaire command failed: launcher missing");
                Debug.LogWarning($"[QuestVrInputManager] questionnaire command failed source={source}: launcher missing", this);
                return false;
            }

            var status = launch(questionnaireLauncher);
            hud?.SetTransientMessage(status);
            Debug.Log($"[QuestVrInputManager] questionnaire command source={source} {status}", this);
            return true;
        }

        static RuntimeCommandParts ParseRuntimeCommand(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
            {
                return new RuntimeCommandParts(string.Empty, string.Empty);
            }

            var trimmed = rawCommand.Trim();
            var separatorIndex = trimmed.IndexOfAny(new[] { ':', '=' });
            var command = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
            var argument = separatorIndex >= 0 && separatorIndex < trimmed.Length - 1
                ? trimmed[(separatorIndex + 1)..]
                : string.Empty;

            return new RuntimeCommandParts(NormalizeRuntimeCommandName(command), argument.Trim());
        }

        static string NormalizeRuntimeCommandName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        }

        float ParseDurationSeconds(string argument, float fallbackSeconds)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return Mathf.Max(0.1f, fallbackSeconds);
            }

            var value = argument.Trim();
            if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^1];
            }

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? Mathf.Clamp(seconds, 0.1f, 120f)
                : Mathf.Max(0.1f, fallbackSeconds);
        }

        static List<string> SplitRuntimeCommandScript(string script)
        {
            var commands = new List<string>();
            if (string.IsNullOrWhiteSpace(script))
            {
                return commands;
            }

            var parts = script.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var command = parts[i].Trim();
                if (!string.IsNullOrWhiteSpace(command))
                {
                    commands.Add(command);
                }
            }

            return commands;
        }

        static List<string> ReadRuntimeCommandLaunchExtras(out double intervalSeconds)
        {
            intervalSeconds = DefaultRuntimeCommandIntervalSeconds;
            var commands = new List<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity?.Call<AndroidJavaObject>("getIntent");
                if (intent == null)
                {
                    return commands;
                }

                var hasCommand = intent.Call<bool>("hasExtra", RuntimeCommandLaunchExtra);
                var hasScript = intent.Call<bool>("hasExtra", RuntimeCommandScriptLaunchExtra);
                var hasRepeat = intent.Call<bool>("hasExtra", RuntimeCommandRepeatLaunchExtra);
                var hasInterval = intent.Call<bool>("hasExtra", RuntimeCommandIntervalMsLaunchExtra);
                if (!hasCommand && !hasScript && !hasRepeat && !hasInterval)
                {
                    return commands;
                }

                var command = hasCommand
                    ? intent.Call<string>("getStringExtra", RuntimeCommandLaunchExtra)
                    : string.Empty;
                var script = hasScript
                    ? intent.Call<string>("getStringExtra", RuntimeCommandScriptLaunchExtra)
                    : string.Empty;
                var repeat = Mathf.Clamp(
                    intent.Call<int>("getIntExtra", RuntimeCommandRepeatLaunchExtra, 1),
                    1,
                    MaxCliRuntimeCommands);
                var intervalMs = Mathf.Clamp(
                    intent.Call<int>("getIntExtra", RuntimeCommandIntervalMsLaunchExtra, 350),
                    50,
                    10000);

                intent.Call("removeExtra", RuntimeCommandLaunchExtra);
                intent.Call("removeExtra", RuntimeCommandScriptLaunchExtra);
                intent.Call("removeExtra", RuntimeCommandRepeatLaunchExtra);
                intent.Call("removeExtra", RuntimeCommandIntervalMsLaunchExtra);

                intervalSeconds = intervalMs / 1000d;
                commands.AddRange(SplitRuntimeCommandScript(script));
                if (!string.IsNullOrWhiteSpace(command))
                {
                    for (var i = 0; i < repeat && commands.Count < MaxCliRuntimeCommands; i++)
                    {
                        commands.Add(command);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestVrInputManager] runtime command launch extra check failed: {ex.Message}");
            }
#endif
            return commands;
        }

        string BuildStatusSummary()
        {
            var summary = new StringBuilder(160);
            summary.Append("button ");
            summary.Append(buttonTransform == null ? "missing" : "ready");
            summary.Append(" / presses ");
            summary.Append(_buttonPressCount);

            if (TryGetButtonBounds(out var bounds))
            {
                summary.Append(" / height ");
                summary.Append(bounds.size.y.ToString("0.00"));
                summary.Append("m");
            }

            if (headTransform != null && buttonTransform != null)
            {
                summary.Append(" / distance ");
                summary.Append(Vector3.Distance(headTransform.position, buttonTransform.position).ToString("0.00"));
                summary.Append("m");
            }

            summary.Append(" / layout ");
            summary.Append(DescribeRuntimeLayoutCompact());

            if (polarRuntimeManager != null)
            {
                summary.Append(" / perms ");
                summary.Append(polarRuntimeManager.GetBlePermissionStatusLabel());
                summary.Append(" / ");
                summary.Append(polarRuntimeManager.BuildPlainStatusSummary());
            }

            if (questionnaireLauncher != null)
            {
                summary.Append(" / questionnaire ");
                summary.Append(questionnaireLauncher.LatestStatus);
            }

            return summary.ToString();
        }

        void AppendDashboardPage(StringBuilder builder)
        {
            AppendButtonSection(builder);
            builder.AppendLine();
            AppendQuestionnaireSection(builder);
            builder.AppendLine();
            builder.AppendLine("<b><color=#FFB56B>[POLAR SNAPSHOT]</color></b>");
            if (polarRuntimeManager == null)
            {
                builder.AppendLine("<color=#AFC0CF>Status:</color> <color=#97A9B6>runtime unavailable</color>");
                return;
            }

            builder.AppendLine($"<color=#AFC0CF>Permissions:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetBlePermissionStatusLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Guidance:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetBlePermissionGuidanceLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Polar:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetConnectionStateLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Heart:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetHeartbeatLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Button drive:</color> <color=#EAF6FF>{EscapeRichText(GetButtonDriveLabel())}</color>");
        }

        void AppendPermissionsPage(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#66FFCC>[PERMISSIONS]</color></b>");
            if (polarRuntimeManager == null)
            {
                builder.AppendLine("<color=#AFC0CF>Status:</color> <color=#97A9B6>runtime unavailable</color>");
                return;
            }

            builder.AppendLine($"<color=#AFC0CF>Bluetooth:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetBlePermissionStatusLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Guidance:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetBlePermissionGuidanceLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Runtime state:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.StatusMessage)}</color>");
            builder.AppendLine();
            builder.AppendLine("<b><color=#FFB56B>[CONNECTION]</color></b>");
            builder.AppendLine($"<color=#AFC0CF>BLE:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetBleStateLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Polar:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetConnectionStateLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Device:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.ConnectedDeviceName)}</color>");
            builder.AppendLine($"<color=#AFC0CF>Seen:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetRecentDevicesLabel())}</color>");
        }

        void AppendSignalsPage(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#66FFCC>[HEART / BREATH / COHERENCE]</color></b>");
            if (polarRuntimeManager == null)
            {
                builder.AppendLine("<color=#AFC0CF>Status:</color> <color=#97A9B6>runtime unavailable</color>");
                return;
            }

            builder.AppendLine($"<color=#AFC0CF>Heart:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetHeartbeatLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Breath:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetBreathingLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Coherence:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.GetCoherenceLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Button drive:</color> <color=#EAF6FF>{EscapeRichText(GetButtonDriveLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Press count:</color> <color=#EAF6FF>{_buttonPressCount:N0}</color>");
            builder.AppendLine($"<color=#AFC0CF>Status:</color> <color=#EAF6FF>{EscapeRichText(polarRuntimeManager.StatusMessage)}</color>");
            builder.AppendLine();
            AppendDiagnosticSection(builder);
        }

        void AppendQuestionnaireSection(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#66FFCC>[QUESTIONNAIRE PANEL]</color></b>");
            if (questionnaireLauncher == null)
            {
                builder.AppendLine("<color=#AFC0CF>Status:</color> <color=#97A9B6>launcher unavailable</color>");
                return;
            }

            builder.AppendLine($"<color=#AFC0CF>Status:</color> <color=#EAF6FF>{EscapeRichText(questionnaireLauncher.LatestStatus)}</color>");
        }

        void AppendDiagnosticSection(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#66FFCC>[COMPARISON ROUTES]</color></b>");
            if (diagnosticComparisonController == null)
            {
                builder.AppendLine("<color=#AFC0CF>Status:</color> <color=#97A9B6>comparison controller unavailable</color>");
                return;
            }

            var lines = diagnosticComparisonController.BuildHudLines();
            for (var i = 0; i < lines.Count; i++)
            {
                builder.AppendLine($"<color=#AFC0CF>{EscapeRichText(lines[i])}</color>");
            }
        }

        void AppendTerminalPage(StringBuilder builder, int selectedCommandIndex)
        {
            builder.AppendLine("<b><color=#FFD892>[TERMINAL COMMANDS]</color></b>");
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var marker = i == selectedCommandIndex ? "&gt;" : " ";
                var markerColor = i == selectedCommandIndex ? "#FFD79A" : "#6E8190";
                var commandColor = i == selectedCommandIndex ? "#FFF4D0" : "#E8F2FF";
                var description = string.IsNullOrWhiteSpace(command.description) ? "no description" : command.description.Trim();

                builder.AppendLine(
                    $"<color={markerColor}>{marker}</color> <color={commandColor}>{EscapeRichText(command.command)}</color> " +
                    $"<color=#97A9B6>-</color> <color=#AFC0CF>{EscapeRichText(description)}</color>");
            }
        }

        void AppendInputPage(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#C7FFA2>[INPUT MAPPINGS]</color></b>");

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.action == VrActionId.None)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(binding.label) ? binding.action.ToString() : binding.label.Trim();
                builder.AppendLine(
                    $"<b><color=#FFD79A>{EscapeRichText(GetControllerButtonLabel(binding.controllerButton))}</color></b> " +
                    $"<color=#97A9B6>/</color> <b><color=#C2F5A1>{EscapeRichText(GetKeyboardLabel(binding.keyboardKey))}</color></b> " +
                    $"<color=#97A9B6>-></color> <color=#E8F2FF>{EscapeRichText(label)}</color>");
            }

            builder.AppendLine();
            builder.AppendLine("<b><color=#66FFCC>[HUD CONTROLS]</color></b>");
            builder.AppendLine($"<color=#AFC0CF>Page nav:</color> <color=#EAF6FF>{PreviousPageControlLabel} / {NextPageControlLabel}</color>");
            builder.AppendLine($"<color=#AFC0CF>Command move:</color> <color=#EAF6FF>{CommandMoveControlLabel}</color>");
            builder.AppendLine($"<color=#AFC0CF>Command select:</color> <color=#EAF6FF>{CommandActivateControlLabel}</color>");
        }

        string BuildPageFooter(int activePageIndex)
        {
            var builder = new StringBuilder(512);
            builder.AppendLine(
                $"<size=78%><b><color=#C7FFA2>PAGE NAV</color></b> " +
                $"<color=#AFC0CF>Prev:</color> <color=#FFD79A>{PreviousPageControlLabel}</color>  " +
                $"<color=#AFC0CF>Next:</color> <color=#FFD79A>{NextPageControlLabel}</color>  " +
                $"<color=#AFC0CF>Move:</color> <color=#FFD79A>{CommandMoveControlLabel}</color>  " +
                $"<color=#AFC0CF>Select:</color> <color=#FFD79A>{CommandActivateControlLabel}</color></size>");
            builder.Append("<size=78%><color=#AFC0CF>Pages:</color> ");

            for (var i = 0; i < HudPages.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(" <color=#7C8A95>|</color> ");
                }

                var isActive = i == Mathf.Clamp(activePageIndex, 0, HudPages.Length - 1);
                var name = HudPages[i].Name;
                if (isActive)
                {
                    builder.Append($"<color=#C7FFA2>{EscapeRichText(name)}<color=#FFD79A>*</color></color>");
                }
                else
                {
                    builder.Append($"<color=#AFC0CF>{EscapeRichText(name)}</color>");
                }
            }

            builder.Append("</size>");
            return builder.ToString();
        }

        void AppendButtonSection(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#66FFCC>[BUTTON]</color></b>");
            builder.AppendLine($"<color=#AFC0CF>Press count:</color> <color=#EAF6FF>{_buttonPressCount:N0}</color>");

            if (TryGetButtonBounds(out var bounds))
            {
                builder.AppendLine($"<color=#AFC0CF>Height:</color> <color=#EAF6FF>{bounds.size.y:0.00} m</color>");
            }
            else
            {
                builder.AppendLine("<color=#AFC0CF>Height:</color> <color=#EAF6FF>n/a</color>");
            }

            if (headTransform != null && buttonTransform != null)
            {
                builder.AppendLine($"<color=#AFC0CF>Distance:</color> <color=#EAF6FF>{Vector3.Distance(headTransform.position, buttonTransform.position):0.00} m</color>");
            }
            else
            {
                builder.AppendLine("<color=#AFC0CF>Distance:</color> <color=#EAF6FF>n/a</color>");
            }
        }

        string GetButtonDriveLabel()
        {
            return polarHeartbeatButtonDriver != null ? polarHeartbeatButtonDriver.DriveStateLabel : "manual only";
        }

        HudPageDefinition GetHudPage(int index)
        {
            var clampedIndex = Mathf.Clamp(index, 0, HudPages.Length - 1);
            return HudPages[clampedIndex];
        }

        void EnsureReasonableButtonScale()
        {
            if (buttonTransform == null || !TryGetButtonBounds(out var bounds) || bounds.size.y <= 0.0001f)
            {
                return;
            }

            var scaleFactor = targetButtonHeight / bounds.size.y;
            buttonTransform.localScale *= scaleFactor;
        }

        bool TryGetButtonBounds(out Bounds bounds)
        {
            bounds = default;
            if (buttonTransform == null)
            {
                return false;
            }

            var renderers = buttonTransform.GetComponentsInChildren<Renderer>(true);
            Renderer rootRenderer = null;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<BigRedButtonColliderDebugVisual>() != null)
                {
                    continue;
                }

                rootRenderer = renderer;
                break;
            }

            if (rootRenderer == null)
            {
                return false;
            }

            bounds = rootRenderer.bounds;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer == rootRenderer || renderer.GetComponentInParent<BigRedButtonColliderDebugVisual>() != null)
                {
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return true;
        }

        static List<ActionBinding> BuildDefaultBindings()
        {
            return new List<ActionBinding>
            {
                new() { controllerButton = VrControllerButtonId.RightSecondaryButtonB, keyboardKey = KeyCode.C, action = VrActionId.CenterButton, label = "center_button" },
                new() { controllerButton = VrControllerButtonId.RightGripTrigger, keyboardKey = KeyCode.H, action = VrActionId.ToggleHud, label = "toggle_hud" },
                new() { controllerButton = VrControllerButtonId.RightIndexTrigger, keyboardKey = KeyCode.Return, action = VrActionId.ExecuteSelectedCommand, label = "execute_command" },
                new() { controllerButton = VrControllerButtonId.RightPrimaryButtonA, keyboardKey = KeyCode.P, action = VrActionId.ReplayButtonPress, label = "press_button" }
            };
        }

        void MigrateLegacyBindings()
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].action != VrActionId.ToggleHud)
                {
                    continue;
                }

                var binding = bindings[i];
                binding.controllerButton = VrControllerButtonId.RightGripTrigger;
                binding.label = "toggle_hud";
                if (binding.keyboardKey == KeyCode.None)
                {
                    binding.keyboardKey = KeyCode.H;
                }

                bindings[i] = binding;
            }
        }

        void RemoveLegacyCommandCursorBindings()
        {
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                if (bindings[i].action == VrActionId.SelectPreviousCommand || bindings[i].action == VrActionId.SelectNextCommand)
                {
                    bindings.RemoveAt(i);
                }
            }
        }

        void RemoveObsoleteExternalBindings()
        {
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                if ((int)bindings[i].action == ObsoleteExternalConsoleActionId)
                {
                    bindings.RemoveAt(i);
                }
            }
        }

        void MergeMissingBindings()
        {
            var defaults = BuildDefaultBindings();
            for (var i = 0; i < defaults.Count; i++)
            {
                var exists = false;
                for (var j = 0; j < bindings.Count; j++)
                {
                    if (bindings[j].action == defaults[i].action)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    bindings.Add(defaults[i]);
                }
            }
        }

        void MergeMissingCommands()
        {
            var defaults = BuildDefaultCommands();
            for (var i = 0; i < defaults.Count; i++)
            {
                var exists = false;
                for (var j = 0; j < commands.Count; j++)
                {
                    if (commands[j].action == defaults[i].action)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    commands.Add(defaults[i]);
                }
            }
        }

        static List<TerminalCommand> BuildDefaultCommands()
        {
            return new List<TerminalCommand>
            {
                new() { command = "polar_permissions", description = "request or re-check BLE permissions", action = VrTerminalCommandId.PolarRequestPermissions },
                new() { command = "polar_connect", description = "request BLE permissions and reconnect to Polar", action = VrTerminalCommandId.PolarConnect },
                new() { command = "polar_scan", description = "scan for nearby Polar H10 devices", action = VrTerminalCommandId.PolarScan },
                new() { command = "polar_clear_saved_device", description = "forget the saved Polar device address", action = VrTerminalCommandId.PolarClearSavedDevice },
                new() { command = "questionnaire_open", description = "open the standalone questionnaire panel", action = VrTerminalCommandId.QuestionnaireOpen },
                new() { command = "center_button", description = "place the button in front of the viewer", action = VrTerminalCommandId.CenterButton },
                new() { command = "press_button", description = "play the imported press animation once", action = VrTerminalCommandId.PressButton },
                new() { command = "blink_button", description = "blink the button for the default timed interval", action = VrTerminalCommandId.BlinkButton },
                new() { command = "stop_blink", description = "stop the timed button blink", action = VrTerminalCommandId.StopButtonBlink },
                new() { command = "reload_layout", description = "reload runtime layout config files", action = VrTerminalCommandId.ReloadLayout },
                new() { command = "layout_status", description = "log active runtime layout config paths and values", action = VrTerminalCommandId.LayoutStatus },
                new() { command = "toggle_hud", description = "show or hide the overlay", action = VrTerminalCommandId.ToggleHud },
                new() { command = "status", description = "log the button and Polar sensor status snapshot", action = VrTerminalCommandId.StatusSnapshot }
            };
        }

        void RemoveObsoleteExternalCommands()
        {
            for (var i = commands.Count - 1; i >= 0; i--)
            {
                var actionId = (int)commands[i].action;
                if (actionId >= FirstObsoleteExternalCommandId && actionId <= LastObsoleteExternalCommandId)
                {
                    commands.RemoveAt(i);
                }
            }
        }

        bool WasPressed(ActionBinding binding)
        {
            return WasControllerPressed(binding.controllerButton) || WasKeyboardPressed(binding.keyboardKey);
        }

        bool WasControllerPressed(VrControllerButtonId button)
        {
            if (button == VrControllerButtonId.RightIndexTrigger &&
                manualPressController != null &&
                manualPressController.ConsumesRightIndexTriggerInput)
            {
                return false;
            }

            return button switch
            {
                VrControllerButtonId.RightPrimaryButtonA => OVRInput.GetDown(OVRInput.RawButton.A),
                VrControllerButtonId.RightSecondaryButtonB => OVRInput.GetDown(OVRInput.RawButton.B),
                VrControllerButtonId.RightIndexTrigger => OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger),
                VrControllerButtonId.RightGripTrigger => OVRInput.GetDown(OVRInput.RawButton.RHandTrigger),
                VrControllerButtonId.RightThumbstickClick => OVRInput.GetDown(OVRInput.RawButton.RThumbstick),
                VrControllerButtonId.LeftPrimaryButtonX => OVRInput.GetDown(OVRInput.RawButton.X),
                VrControllerButtonId.LeftSecondaryButtonY => OVRInput.GetDown(OVRInput.RawButton.Y),
                VrControllerButtonId.LeftIndexTrigger => OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger),
                _ => false
            };
        }

        bool WasKeyboardPressed(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && TryMapInputSystemKey(keyCode, out var inputKey))
            {
                return keyboard[inputKey].wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static bool TryMapInputSystemKey(KeyCode keyCode, out Key key)
        {
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            {
                key = Key.A + (keyCode - KeyCode.A);
                return true;
            }

            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            {
                key = Key.Digit0 + (keyCode - KeyCode.Alpha0);
                return true;
            }

            switch (keyCode)
            {
                case KeyCode.UpArrow: key = Key.UpArrow; return true;
                case KeyCode.DownArrow: key = Key.DownArrow; return true;
                case KeyCode.LeftArrow: key = Key.LeftArrow; return true;
                case KeyCode.RightArrow: key = Key.RightArrow; return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: key = Key.Enter; return true;
                case KeyCode.Space: key = Key.Space; return true;
                case KeyCode.Escape: key = Key.Escape; return true;
                default: key = Key.None; return false;
            }
        }
#endif

        static string GetControllerButtonLabel(VrControllerButtonId button)
        {
            return button switch
            {
                VrControllerButtonId.RightPrimaryButtonA => "A",
                VrControllerButtonId.RightSecondaryButtonB => "B",
                VrControllerButtonId.RightIndexTrigger => "R Trigger",
                VrControllerButtonId.RightGripTrigger => "R Grip",
                VrControllerButtonId.RightThumbstickUp => "R Stick Up",
                VrControllerButtonId.RightThumbstickDown => "R Stick Down",
                VrControllerButtonId.RightThumbstickClick => "R Stick Click",
                VrControllerButtonId.LeftPrimaryButtonX => "X",
                VrControllerButtonId.LeftSecondaryButtonY => "Y",
                VrControllerButtonId.LeftIndexTrigger => "L Trigger",
                _ => "-"
            };
        }

        static string GetKeyboardLabel(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.None => "-",
                KeyCode.UpArrow => "Up",
                KeyCode.DownArrow => "Down",
                KeyCode.LeftArrow => "Left",
                KeyCode.RightArrow => "Right",
                KeyCode.Return => "Enter",
                KeyCode.KeypadEnter => "Enter",
                _ => keyCode.ToString()
            };
        }

        static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        void ArmStartupPlacement()
        {
            if (!Application.isPlaying || !placeButtonOnStartup)
            {
                _hasPlacedButtonOnStartup = true;
                return;
            }

            _hasPlacedButtonOnStartup = false;
            _startupPlacementTime = Time.unscaledTime + startupPlacementDelay;
        }

        void TryPlaceButtonOnStartup()
        {
            if (_hasPlacedButtonOnStartup || !placeButtonOnStartup || !Application.isPlaying)
            {
                return;
            }

            if (Time.unscaledTime < _startupPlacementTime)
            {
                return;
            }

            CenterButtonInFrontOfHead(reportToHud: false);
        }

        void TryKeepButtonInFrontOfHead()
        {
            if (!keepButtonInFrontOfHead ||
                !Application.isPlaying ||
                !_hasPlacedButtonOnStartup ||
                buttonTransform == null ||
                headTransform == null)
            {
                return;
            }

            if (!TryResolveButtonPoseInFrontOfHead(out var targetPosition, out var targetRotation))
            {
                return;
            }

            buttonTransform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        bool TryResolveButtonPoseInFrontOfHead(out Vector3 targetPosition, out Quaternion targetRotation)
        {
            targetPosition = default;
            targetRotation = default;

            if (buttonTransform == null || headTransform == null)
            {
                return false;
            }

            var horizontalForward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.001f)
            {
                horizontalForward = headTransform.forward;
            }

            if (horizontalForward.sqrMagnitude < 0.001f)
            {
                return false;
            }

            horizontalForward.Normalize();

            targetPosition = headTransform.position + horizontalForward * buttonDistanceFromHead;
            targetPosition.y = useAbsoluteButtonWorldY
                ? absoluteButtonWorldY
                : Mathf.Max(minimumButtonHeight, headTransform.position.y + buttonVerticalOffset);
            targetRotation = Quaternion.LookRotation(-horizontalForward, Vector3.up) * Quaternion.Euler(buttonRotationOffsetEuler);
            return true;
        }

        bool ReloadRuntimeLayoutConfigFromDisk(bool recenterButton, bool reportToHud)
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            var config = CaptureCurrentRuntimeLayoutConfig();
            var defaultsPath = ResolveRuntimeLayoutPath(runtimeLayoutDefaultsRelativePath, BigRedButtonRuntimeLayoutConfig.DefaultsRelativePath);
            var overridePath = ResolveRuntimeLayoutPath(runtimeLayoutOverrideRelativePath, BigRedButtonRuntimeLayoutConfig.RuntimeOverrideRelativePath);
            var source = "inspector";
            var loadedAnyFile = false;

            if (writeRuntimeLayoutDefaultsIfMissing)
            {
                TryWriteDefaultRuntimeLayoutConfig(defaultsPath, config);
            }

            if (TryReadRuntimeLayoutConfig(defaultsPath, config, out var defaultsConfig, out var defaultsError))
            {
                config = defaultsConfig;
                source = defaultsPath;
                loadedAnyFile = true;
            }
            else if (!string.IsNullOrWhiteSpace(defaultsError) && logRuntimeLayoutConfig)
            {
                Debug.LogWarning($"[BRBRuntimeLayout] Default config not loaded from '{defaultsPath}': {defaultsError}", this);
            }

            if (TryReadRuntimeLayoutConfig(overridePath, config, out var overrideConfig, out var overrideError))
            {
                config = overrideConfig;
                source = overridePath;
                loadedAnyFile = true;
            }
            else if (!string.IsNullOrWhiteSpace(overrideError) && logRuntimeLayoutConfig)
            {
                Debug.LogWarning($"[BRBRuntimeLayout] Runtime override not loaded from '{overridePath}': {overrideError}", this);
            }

            ApplyRuntimeLayoutConfig(config, recenterButton);
            _lastRuntimeLayoutSource = source;
            _lastRuntimeLayoutStatus =
                $"{(loadedAnyFile ? "loaded" : "using inspector defaults")} source={source} " +
                $"defaults={defaultsPath} override={overridePath} {config.ToCompactString()}";

            if (logRuntimeLayoutConfig)
            {
                Debug.Log($"[BRBRuntimeLayout] {_lastRuntimeLayoutStatus}", this);
            }

            if (reportToHud)
            {
                hud?.SetTransientMessage($"layout: {DescribeRuntimeLayoutCompact()}");
            }

            return true;
        }

        BigRedButtonRuntimeLayoutConfig CaptureCurrentRuntimeLayoutConfig()
        {
            var config = new BigRedButtonRuntimeLayoutConfig
            {
                counter_canvas_local_y_m = ResolveCounterCanvasLocalY(),
                button_height_m = targetButtonHeight,
                button_distance_from_head_m = buttonDistanceFromHead,
                button_vertical_offset_from_head_m = buttonVerticalOffset,
                minimum_button_world_y_m = minimumButtonHeight,
                use_absolute_button_world_y = useAbsoluteButtonWorldY,
                absolute_button_world_y_m = absoluteButtonWorldY,
                place_button_on_startup = placeButtonOnStartup,
                keep_button_in_front_of_head = keepButtonInFrontOfHead
            };
            config.Normalize();
            return config;
        }

        void ApplyRuntimeLayoutConfig(BigRedButtonRuntimeLayoutConfig config, bool recenterButton)
        {
            if (config == null)
            {
                return;
            }

            config.Normalize();
            _activeRuntimeLayoutConfig = config.Clone();
            targetButtonHeight = config.button_height_m;
            buttonDistanceFromHead = config.button_distance_from_head_m;
            buttonVerticalOffset = config.button_vertical_offset_from_head_m;
            minimumButtonHeight = config.minimum_button_world_y_m;
            useAbsoluteButtonWorldY = config.use_absolute_button_world_y;
            absoluteButtonWorldY = config.absolute_button_world_y_m;
            placeButtonOnStartup = config.place_button_on_startup;
            keepButtonInFrontOfHead = config.keep_button_in_front_of_head;

            ApplyCounterCanvasLocalY(config.counter_canvas_local_y_m);
            EnsureReasonableButtonScale();

            if (recenterButton && Application.isPlaying)
            {
                CenterButtonInFrontOfHead(reportToHud: false);
            }

            hud?.RefreshImmediately();
        }

        bool TryWriteDefaultRuntimeLayoutConfig(string path, BigRedButtonRuntimeLayoutConfig config)
        {
            if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
            {
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, BigRedButtonRuntimeLayoutConfig.ToJson(config), Encoding.UTF8);
                if (logRuntimeLayoutConfig)
                {
                    Debug.Log($"[BRBRuntimeLayout] Wrote default layout config '{path}'.", this);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BRBRuntimeLayout] Failed to write default layout config '{path}': {ex.Message}", this);
                return false;
            }
        }

        static bool TryReadRuntimeLayoutConfig(
            string path,
            BigRedButtonRuntimeLayoutConfig fallback,
            out BigRedButtonRuntimeLayoutConfig config,
            out string error)
        {
            config = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(path);
                if (!BigRedButtonRuntimeLayoutConfig.TryFromJson(json, fallback, out config, out error))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        string ResolveRuntimeLayoutPath(string configuredPath, string fallbackRelativePath)
        {
            var path = string.IsNullOrWhiteSpace(configuredPath) ? fallbackRelativePath : configuredPath.Trim();
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            var normalized = path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(Application.persistentDataPath, normalized);
        }

        float ResolveCounterCanvasLocalY()
        {
            var counterTransform = ResolveCounterTransform();
            return counterTransform != null
                ? counterTransform.localPosition.y
                : BigRedButtonRuntimeLayoutConfig.DefaultCounterCanvasLocalY;
        }

        void ApplyCounterCanvasLocalY(float localY)
        {
            var counterTransform = ResolveCounterTransform();
            if (counterTransform == null)
            {
                return;
            }

            var localPosition = counterTransform.localPosition;
            localPosition.y = localY;
            counterTransform.localPosition = localPosition;
        }

        Transform ResolveCounterTransform()
        {
            if (buttonTransform != null)
            {
                var counter = buttonTransform.Find("Button Press Counter");
                if (counter != null)
                {
                    return counter;
                }
            }

            var worldCounter = FindAnyObjectByType<BigRedButtonWorldPressCounter>();
            return worldCounter != null ? worldCounter.transform : null;
        }

        string DescribeRuntimeLayoutCompact()
        {
            var config = _activeRuntimeLayoutConfig ?? CaptureCurrentRuntimeLayoutConfig();
            return config.ToCompactString();
        }

        void LogRuntimeLayoutStatus(bool reportToHud)
        {
            var message = string.IsNullOrWhiteSpace(_lastRuntimeLayoutStatus)
                ? $"source={_lastRuntimeLayoutSource} {DescribeRuntimeLayoutCompact()}"
                : _lastRuntimeLayoutStatus;
            Debug.Log($"[BRBRuntimeLayout] {message}", this);
            if (reportToHud)
            {
                hud?.SetTransientMessage($"layout: {DescribeRuntimeLayoutCompact()}");
            }
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (hud == null)
            {
                hud = GetComponentInChildren<QuestVrOverlayHud>(true);
            }

            if (headTransform == null || forceRefresh)
            {
                var cameraRig = FindAnyObjectByType<OVRCameraRig>();
                if (cameraRig != null)
                {
                    headTransform = cameraRig.centerEyeAnchor;
                }
            }

            if ((headTransform == null || forceRefresh) && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }

            if (buttonTransform == null || forceRefresh)
            {
                var buttonObject = GameObject.Find(ButtonObjectName);
                if (buttonObject != null)
                {
                    buttonTransform = buttonObject.transform;
                }
            }

            if ((buttonAnimationTester == null || forceRefresh) && buttonTransform != null)
            {
                buttonAnimationTester = buttonTransform.GetComponent<BigRedButtonAnimationTester>();
            }

            if ((buttonBlinkController == null || forceRefresh) && buttonTransform != null)
            {
                buttonBlinkController = buttonTransform.GetComponent<BigRedButtonBlinkController>();
            }

            if ((manualPressController == null || forceRefresh) && buttonTransform != null)
            {
                manualPressController = buttonTransform.GetComponent<BigRedButtonManualPressController>();
            }

            if ((polarRuntimeManager == null || forceRefresh) && Application.isPlaying)
            {
                polarRuntimeManager = PolarH10RuntimeManager.EnsureRuntimeExists();
            }

            if ((polarRuntimeManager == null || forceRefresh) && !Application.isPlaying)
            {
                polarRuntimeManager = FindAnyObjectByType<PolarH10RuntimeManager>();
            }

            if (polarHeartbeatButtonDriver == null || forceRefresh)
            {
                polarHeartbeatButtonDriver = GetComponent<PolarHeartbeatButtonDriver>();
                if (polarHeartbeatButtonDriver == null)
                {
                    polarHeartbeatButtonDriver = FindAnyObjectByType<PolarHeartbeatButtonDriver>();
                }
            }

            if (diagnosticComparisonController == null || forceRefresh)
            {
                diagnosticComparisonController = GetComponent<BigRedButtonDiagnosticComparisonController>();
                if (diagnosticComparisonController == null)
                {
                    diagnosticComparisonController = FindAnyObjectByType<BigRedButtonDiagnosticComparisonController>();
                }
            }

            if (questionnaireLauncher == null || forceRefresh)
            {
                questionnaireLauncher = GetComponent<QuestQuestionnairePanelLauncher>();
                if (questionnaireLauncher == null)
                {
                    questionnaireLauncher = FindAnyObjectByType<QuestQuestionnairePanelLauncher>();
                }
            }
        }

        void TryConfigureSimultaneousHandsAndControllers()
        {
            if (!enableSimultaneousHandsAndControllers || _hasConfiguredSimultaneousHandsAndControllers)
            {
                return;
            }

            var manager = OVRManager.instance;
            if (manager == null)
            {
                return;
            }

            manager.launchSimultaneousHandsControllersOnStartup = true;
            manager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            manager.SimultaneousHandsAndControllersEnabled = true;
            OVRInput.EnableSimultaneousHandsAndControllers();
            _hasConfiguredSimultaneousHandsAndControllers = true;
        }
    }
}

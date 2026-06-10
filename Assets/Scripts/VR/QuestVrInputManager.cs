using System;
using System.Collections.Generic;
using System.Text;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.Diagnostics;
using TheBigRedButtonInstitute.RustyXrBroker;
using UnityEngine;
using UnityEngine.XR;
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
        const string BrokerOpenUiLaunchExtra = "rustyxr.brokerOpenUi";

        public enum VrActionId
        {
            None = 0,
            ToggleHud = 1,
            SelectPreviousCommand = 2,
            SelectNextCommand = 3,
            ExecuteSelectedCommand = 4,
            CenterButton = 5,
            ReplayButtonPress = 6,
            OpenBrokerConsole = 7
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
            BrokerStatus = 9,
            BrokerConnect = 10,
            BrokerSubscribe = 11,
            BrokerDriveButton = 12,
            BrokerOpenUi = 13,
            BrokerCloseUi = 14,
            BrokerPolarHeartRateStart = 15,
            BrokerPolarPmdStart = 16,
            BrokerPolarStop = 17
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
        [SerializeField] RustyXrBrokerClient brokerClient;
        [SerializeField] RustyXrBrokerButtonDriver brokerButtonDriver;
        [SerializeField] QuestVrRustyXrBrokerButtonBridge brokerButtonBridge;
        [SerializeField] BigRedButtonDiagnosticComparisonController diagnosticComparisonController;

        [Header("Behavior")]
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool placeButtonOnStartup = true;
        [SerializeField] bool keepButtonInFrontOfHead = true;
        [SerializeField] bool enableSimultaneousHandsAndControllers = true;
        [SerializeField, Min(0f)] float startupPlacementDelay = 0.2f;
        [SerializeField, Min(0.2f)] float buttonDistanceFromHead = 0.48f;
        [SerializeField] float buttonVerticalOffset = -0.62f;
        [SerializeField, Min(0.4f)] float minimumButtonHeight = 0.54f;
        [SerializeField, Min(0.1f)] float targetButtonHeight = 0.36f;
        [SerializeField] Vector3 buttonRotationOffsetEuler;
        [SerializeField] bool allowBrokerOpenUiLaunchExtra = true;

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
        bool _hasPlacedButtonOnStartup;
        bool _hasConfiguredSimultaneousHandsAndControllers;
        bool _brokerOpenUiLaunchExtraChecked;
        bool _brokerOpenUiLaunchExtraPending;
        bool _rightPrimaryBrokerOpenWasPressed;
        bool _brokerConsoleCloseProbeArmed;
        float _startupPlacementTime;
        double _nextBrokerOpenUiLaunchAttempt;
        int _brokerOpenShortcutFrame = -1;

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
            hud?.ConfigureReferences(this, headTransform);
            EnsureReasonableButtonScale();
            TryConfigureSimultaneousHandsAndControllers();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
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
            TryHandleBrokerOpenUiLaunchExtra();
            ProcessHudNavigation();
            ProcessBrokerOpenUiPrimaryShortcut();
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
            hud?.ConfigureReferences(this, headTransform);
            ArmStartupPlacement();
        }

        public void ConfigurePolarReferences(PolarH10RuntimeManager runtimeManager, PolarHeartbeatButtonDriver heartbeatButtonDriver)
        {
            polarRuntimeManager = runtimeManager;
            polarHeartbeatButtonDriver = heartbeatButtonDriver;
        }

        public void ConfigureBrokerReferences(
            RustyXrBrokerClient client,
            RustyXrBrokerButtonDriver buttonDriver,
            QuestVrRustyXrBrokerButtonBridge buttonBridge)
        {
            brokerClient = client;
            brokerButtonDriver = buttonDriver;
            brokerButtonBridge = buttonBridge;
        }

        public void ConfigureDiagnosticReferences(BigRedButtonDiagnosticComparisonController comparisonController)
        {
            diagnosticComparisonController = comparisonController;
        }

        public void EnsureConfiguration()
        {
            bindings ??= new List<ActionBinding>();
            commands ??= new List<TerminalCommand>();
            MigrateLegacyBindings();
            RemoveLegacyCommandCursorBindings();
            MergeMissingBindings();
            EnsureBrokerOpenUiPrimaryButtonBinding();
            MergeMissingCommands();
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

            if (buttonBlinkController != null)
            {
                buttonBlinkController.PulseOnce();
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

                if (binding.action == VrActionId.OpenBrokerConsole && _brokerOpenShortcutFrame == Time.frameCount)
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
                case VrActionId.OpenBrokerConsole:
                    ToggleBrokerConsoleShortcut("binding", primaryShortcut: false);
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
                case VrTerminalCommandId.ToggleHud:
                    ExecuteAction(VrActionId.ToggleHud);
                    break;
                case VrTerminalCommandId.StatusSnapshot:
                    var snapshot = BuildStatusSummary();
                    Debug.Log($"[QuestVrInputManager] {snapshot}", this);
                    hud?.SetTransientMessage($"status: {snapshot}");
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
                case VrTerminalCommandId.BrokerStatus:
                    if (brokerClient == null)
                    {
                        hud?.SetTransientMessage("broker_status failed: client missing");
                        break;
                    }

                    brokerClient.RequestStatus();
                    var brokerStatus = brokerClient.BuildStatusLabel();
                    Debug.Log($"[QuestVrInputManager] broker {brokerStatus}", this);
                    hud?.SetTransientMessage($"broker: {brokerStatus}");
                    break;
                case VrTerminalCommandId.BrokerConnect:
                    if (brokerClient == null)
                    {
                        hud?.SetTransientMessage("broker_connect failed: client missing");
                        break;
                    }

                    brokerClient.ConnectNow();
                    hud?.SetTransientMessage("broker_connect requested");
                    break;
                case VrTerminalCommandId.BrokerSubscribe:
                    if (brokerClient == null)
                    {
                        hud?.SetTransientMessage("broker_subscribe failed: client missing");
                        break;
                    }

                    var subscriptions = brokerClient.SubscribeToDefaultStreams();
                    hud?.SetTransientMessage($"broker_subscribe requested: {subscriptions}");
                    break;
                case VrTerminalCommandId.BrokerOpenUi:
                    OpenBrokerConsole();
                    break;
                case VrTerminalCommandId.BrokerCloseUi:
                    CloseBrokerConsole("terminal");
                    break;
                case VrTerminalCommandId.BrokerPolarHeartRateStart:
                    if (brokerClient == null)
                    {
                        hud?.SetTransientMessage("broker_polar_hr_start failed: client missing");
                        break;
                    }

                    if (brokerClient.StartPolarHeartRate())
                    {
                        hud?.SetTransientMessage("broker_polar_hr_start requested");
                    }
                    else
                    {
                        hud?.SetTransientMessage($"broker_polar_hr_start failed: {brokerClient.LastError}");
                    }

                    break;
                case VrTerminalCommandId.BrokerPolarPmdStart:
                    if (brokerClient == null)
                    {
                        hud?.SetTransientMessage("broker_polar_pmd_start failed: client missing");
                        break;
                    }

                    if (brokerClient.StartPolarPmd())
                    {
                        hud?.SetTransientMessage("broker_polar_pmd_start requested");
                    }
                    else
                    {
                        hud?.SetTransientMessage($"broker_polar_pmd_start failed: {brokerClient.LastError}");
                    }

                    break;
                case VrTerminalCommandId.BrokerPolarStop:
                    if (brokerClient == null)
                    {
                        hud?.SetTransientMessage("broker_polar_stop failed: client missing");
                        break;
                    }

                    if (brokerClient.StopPolarSources())
                    {
                        hud?.SetTransientMessage("broker_polar_stop requested");
                    }
                    else
                    {
                        hud?.SetTransientMessage($"broker_polar_stop failed: {brokerClient.LastError}");
                    }

                    break;
                case VrTerminalCommandId.BrokerDriveButton:
                    if (brokerButtonDriver == null)
                    {
                        hud?.SetTransientMessage("broker_drive_button failed: driver missing");
                        break;
                    }

                    if (brokerButtonDriver.ApplyBrokerDriveValue(1f, Time.unscaledTimeAsDouble, true))
                    {
                        hud?.SetTransientMessage("broker_drive_button executed");
                    }
                    else
                    {
                        hud?.SetTransientMessage($"broker_drive_button ignored: {brokerButtonDriver.DriveStateLabel}");
                    }

                    break;
            }
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

        void OpenBrokerConsole()
        {
            if (brokerClient == null)
            {
                hud?.SetTransientMessage("broker_open_ui failed: client missing");
                return;
            }

            if (!brokerClient.IsConnected)
            {
                brokerClient.ConnectNow();
                hud?.SetTransientMessage("broker_open_ui waiting for connection");
                return;
            }

            if (brokerClient.OpenBrokerConsole())
            {
                _brokerConsoleCloseProbeArmed = true;
                hud?.SetTransientMessage("broker_open_ui requested");
                Debug.Log("[QuestVrInputManager] broker_open_ui requested", this);
            }
            else
            {
                hud?.SetTransientMessage($"broker_open_ui failed: {brokerClient.LastError}");
            }
        }

        void CloseBrokerConsole(string source)
        {
            if (brokerClient == null)
            {
                hud?.SetTransientMessage("broker_close_ui failed: client missing");
                return;
            }

            if (!brokerClient.IsConnected)
            {
                brokerClient.ConnectNow();
                hud?.SetTransientMessage("broker_close_ui waiting for connection");
                return;
            }

            if (brokerClient.CloseBrokerConsole())
            {
                _brokerConsoleCloseProbeArmed = false;
                hud?.SetTransientMessage("broker_close_ui requested");
                Debug.Log($"[QuestVrInputManager] broker_close_ui requested source={source}", this);
            }
            else
            {
                hud?.SetTransientMessage($"broker_close_ui failed: {brokerClient.LastError}");
            }
        }

        void ProcessBrokerOpenUiPrimaryShortcut()
        {
            var pressed = TryReadRightPrimaryBrokerOpenPressed(out var source);
            if (pressed && !_rightPrimaryBrokerOpenWasPressed)
            {
                _brokerOpenShortcutFrame = Time.frameCount;
                ToggleBrokerConsoleShortcut(source, primaryShortcut: true);
            }

            _rightPrimaryBrokerOpenWasPressed = pressed;
        }

        void ToggleBrokerConsoleShortcut(string source, bool primaryShortcut)
        {
            var prefix = primaryShortcut ? "primary shortcut" : "shortcut";
            if (_brokerConsoleCloseProbeArmed)
            {
                Debug.Log($"[QuestVrInputManager] broker_close_ui {prefix} source={source}", this);
                CloseBrokerConsole(primaryShortcut ? $"primary:{source}" : $"shortcut:{source}");
            }
            else
            {
                Debug.Log($"[QuestVrInputManager] broker_open_ui {prefix} source={source}", this);
                OpenBrokerConsole();
            }
        }

        static bool TryReadRightPrimaryBrokerOpenPressed(out string source)
        {
            if (OVRInput.Get(OVRInput.RawButton.A))
            {
                source = "ovr_raw_a";
                return true;
            }

            if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                source = "ovr_button_one_right";
                return true;
            }

            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid &&
                rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out var primaryButton) &&
                primaryButton)
            {
                source = "xr_common_primary_right";
                return true;
            }

            source = "";
            return false;
        }

        void TryHandleBrokerOpenUiLaunchExtra()
        {
            if (!allowBrokerOpenUiLaunchExtra)
            {
                return;
            }

            if (!_brokerOpenUiLaunchExtraChecked)
            {
                _brokerOpenUiLaunchExtraChecked = true;
                _brokerOpenUiLaunchExtraPending = ReadBrokerOpenUiLaunchExtra();
                if (_brokerOpenUiLaunchExtraPending)
                {
                    Debug.Log("[QuestVrInputManager] broker_open_ui launch extra received", this);
                }
            }

            if (!_brokerOpenUiLaunchExtraPending)
            {
                return;
            }

            if (Time.unscaledTimeAsDouble < _nextBrokerOpenUiLaunchAttempt)
            {
                return;
            }

            _nextBrokerOpenUiLaunchAttempt = Time.unscaledTimeAsDouble + 0.5d;
            if (brokerClient == null)
            {
                return;
            }

            if (!brokerClient.IsConnected)
            {
                brokerClient.ConnectNow();
                return;
            }

            if (brokerClient.OpenBrokerConsole())
            {
                _brokerConsoleCloseProbeArmed = true;
                _brokerOpenUiLaunchExtraPending = false;
                hud?.SetTransientMessage("broker_open_ui launch extra requested");
                Debug.Log("[QuestVrInputManager] broker_open_ui requested from launch extra", this);
            }
        }

        static bool ReadBrokerOpenUiLaunchExtra()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity?.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>("getBooleanExtra", BrokerOpenUiLaunchExtra, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestVrInputManager] broker_open_ui launch extra check failed: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
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

            if (polarRuntimeManager != null)
            {
                summary.Append(" / perms ");
                summary.Append(polarRuntimeManager.GetBlePermissionStatusLabel());
                summary.Append(" / ");
                summary.Append(polarRuntimeManager.BuildPlainStatusSummary());
            }

            if (brokerClient != null)
            {
                summary.Append(" / broker ");
                summary.Append(brokerClient.BuildStatusLabel());
            }

            return summary.ToString();
        }

        void AppendDashboardPage(StringBuilder builder)
        {
            AppendButtonSection(builder);
            builder.AppendLine();
            AppendBrokerSection(builder);
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
            AppendBrokerSection(builder);
            builder.AppendLine();
            AppendDiagnosticSection(builder);
        }

        void AppendBrokerSection(StringBuilder builder)
        {
            builder.AppendLine("<b><color=#66FFCC>[RUSTY XR BROKER]</color></b>");
            if (brokerClient == null)
            {
                builder.AppendLine("<color=#AFC0CF>Status:</color> <color=#97A9B6>client unavailable</color>");
                return;
            }

            builder.AppendLine($"<color=#AFC0CF>Status:</color> <color=#EAF6FF>{EscapeRichText(brokerClient.BuildStatusLabel())}</color>");
            builder.AppendLine($"<color=#AFC0CF>Last:</color> <color=#EAF6FF>{EscapeRichText(brokerClient.LastStatus)}</color>");
            if (brokerButtonDriver != null)
            {
                builder.AppendLine($"<color=#AFC0CF>Drive:</color> <color=#EAF6FF>{EscapeRichText(brokerButtonDriver.DriveStateLabel)}</color>");
                builder.AppendLine($"<color=#AFC0CF>Drive pulses:</color> <color=#EAF6FF>{brokerButtonDriver.TriggerCount:N0}</color>");
            }

            if (brokerButtonBridge != null)
            {
                builder.AppendLine($"<color=#AFC0CF>Button bridge:</color> <color=#EAF6FF>{EscapeRichText(brokerButtonBridge.LastState)}</color>");
            }
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
            if (brokerButtonDriver != null)
            {
                return $"broker {brokerButtonDriver.DriveStateLabel}";
            }

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
                new() { controllerButton = VrControllerButtonId.RightPrimaryButtonA, keyboardKey = KeyCode.O, action = VrActionId.OpenBrokerConsole, label = "broker_open_ui" }
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

        void EnsureBrokerOpenUiPrimaryButtonBinding()
        {
            var foundOpenUi = false;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.action == VrActionId.OpenBrokerConsole)
                {
                    binding.controllerButton = VrControllerButtonId.RightPrimaryButtonA;
                    binding.keyboardKey = KeyCode.O;
                    binding.label = "broker_open_ui";
                    bindings[i] = binding;
                    foundOpenUi = true;
                    continue;
                }

                if (binding.controllerButton == VrControllerButtonId.RightPrimaryButtonA)
                {
                    binding.controllerButton = binding.action == VrActionId.CenterButton
                        ? VrControllerButtonId.RightSecondaryButtonB
                        : VrControllerButtonId.None;
                    bindings[i] = binding;
                }
            }

            if (!foundOpenUi)
            {
                bindings.Add(new ActionBinding
                {
                    controllerButton = VrControllerButtonId.RightPrimaryButtonA,
                    keyboardKey = KeyCode.O,
                    action = VrActionId.OpenBrokerConsole,
                    label = "broker_open_ui"
                });
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
                new() { command = "broker_status", description = "request broker status", action = VrTerminalCommandId.BrokerStatus },
                new() { command = "broker_connect", description = "connect the localhost broker client", action = VrTerminalCommandId.BrokerConnect },
                new() { command = "broker_open_ui", description = "open the broker 2D console", action = VrTerminalCommandId.BrokerOpenUi },
                new() { command = "broker_close_ui", description = "close the broker 2D console", action = VrTerminalCommandId.BrokerCloseUi },
                new() { command = "broker_subscribe", description = "subscribe to broker test streams", action = VrTerminalCommandId.BrokerSubscribe },
                new() { command = "broker_polar_hr_start", description = "start Gargoyle Polar HR/RR source", action = VrTerminalCommandId.BrokerPolarHeartRateStart },
                new() { command = "broker_polar_pmd_start", description = "start Gargoyle Polar PMD ACC source", action = VrTerminalCommandId.BrokerPolarPmdStart },
                new() { command = "broker_polar_stop", description = "stop Gargoyle Polar sources", action = VrTerminalCommandId.BrokerPolarStop },
                new() { command = "broker_drive_button", description = "drive the button from broker path", action = VrTerminalCommandId.BrokerDriveButton },
                new() { command = "center_button", description = "place the button in front of the viewer", action = VrTerminalCommandId.CenterButton },
                new() { command = "press_button", description = "play the imported press animation once", action = VrTerminalCommandId.PressButton },
                new() { command = "toggle_hud", description = "show or hide the overlay", action = VrTerminalCommandId.ToggleHud },
                new() { command = "status", description = "log the button and Polar sensor status snapshot", action = VrTerminalCommandId.StatusSnapshot }
            };
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
            targetPosition.y = Mathf.Max(minimumButtonHeight, headTransform.position.y + buttonVerticalOffset);
            targetRotation = Quaternion.LookRotation(-horizontalForward, Vector3.up) * Quaternion.Euler(buttonRotationOffsetEuler);
            return true;
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

            if (brokerClient == null || forceRefresh)
            {
                brokerClient = GetComponent<RustyXrBrokerClient>();
                if (brokerClient == null)
                {
                    brokerClient = FindAnyObjectByType<RustyXrBrokerClient>();
                }
            }

            if (brokerButtonDriver == null || forceRefresh)
            {
                brokerButtonDriver = GetComponent<RustyXrBrokerButtonDriver>();
                if (brokerButtonDriver == null)
                {
                    brokerButtonDriver = FindAnyObjectByType<RustyXrBrokerButtonDriver>();
                }
            }

            if (brokerButtonBridge == null || forceRefresh)
            {
                brokerButtonBridge = GetComponent<QuestVrRustyXrBrokerButtonBridge>();
                if (brokerButtonBridge == null)
                {
                    brokerButtonBridge = FindAnyObjectByType<QuestVrRustyXrBrokerButtonBridge>();
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

using System;
using System.Collections.Generic;
using System.Text;
using TheBigRedButtonInstitute.Biofeedback;
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
            PolarRequestPermissions = 8
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
            LeftIndexTrigger = 9
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
        [SerializeField] PolarH10RuntimeManager polarRuntimeManager;
        [SerializeField] PolarHeartbeatButtonDriver polarHeartbeatButtonDriver;

        [Header("Behavior")]
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField, Min(0.6f)] float buttonDistanceFromHead = 1.35f;
        [SerializeField] float buttonVerticalOffset = -0.35f;
        [SerializeField, Min(0.4f)] float minimumButtonHeight = 0.8f;
        [SerializeField, Min(0.1f)] float targetButtonHeight = 0.36f;
        [SerializeField] Vector3 buttonRotationOffsetEuler;

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

        public IReadOnlyList<ActionBinding> Bindings => bindings;
        public IReadOnlyList<TerminalCommand> Commands => commands;
        public int ButtonPressCount => _buttonPressCount;
        public BigRedButtonAnimationTester ButtonAnimationTester => buttonAnimationTester;

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
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            hud?.ConfigureReferences(this, headTransform);
            hud?.RefreshImmediately();
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);
            ProcessHudNavigation();
            ProcessBindings();
        }

        public void ConfigureReferences(QuestVrOverlayHud targetHud, Transform head, Transform button, BigRedButtonAnimationTester tester)
        {
            hud = targetHud;
            headTransform = head;
            buttonTransform = button;
            buttonAnimationTester = tester;
            EnsureConfiguration();
            hud?.ConfigureReferences(this, headTransform);
        }

        public void ConfigurePolarReferences(PolarH10RuntimeManager runtimeManager, PolarHeartbeatButtonDriver heartbeatButtonDriver)
        {
            polarRuntimeManager = runtimeManager;
            polarHeartbeatButtonDriver = heartbeatButtonDriver;
        }

        public void EnsureConfiguration()
        {
            bindings ??= new List<ActionBinding>();
            commands ??= new List<TerminalCommand>();
            RemoveLegacyCommandCursorBindings();
            MergeMissingBindings();
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
            ResolveReferences(forceRefresh: false);
            if (buttonTransform == null || headTransform == null)
            {
                hud?.SetTransientMessage("center_button failed: missing references");
                return;
            }

            EnsureReasonableButtonScale();

            var horizontalForward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.001f)
            {
                horizontalForward = headTransform.forward;
            }

            horizontalForward.Normalize();

            var targetPosition = headTransform.position + horizontalForward * buttonDistanceFromHead;
            targetPosition.y = Mathf.Max(minimumButtonHeight, headTransform.position.y + buttonVerticalOffset);

            buttonTransform.position = targetPosition;
            buttonTransform.rotation = Quaternion.LookRotation(-horizontalForward, Vector3.up) * Quaternion.Euler(buttonRotationOffsetEuler);

            hud?.SetTransientMessage("center_button executed");
            hud?.RefreshImmediately();
        }

        public bool TriggerButtonPressFromRuntime()
        {
            ResolveReferences(forceRefresh: false);
            if (buttonAnimationTester == null)
            {
                return false;
            }

            buttonAnimationTester.PlayPressed();
            _buttonPressCount++;
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
            }
        }

        void ReplayButtonPress()
        {
            if (!TriggerButtonPressFromRuntime())
            {
                hud?.SetTransientMessage("press_button failed: no animation tester");
                return;
            }

            hud?.SetTransientMessage("press_button executed");
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

            return summary.ToString();
        }

        void AppendDashboardPage(StringBuilder builder)
        {
            AppendButtonSection(builder);
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
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        static List<ActionBinding> BuildDefaultBindings()
        {
            return new List<ActionBinding>
            {
                new() { controllerButton = VrControllerButtonId.RightPrimaryButtonA, keyboardKey = KeyCode.C, action = VrActionId.CenterButton, label = "center_button" },
                new() { controllerButton = VrControllerButtonId.RightSecondaryButtonB, keyboardKey = KeyCode.H, action = VrActionId.ToggleHud, label = "toggle_hud" },
                new() { controllerButton = VrControllerButtonId.RightIndexTrigger, keyboardKey = KeyCode.Return, action = VrActionId.ExecuteSelectedCommand, label = "execute_command" }
            };
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
            return button switch
            {
                VrControllerButtonId.RightPrimaryButtonA => OVRInput.GetDown(OVRInput.RawButton.A),
                VrControllerButtonId.RightSecondaryButtonB => OVRInput.GetDown(OVRInput.RawButton.B),
                VrControllerButtonId.RightIndexTrigger => OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger),
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
        }
    }
}
